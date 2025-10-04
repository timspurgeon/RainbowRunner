using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Server.Common;
using Server.Net;
using System.IO;

namespace Server.Game
{
    public class GameServer
    {
        private readonly NetServer net;
        private readonly string bindIp;
        private readonly int port;

        private static int NextConnId = 1;
        private readonly ConcurrentDictionary<int, RRConnection> _connections = new();
        private readonly ConcurrentDictionary<int, string> _users = new();
        private readonly ConcurrentDictionary<int, uint> _peerId24 = new();
        private readonly ConcurrentDictionary<int, List<Server.Game.GCObject>> _playerCharacters = new();

        private readonly ConcurrentDictionary<int, bool> _charListSent = new();
        private readonly ConcurrentDictionary<string, List<Server.Game.GCObject>> _persistentCharacters = new();

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Server.Game.GCObject> _selectedCharacter = new();

        private bool _gameLoopRunning = false;
        private readonly object _gameLoopLock = new object();

        private static void WriteCString(LEWriter w, string s)
        {
            var bytes = Encoding.ASCII.GetBytes(s ?? string.Empty);
            w.WriteBytes(bytes);
            w.WriteByte(0);
        }

        private const bool DUPLICATE_AVATAR_RECORD = false;
        private const uint MSG_DEST = 0x000F01;
        private const uint MSG_SOURCE = 0x007BDD;

        public GameServer(string ip, int port)
        {
            bindIp = ip;
            this.port = port;
            net = new NetServer(ip, port, HandleClient);
            Debug.Log($"[INIT] DFC Active Version set to 0x{GCObject.DFC_VERSION:X2}");
        }

        public Task RunAsync()
        {
            Debug.Log($"<color=#9f9>[Game]</color> Listening on {bindIp}:{port}");
            StartGameLoop();
            return net.RunAsync();
        }

        private void StartGameLoop()
        {
            lock (_gameLoopLock)
            {
                if (_gameLoopRunning) return;
                _gameLoopRunning = true;
            }

            Task.Run(async () =>
            {
                Debug.Log("[Game] Game loop started");
                while (_gameLoopRunning)
                {
                    try { await Task.Delay(16); }
                    catch (Exception ex) { Debug.LogError($"[Game] Game loop error: {ex}"); }
                }
                Debug.Log("[Game] Game loop stopped");
            });
        }

        private async Task HandleClient(TcpClient c)
        {
            var ep = c.Client.RemoteEndPoint?.ToString() ?? "unknown";
            int connId = Interlocked.Increment(ref NextConnId);
            Debug.Log($"<color=#9f9>[Game]</color> Connection from {ep} (ID={connId})");
            c.NoDelay = true;
            using var s = c.GetStream();
            var rrConn = new RRConnection(connId, c, s);
            _connections[connId] = rrConn;

            try
            {
                byte[] buffer = new byte[10240];
                while (rrConn.IsConnected)
                {
                    int bytesRead = await s.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    byte[] receivedData = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, receivedData, 0, bytesRead);
                    await ProcessReceivedData(rrConn, receivedData);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] Exception: {ex.Message}");
            }
            finally
            {
                rrConn.IsConnected = false;
                _connections.TryRemove(connId, out _);
                _users.TryRemove(connId, out _);
                _peerId24.TryRemove(connId, out _);
                _playerCharacters.TryRemove(connId, out _);
            }
        }

        private async Task ProcessReceivedData(RRConnection conn, byte[] data)
        {
            var reader = new LEReader(data);
            byte msgType = reader.ReadByte();

            switch (msgType)
            {
                case 0x0A: await HandleCompressedA(conn, reader); break;
                case 0x0E: await HandleCompressedE(conn, reader); break;
                case 0x06: await HandleType06(conn, reader); break;
                case 0x31: await HandleType31(conn, reader); break;
            }
        }

        private async Task HandleCompressedA(RRConnection conn, LEReader reader)
        {
            if (reader.Remaining < 7) return;
            uint peer = reader.ReadUInt24();
            _peerId24[conn.ConnId] = peer;
            reader.ReadUInt32();
            byte dest = reader.ReadByte();
            byte sub = reader.ReadByte();
            reader.ReadByte();
            uint unclen = reader.ReadUInt32();
            byte[] comp = reader.ReadBytes(reader.Remaining);
            byte[] inner = ZlibUtil.Inflate(comp, unclen);
            await ProcessUncompressedMessage(conn, dest, sub, inner);
        }

        private async Task HandleCompressedE(RRConnection conn, LEReader reader)
        {
            if (reader.Remaining < 15) return;
            reader.ReadUInt24();
            reader.ReadUInt24();
            reader.ReadByte();
            reader.ReadUInt24();
            reader.ReadBytes(5);
            uint unclen = reader.ReadUInt32();
            byte[] comp = reader.ReadBytes(reader.Remaining);
            byte[] inner = ZlibUtil.Inflate(comp, unclen);
            await ProcessUncompressedEMessage(conn, inner);
        }

        private async Task ProcessUncompressedMessage(RRConnection conn, byte dest, byte msgTypeA, byte[] uncompressed)
        {
            switch (msgTypeA)
            {
                case 0x00: await HandleInitialLogin(conn, uncompressed); break;
                case 0x0F: await HandleChannelMessage(conn, uncompressed); break;
            }
        }

        private async Task ProcessUncompressedEMessage(RRConnection conn, byte[] inner)
        {
            if (inner.Length < 2) return;
            byte channel = inner[0];
            byte type = inner[1];
            if (channel == 0 && type == 2)
            {
                var ack = new LEWriter();
                ack.WriteByte(0x00);
                ack.WriteByte(0x02);
                await SendCompressedEResponseWithDump(conn, ack.ToArray(), "e_ack_0_2");
                return;
            }
            await HandleChannelMessage(conn, inner);
        }

        private async Task HandleInitialLogin(RRConnection conn, byte[] data)
        {
            if (data.Length < 5) return;
            var reader = new LEReader(data);
            byte subtype = reader.ReadByte();
            uint oneTimeKey = reader.ReadUInt32();
            if (!GlobalSessions.TryConsume(oneTimeKey, out var user)) return;
            conn.LoginName = user;
            _users[conn.ConnId] = user;
            await StartCharacterFlow(conn);
        }

        private async Task HandleChannelMessage(RRConnection conn, byte[] data)
        {
            if (data.Length < 2) return;
            byte channel = data[0];
            byte messageType = data[1];

            switch (channel)
            {
                case 4:
                    switch (messageType)
                    {
                        case 0: await SendCharacterConnectedResponse(conn); break;
                        case 3: await SendCharacterList(conn); break;
                        case 5: await HandleCharacterPlay(conn, data); break;
                        case 2: await HandleCharacterCreate(conn, data); break;
                    }
                    break;
                case 9: await HandleGroupChannelMessages(conn, messageType); break;
                case 13: await HandleZoneChannelMessages(conn, messageType, data); break;
            }
        }

        private async Task StartCharacterFlow(RRConnection conn)
        {
            await EnsurePeerThenSendCharConnected(conn);
        }

        private async Task EnsurePeerThenSendCharConnected(RRConnection conn)
        {
            await SendCharacterConnectedResponse(conn);
        }

        private async Task HandleGroupChannelMessages(RRConnection conn, byte messageType)
        {
            if (messageType == 0) await SendGoToZone_V2(conn, "Town");
        }

        private async Task SendCharacterConnectedResponse(RRConnection conn)
        {
            const int count = 2;
            if (!_persistentCharacters.ContainsKey(conn.LoginName))
            {
                _persistentCharacters[conn.LoginName] = new List<Server.Game.GCObject>(count);
                for (int i = 0; i < count; i++)
                {
                    var p = Server.Game.Objects.NewPlayer(conn.LoginName);
                    p.ID = (uint)Server.Game.Objects.NewID();
                    _persistentCharacters[conn.LoginName].Add(p);
                }
            }
            var body = new LEWriter();
            body.WriteByte(4);
            body.WriteByte(0);
            await SendCompressedEResponseWithDump(conn, body.ToArray(), "char_connected");
        }

        private async Task SendCharacterList(RRConnection conn)
        {
            if (!_persistentCharacters.TryGetValue(conn.LoginName, out var chars)) return;
            var body = new LEWriter();
            body.WriteByte(4);
            body.WriteByte(3);
            body.WriteByte((byte)chars.Count);
            foreach (var character in chars)
            {
                body.WriteUInt32(character.ID);
                WriteGoSendPlayer(body, character);
            }
            await SendCompressedEResponseWithDump(conn, body.ToArray(), "charlist");
            _charListSent[conn.ConnId] = true;
        }

        private void WriteGoSendPlayer(LEWriter body, Server.Game.GCObject character)
        {
            var avatar = Server.Game.Objects.LoadAvatar();
            character.AddChild(avatar);
            var procMod = Server.Game.Objects.NewProcModifier();
            character.AddChild(procMod);
            character.WriteFullGCObject(body);
        }

        private async Task HandleCharacterPlay(RRConnection conn, byte[] data)
        {
            var reader = new LEReader(data);
            if (reader.Remaining < 3) return;
            byte ch = reader.ReadByte();
            byte mt = reader.ReadByte();
            byte slot = reader.ReadByte();
            if (ch != 0x04 || mt != 0x05) return;
            if (!_persistentCharacters.TryGetValue(conn.LoginName, out var chars) || chars.Count == 0) return;
            if (slot >= chars.Count) slot = 0;
            _selectedCharacter[conn.LoginName] = chars[slot];
            var w = new LEWriter();
            w.WriteByte(4);
            w.WriteByte(5);
            await SendCompressedEResponseWithDump(conn, w.ToArray(), "char_play_ack");
            await SendGroupConnectedResponse(conn);
        }

        private async Task HandleCharacterCreate(RRConnection conn, byte[] data)
        {
            string characterName = $"{conn.LoginName}_NewHero";
            uint newCharId = (uint)(conn.ConnId * 100 + 1);
            var newCharacter = Server.Game.Objects.NewPlayer(characterName);
            newCharacter.ID = newCharId;
            if (!_persistentCharacters.ContainsKey(conn.LoginName))
                _persistentCharacters[conn.LoginName] = new List<Server.Game.GCObject>();
            _persistentCharacters[conn.LoginName].Add(newCharacter);
            var response = new LEWriter();
            response.WriteByte(4);
            response.WriteByte(2);
            response.WriteByte(1);
            response.WriteUInt32(newCharId);
            await SendCompressedEResponseWithDump(conn, response.ToArray(), "char_create");
            await SendUpdatedCharacterList(conn, newCharId, characterName);
        }

        private async Task SendUpdatedCharacterList(RRConnection conn, uint charId, string charName)
        {
            await SendCharacterList(conn);
        }

        private async Task SendGroupConnectedResponse(RRConnection conn)
        {
            var w = new LEWriter();
            w.WriteByte(9);
            w.WriteByte(0);
            await SendCompressedEResponseWithDump(conn, w.ToArray(), "group_connected");
            await SendGoToZone_V2(conn, "Town");
        }

        private async Task SendGoToZone_V2(RRConnection conn, string zoneName)
        {
            if (conn.ZoneInitialized) return;
            conn.ZoneInitialized = true;

            var groupWriter = new LEWriter();
            groupWriter.WriteByte(9);
            groupWriter.WriteByte(48);
            groupWriter.WriteUInt32(33752069);
            groupWriter.WriteByte(1);
            groupWriter.WriteByte(1);
            await SendCompressedEResponseWithDump(conn, groupWriter.ToArray(), "group_connect");

            var zoneWriter = new LEWriter();
            zoneWriter.WriteByte(13);
            zoneWriter.WriteByte(0);
            zoneWriter.WriteCString(zoneName);
            zoneWriter.WriteUInt32(30);
            zoneWriter.WriteByte(0);
            zoneWriter.WriteUInt32(1);
            await SendCompressedEResponseWithDump(conn, zoneWriter.ToArray(), "zone_connect");

            await SendCE_Interval_A(conn);
            await Task.Delay(80);
            await SendCE_RandomSeed_A(conn);
            await Task.Delay(80);
            await SendCE_Connect_A(conn);
            await Task.Delay(120);
            await SendPlayerEntitySpawn(conn);
        }

        private async Task HandleZoneChannelMessages(RRConnection conn, byte messageType, byte[] data)
        {
            switch (messageType)
            {
                case 6: await HandleZoneJoin(conn); break;
                case 8: await HandleZoneReady(conn); break;
                case 0: await HandleZoneConnected(conn); break;
                case 1: await HandleZoneReadyResponse(conn); break;
                case 5: await HandleZoneInstanceCount(conn); break;
            }
        }

        private async Task HandleZoneJoin(RRConnection conn)
        {
            var w = new LEWriter();
            w.WriteByte(13);
            w.WriteByte(1);
            w.WriteUInt32(1);
            w.WriteUInt16(0x12);
            for (int i = 0; i < 0x12; i++) w.WriteUInt32(0xFFFFFFFF);
            await SendCompressedEResponseWithDump(conn, w.ToArray(), "zone_ready_13_1");

            var instanceCount = new LEWriter();
            instanceCount.WriteByte(13);
            instanceCount.WriteByte(5);
            instanceCount.WriteUInt32(1);
            instanceCount.WriteUInt32(1);
            await SendCompressedEResponseWithDump(conn, instanceCount.ToArray(), "zone_instance_count_13_5");

            await SendCE_PathManager_Create(conn);
            await SendCE_Interval_A(conn);
            await SendPlayerEntitySpawn(conn);
            await SendFollowClient(conn);
        }

        private async Task HandleZoneConnected(RRConnection conn)
        {
            var w = new LEWriter();
            w.WriteByte(13);
            w.WriteByte(0);
            await SendCompressedEResponseWithDump(conn, w.ToArray(), "zone_connected");
        }

        private async Task HandleZoneReady(RRConnection conn)
        {
            var w = new LEWriter();
            w.WriteByte(13);
            w.WriteByte(1);
            w.WriteUInt32(33752069);
            await SendCompressedEResponseWithDump(conn, w.ToArray(), "zone_ready_resp");
        }

        private async Task HandleZoneReadyResponse(RRConnection conn)
        {
        }

        private async Task HandleZoneInstanceCount(RRConnection conn)
        {
            var w = new LEWriter();
            w.WriteByte(13);
            w.WriteByte(5);
            w.WriteUInt32(1);
            await SendCompressedEResponseWithDump(conn, w.ToArray(), "zone_instance_count");
        }

        private async Task HandleType06(RRConnection conn, LEReader reader)
        {
        }

        private async Task HandleType31(RRConnection conn, LEReader reader)
        {
            await SendType31Ack(conn);
        }

        private async Task SendType31Ack(RRConnection conn)
        {
            var response = new LEWriter();
            response.WriteByte(4);
            response.WriteByte(1);
            response.WriteUInt32(0);
            await SendCompressedEResponseWithDump(conn, response.ToArray(), "type31_ack");
        }

        private async Task SendCE_PathManager_Create(RRConnection conn)
        {
            var body = new LEWriter();
            body.WriteByte(7);
            body.WriteByte(0x0D);
            body.WriteUInt16(0x0001);
            body.WriteUInt16(0x0050);
            body.WriteByte(0xFF);
            WriteCString(body, "PathManager");
            body.WriteByte(0x01);
            body.WriteUInt32(0);
            body.WriteUInt32(0);
            body.WriteUInt16(100);
            body.WriteUInt16(20);
            body.WriteUInt16(100);
            body.WriteUInt16(20);
            body.WriteByte(0x06);
            await SendCompressedAResponseWithDump(conn, body.ToArray(), "ce_pathmanager_create_7_0d");
            Debug.Log($"[Game] SendCE_PathManager_Create: Created PathManager component");
        }

        private async Task SendPlayerEntitySpawn(RRConnection conn)
        {
            try
            {
                var writer = new LEWriter();
                writer.WriteByte(7);
                CreatePlayerEntity(conn, writer);
                writer.WriteByte(0x46);
                await SendCompressedAResponseWithDump(conn, writer.ToArray(), "player_entity_spawn_7");
                Debug.Log($"[Game] SendPlayerEntitySpawn: ✅ Player entity spawned successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendPlayerEntitySpawn: ❌ ERROR - {ex.Message}");
            }
        }

        private void CreatePlayerEntity(RRConnection conn, LEWriter writer)
        {
            const ushort AVATAR_ID = 0x0001;
            const ushort PLAYER_ID = 0x0002;
            const ushort UNIT_CONTAINER_ID = 0x000A;

            writer.WriteByte(0x01);
            writer.WriteByte(0x00);
            writer.WriteByte(0x01);
            writer.WriteByte(0xFF);
            WriteCString(writer, "Avatar");

            writer.WriteByte(0x01);
            writer.WriteByte(0x00);
            writer.WriteByte(0x02);
            writer.WriteByte(0xFF);
            WriteCString(writer, "Player");

            writer.WriteByte(0x32);
            writer.WriteByte(0x00);
            writer.WriteByte(0x01);
            writer.WriteByte(0x00);
            writer.WriteByte(0x0A);
            writer.WriteByte(0xFF);
            WriteCString(writer, "UnitContainer");
            writer.WriteByte(0x01);
            writer.WriteUInt32(1);
            writer.WriteUInt32(1);
            writer.WriteByte(3);
            WriteInventoryChild(writer, "avatar.base.Inventory", 1);
            WriteInventoryChild(writer, "avatar.base.Bank", 2);
            WriteInventoryChild(writer, "avatar.base.TradeInventory", 2);
            writer.WriteByte(0x00);
        }

        private static void WriteInventoryChild(LEWriter w, string gcType, byte inventoryId)
        {
            w.WriteByte(0xFF);
            WriteCString(w, gcType);
            w.WriteByte(inventoryId);
            w.WriteByte(0x01);
            w.WriteByte(0x00);
        }

        private async Task SendFollowClient(RRConnection conn)
        {
            try
            {
                const ushort UNIT_BEHAVIOR_ID = 0x0056;
                var writer = new LEWriter();
                writer.WriteByte(7);
                writer.WriteByte(0x35);
                writer.WriteByte(0x00);
                writer.WriteByte(0x56);
                writer.WriteByte(0x64);
                writer.WriteByte(0x01);
                writer.WriteByte(0x02);
                writer.WriteUInt32(0);
                await SendCompressedAResponseWithDump(conn, writer.ToArray(), "follow_client_7");
                Debug.Log($"[Game] SendFollowClient: ✅ Client control enabled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendFollowClient: ❌ ERROR - {ex.Message}");
            }
        }

        public class RRConnection
        {
            public int ConnId { get; }
            public TcpClient Client { get; }
            public NetworkStream Stream { get; }
            public string LoginName { get; set; } = "";
            public bool IsConnected { get; set; } = true;
            public bool ZoneInitialized { get; set; } = false;

            public RRConnection(int connId, TcpClient client, NetworkStream stream)
            {
                ConnId = connId;
                Client = client;
                Stream = stream;
            }
        }
    }
}

