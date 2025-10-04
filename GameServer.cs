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

        // Cache selected character per user (set on 4/5 Play)
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Server.Game.GCObject> _selectedCharacter = new();


        private bool _gameLoopRunning = false;
        private readonly object _gameLoopLock = new object();


        // Helper: write a null-terminated ASCII string using the existing LEWriter
        private static void WriteCString(LEWriter w, string s)
        {
            var bytes = Encoding.ASCII.GetBytes(s ?? string.Empty);
            w.WriteBytes(bytes);
            w.WriteByte(0);
        }

        // MUST be false for the retail client
        private const bool DUPLICATE_AVATAR_RECORD = false;

        // === Python gateway constants (mirror gatewayserver.py) ===
        // In python: msgDest = b'\x01' + b'\x003'[:: -1] => 01 32 00  (LE u24 = 0x003201)
        //            msgSource = b'\xdd' + b'\x00{'[::-1] => dd 7b 00 (LE u24 = 0x007BDD)
        private const uint MSG_DEST = 0x000F01; // LE bytes => 01 0F 00 (ZoneServer 1.15)
        private const uint MSG_SOURCE = 0x007BDD; // bytes LE => DD 7B 00

        // ===== Dump helper =====
        static class DumpUtil
        {
            // ===== CRC =====
            static readonly uint[] _crcTable = InitCrc();
            static uint[] InitCrc()
            {
                const uint poly = 0xEDB88320u;
                var t = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++) c = ((c & 1) != 0) ? (poly ^ (c >> 1)) : (c >> 1);
                    t[i] = c;
                }
                return t;
            }
            public static uint Crc32(ReadOnlySpan<byte> data)
            {
                uint crc = 0xFFFFFFFFu;
                foreach (var b in data) crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
                return ~crc;
            }

            // ===== Paths =====
            static string _root;
            static string _dumpDir;
            static string _logDir;

            public static string DumpRoot
            {
                get
                {
                    if (!string.IsNullOrEmpty(_root)) return _root;

                    // 1) Env override
                    try
                    {
                        var env = Environment.GetEnvironmentVariable("DR_DUMP_DIR");
                        if (!string.IsNullOrWhiteSpace(env))
                        {
                            Directory.CreateDirectory(env);
                            _root = env;
                            return _root;
                        }
                    }
                    catch { }

#if UNITY_EDITOR
                    // 2) Unity Editor: ProjectRoot/Build/ServerOutput
                    try
                    {
                        var assets = UnityEngine.Application.dataPath; // .../Project/Assets
                        if (!string.IsNullOrEmpty(assets))
                        {
                            var projRoot = Directory.GetParent(assets)!.FullName;
                            var candidate = Path.Combine(projRoot, "Build", "ServerOutput");
                            Directory.CreateDirectory(candidate);
                            _root = candidate;
                            return _root;
                        }
                    }
                    catch { }
#endif

                    // 3) Standalone: next to exe
                    try
                    {
                        var exeDir = AppContext.BaseDirectory;
                        var candidate = Path.Combine(exeDir, "ServerOutput");
                        Directory.CreateDirectory(candidate);
                        _root = candidate;
                        return _root;
                    }
                    catch { }

                    // 4) Fallback: LocalAppData
                    var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DR", "ServerOutput");
                    Directory.CreateDirectory(local);
                    _root = local;
                    return _root;
                }
            }

            public static string DumpDir
            {
                get
                {
                    if (string.IsNullOrEmpty(_dumpDir))
                    {
                        _dumpDir = Path.Combine(DumpRoot, "dumps");
                        Directory.CreateDirectory(_dumpDir);
                    }
                    return _dumpDir;
                }
            }

            public static string LogDir
            {
                get
                {
                    if (string.IsNullOrEmpty(_logDir))
                    {
                        _logDir = Path.Combine(DumpRoot, "logs");
                        Directory.CreateDirectory(_logDir);
                    }
                    return _logDir;
                }
            }

            public static void WriteBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
            public static void WriteText(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

            public static void DumpBlob(string tag, string suffix, byte[] bytes)
            {
                string safeTag = Sanitize(tag);
                string baseName = $"{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{safeTag}.{suffix}";
                string full = Path.Combine(DumpDir, baseName);
                WriteBytes(full, bytes);
                Debug.Log($"[DUMP] Wrote {suffix} -> {full} ({bytes?.Length ?? 0} bytes)");
            }

            public static void DumpCrc(string tag, string label, byte[] bytes)
            {
                string safeTag = Sanitize(tag);
                uint crc = Crc32(bytes);
                string name = $"{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{safeTag}.{label}.crc32.txt";
                string path = Path.Combine(LogDir, name);
                WriteText(path, $"0x{crc:X8}\nlen={bytes?.Length ?? 0}\n");
                Debug.Log($"[DUMP] CRC {label} 0x{crc:X8} (len={bytes?.Length ?? 0}) -> {path}");
            }

            public static void DumpFullFrame(string tag, byte[] payload)
            {
                try
                {
                    DumpBlob(tag, "unity.fullframe.bin", payload);
                    DumpCrc(tag, "fullframe", payload);
                    int head = Math.Min(32, payload?.Length ?? 0);
                    if (payload != null && head > 0)
                    {
                        var sb = new StringBuilder(head * 3);
                        for (int i = 0; i < head; i++) sb.Append(payload[i].ToString("X2")).Append(' ');
                        Debug.Log($"[DUMP] {tag} fullframe head({head}): {sb.ToString().TrimEnd()}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DUMP] DumpFullFrame failed for '{tag}': {ex.Message}");
                }
            }

            static string Sanitize(string tag)
            {
                if (string.IsNullOrWhiteSpace(tag)) return "untagged";
                foreach (var c in Path.GetInvalidFileNameChars()) tag = tag.Replace(c, '_');
                return tag;
            }
        }
        // ============================================================================

        public GameServer(string ip, int port)
        {
            bindIp = ip;
            this.port = port;
            net = new NetServer(ip, port, HandleClient);

            Debug.Log($"[INIT] DFC Active Version set to 0x{GCObject.DFC_VERSION:X2} ({GCObject.DFC_VERSION})");
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
                    try
                    {
                        await Task.Delay(16);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Game] Game loop error: {ex}");
                    }
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
                Debug.Log($"[Game] Client {connId} connected to gameserver");
                Debug.Log($"[Game] Client {connId} - Using improved stream protocol");
                byte[] buffer = new byte[10240];
                while (rrConn.IsConnected)
                {
                    Debug.Log($"[Game] Client {connId} - Reading data...");
                    int bytesRead = await s.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        Debug.LogWarning($"[Game] Client {connId} closed connection.");
                        break;
                    }
                    Debug.Log($"[Game] Client {connId} - Read {bytesRead} bytes");
                    Debug.Log($"[Game] Client {connId} - Data: {BitConverter.ToString(buffer, 0, bytesRead)}");
                    int probe = Math.Min(8, bytesRead);
                    if (probe > 0)
                        Debug.Log($"[Game] Client {connId} - First {probe} bytes: {BitConverter.ToString(buffer, 0, probe)}");
                    byte[] receivedData = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, receivedData, 0, bytesRead);
                    await ProcessReceivedData(rrConn, receivedData);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] Exception from {ep} (ID={connId}): {ex.Message}");
            }
            finally
            {
                rrConn.IsConnected = false;
                _connections.TryRemove(connId, out _);
                _users.TryRemove(connId, out _);
                _peerId24.TryRemove(connId, out _);
                _playerCharacters.TryRemove(connId, out _);
                Debug.Log($"[Game] Client {connId} disconnected");
            }
        }

        private async Task ProcessReceivedData(RRConnection conn, byte[] data)
        {
            Debug.Log($"[Game] ProcessReceivedData: Processing {data.Length} bytes for client {conn.ConnId}");

            try
            {
                await ReadPacket(conn, data);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] ProcessReceivedData: Error processing data for client {conn.ConnId}: {ex.Message}");

                if (!string.IsNullOrEmpty(conn.LoginName))
                {
                    Debug.Log($"[Game] ProcessReceivedData: Sending keep-alive for authenticated client {conn.ConnId}");
                    try
                    {
                        await SendKeepAlive(conn);
                    }
                    catch (Exception keepAliveEx)
                    {
                        Debug.LogError($"[Game] ProcessReceivedData: Keep-alive failed for client {conn.ConnId}: {keepAliveEx.Message}");
                    }
                }
            }
        }

        private async Task SendKeepAlive(RRConnection conn)
        {
            Debug.Log($"[Game] SendKeepAlive: Sending keep-alive to client {conn.ConnId}");
            var keepAlive = new LEWriter();
            keepAlive.WriteByte(0);
            try
            {
                await SendMessage0x10(conn, 0xFF, keepAlive.ToArray(), "keepalive");
                Debug.Log($"[Game] SendKeepAlive: Keep-alive sent to client {conn.ConnId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendKeepAlive: Failed to send keep-alive to client {conn.ConnId}: {ex.Message}");
                throw;
            }
        }

        private async Task ReadPacket(RRConnection conn, byte[] data)
        {
            Debug.Log($"[Game] ReadPacket: Processing {data.Length} bytes for client {conn.ConnId}");

            if (data.Length == 0)
            {
                Debug.LogWarning($"[Game] ReadPacket: Empty data for client {conn.ConnId}");
                return;
            }

            var reader = new LEReader(data);
            byte msgType = reader.ReadByte();

            Debug.Log($"[Game] ReadPacket: Message type 0x{msgType:X2} for client {conn.ConnId}");
            Debug.Log($"[Game] ReadPacket: Login name = '{conn.LoginName}' (authenticated: {!string.IsNullOrEmpty(conn.LoginName)})");

            if (msgType != 0x0A && msgType != 0x0E && string.IsNullOrEmpty(conn.LoginName))
            {
                Debug.LogError($"[Game] ReadPacket: Received invalid message type 0x{msgType:X2} before login for client {conn.ConnId}");
                Debug.LogError($"[Game] ReadPacket: Only 0x0A/0x0E messages allowed before authentication!");
                return;
            }

            switch (msgType)
            {
                case 0x0A:
                    Debug.Log($"[Game] ReadPacket: Handling Compressed A (zlib3) message for client {conn.ConnId}");
                    await HandleCompressedA(conn, reader);
                    break;
                case 0x0E:
                    Debug.Log($"[Game] ReadPacket: Handling Compressed E (zlib1) message for client {conn.ConnId}");
                    await HandleCompressedE(conn, reader);
                    break;
                case 0x06:
                    Debug.Log($"[Game] ReadPacket: Handling Type 06 message for client {conn.ConnId}");
                    await HandleType06(conn, reader);
                    break;
                case 0x31:
                    Debug.Log($"[Game] ReadPacket: Handling Type 31 message for client {conn.ConnId}");
                    await HandleType31(conn, reader);
                    break;
                default:
                    Debug.LogWarning($"[Game] ReadPacket: Unhandled message type 0x{msgType:X2} for client {conn.ConnId}");
                    Debug.LogWarning($"[Game] ReadPacket: Full message hex: {BitConverter.ToString(data)}");
                    Debug.LogWarning($"[Game] ReadPacket: First 32 bytes: {BitConverter.ToString(data, 0, Math.Min(32, data.Length))}");
                    break;
            }
        }

        private async Task HandleCompressedA(RRConnection conn, LEReader reader)
        {
            Debug.Log($"[Game] HandleCompressedA: Starting for client {conn.ConnId}");
            Debug.Log($"[Game] HandleCompressedA: Remaining bytes: {reader.Remaining}");

            // Python zlib3 format:
            // [0x0A][msgDest:u24][compLen:u32][(if 0x0A) 00 03 00 else msgSource:u24][unclen:u32][zlib...]
            const int MIN_HDR = 3 + 4 + 3 + 4; // rough min once we know branch
            if (reader.Remaining < MIN_HDR)
            {
                Debug.LogError($"[Game] HandleCompressedA: Insufficient data, have {reader.Remaining}");
            }

            // We still keep peer24 for downstream since client will send it on 0x0E too
            // but for A(0x0A) we don't strictly need to parse all subfields here for routing;
            // just decompress and forward to the inner dispatcher (same as before).
            // For brevity we reuse previous parsing path that expected:
            // [peer:u24][packetLen:u32][dest:u8][sub:u8][zero:u8][unclen:u32][zlib...]
            // but we only support the branch we generate (00 03 00).
            // If your client actually sends A-frames, keep existing inflate path:
            if (reader.Remaining < (3 + 4)) return;

            uint peer = reader.ReadUInt24();
            _peerId24[conn.ConnId] = peer;

            uint compPlus7 = reader.ReadUInt32();
            int compLen = (int)compPlus7 - 7;
            if (compLen < 0) { Debug.LogError("[Game] HandleCompressedA: bad compLen"); return; }

            if (reader.Remaining < 3 + 4 + compLen) { /* minimal check */ }

            byte dest = reader.ReadByte(); // expected 0x00
            byte sub = reader.ReadByte(); // expected 0x03
            byte zero = reader.ReadByte(); // expected 0x00
            uint unclen = reader.ReadUInt32();
            byte[] comp = reader.ReadBytes(compLen);
            byte[] inner;
            try
            {
                inner = (compLen == 0 || unclen == 0) ? Array.Empty<byte>() : ZlibUtil.Inflate(comp, unclen);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] HandleCompressedA: Decompression failed: {ex.Message}");
                return;
            }

            // In our use, A/0x03 is just small advancement signals; route to same handler:
            await ProcessUncompressedMessage(conn, dest, sub, inner);
        }

        // ===================== zlib1 E-lane (the IMPORTANT fix) ====================
        // Python send_zlib1 format:
        // [0x0E]
        // [msgDest:u24]
        // [compressedLen:u24]
        // [0x00]
        // [msgSource:u24]
        // [0x01 0x00 0x01 0x00 0x00]
        // [uncompressedLen:u32]
        // [zlib(inner)]
        private (byte[] payload, byte[] compressed) BuildCompressedEPayload_Zlib1(byte[] innerData)
        {
            byte[] z = ZlibUtil.Deflate(innerData);
            int compressedLen = z.Length + 12; // python: len(zlibMsg) + 12

            var w = new LEWriter();
            w.WriteByte(0x0E);
            w.WriteUInt24((int)MSG_DEST);              // msgDest
            w.WriteUInt24(compressedLen);              // 3-byte comp len
            w.WriteByte(0x00);
            w.WriteUInt24((int)MSG_SOURCE);            // msgSource
            // python literal: b'\x01\x00\x01\x00\x00'
            w.WriteByte(0x01);
            w.WriteByte(0x00);
            w.WriteByte(0x01);
            w.WriteByte(0x00);
            w.WriteByte(0x00);
            w.WriteUInt32((uint)innerData.Length);     // uncompressed size
            w.WriteBytes(z);

            return (w.ToArray(), z);
        }

        // We keep an A-lane helper too, but match python's zlib3 packing when WE send:
        // Python send_zlib3 (for pktType==0x0A):
        // [0x0A][msgDest:u24][compLen:u32][00 03 00][unclen:u32][zlib...]
        private (byte[] payload, byte[] compressed) BuildCompressedAPayload_Zlib3(byte[] innerData)
        {
            byte[] z = ZlibUtil.Deflate(innerData);
            var w = new LEWriter();
            w.WriteByte(0x0A);
            w.WriteUInt24((int)MSG_DEST);              // msgDest
            w.WriteUInt32((uint)(z.Length + 7));       // comp len (+7)
            w.WriteByte(0x00);                         // 00 03 00  (python fixed)
            w.WriteByte(0x03);
            w.WriteByte(0x00);
            w.WriteUInt32((uint)innerData.Length);     // uncompressed size
            w.WriteBytes(z);
            return (w.ToArray(), z);
        }

        // --------------- SEND helpers (now split: A=zlib3, E=zlib1) ----------------
        private async Task SendCompressedEResponse(RRConnection conn, byte[] innerData)
        {
            try
            {
                var (payload, z) = BuildCompressedEPayload_Zlib1(innerData);
                Debug.Log($"[SEND][E/zlib1] comp={z.Length} unclen={innerData.Length}");
                await conn.Stream.WriteAsync(payload, 0, payload.Length);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Wire][E] send failed: {ex.Message}");
            }
        }

        private async Task SendCompressedEResponseWithDump(RRConnection conn, byte[] innerData, string tag)
        {
            try
            {
                DumpUtil.DumpBlob(tag, "unity.uncompressed.bin", innerData);
                DumpUtil.DumpCrc(tag, "uncompressed", innerData);
                var (payload, z) = BuildCompressedEPayload_Zlib1(innerData);
                DumpUtil.DumpBlob(tag, "unity.compressed.bin", z);
                DumpUtil.DumpCrc(tag, "compressed", z);
                DumpUtil.DumpBlob(tag, "unity.fullframe.bin", payload);
                DumpUtil.DumpCrc(tag, "fullframe", payload);
                await conn.Stream.WriteAsync(payload, 0, payload.Length);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Wire][E] dump/send failed: {ex.Message}");
            }
        }

        private async Task SendCompressedAResponse(RRConnection conn, byte[] innerData)
        {
            try
            {
                var (payload, z) = BuildCompressedAPayload_Zlib3(innerData);
                Debug.Log($"[SEND][A/zlib3] comp={z.Length} unclen={innerData.Length}");
                await conn.Stream.WriteAsync(payload, 0, payload.Length);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Wire][A] send failed: {ex.Message}");
            }
        }

        private async Task SendCompressedAResponseWithDump(RRConnection conn, byte[] innerData, string tag)
        {
            try
            {
                DumpUtil.DumpBlob(tag, "unity.uncompressed.bin", innerData);
                DumpUtil.DumpCrc(tag, "uncompressed", innerData);
                var (payload, z) = BuildCompressedAPayload_Zlib3(innerData);
                DumpUtil.DumpBlob(tag, "unity.compressed.bin", z);
                DumpUtil.DumpCrc(tag, "compressed", z);
                DumpUtil.DumpBlob(tag, "unity.fullframe.bin", payload);
                DumpUtil.DumpCrc(tag, "fullframe", payload);
                await conn.Stream.WriteAsync(payload, 0, payload.Length);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Wire][A] dump/send failed: {ex.Message}");
            }
        }
        // ---------------------------------------------------------------------------

        private async Task ProcessUncompressedMessage(RRConnection conn, byte dest, byte msgTypeA, byte[] uncompressed)
        {
            Debug.Log($"[Game] ProcessUncompressedMessage: A-lane dest=0x{dest:X2} sub=0x{msgTypeA:X2}");

            switch (msgTypeA)
            {
                case 0x00: // initial login blob
                    await HandleInitialLogin(conn, uncompressed);
                    break;

                case 0x02: // ticks
                    // echo empty 0x02 on A using zlib3 python layout
                    await SendCompressedAResponseWithDump(conn, Array.Empty<byte>(), "a02_empty");
                    break;

                case 0x03: // session token style
                    if (uncompressed.Length >= 4)
                    {
                        var reader = new LEReader(uncompressed);
                        uint sessionToken = reader.ReadUInt32();
                        if (GlobalSessions.TryConsume(sessionToken, out var user) && !string.IsNullOrEmpty(user))
                        {
                            conn.LoginName = user;
                            _users[conn.ConnId] = user;

                            var ack = new LEWriter();
                            ack.WriteByte(0x03);
                            await SendMessage0x10(conn, 0x0A, ack.ToArray(), "msg10_auth_ack");

                            // Immediately tick E so client advances like python does
                            //  await SendCompressedEResponseWithDump(conn, Array.Empty<byte>(), "e_hello_tick");

                            await Task.Delay(50);
                            await StartCharacterFlow(conn);
                        }
                        else
                        {
                            Debug.LogError($"[Game] A/0x03 invalid session token 0x{sessionToken:X8}");
                        }
                    }
                    break;

                case 0x0F:
                    await HandleChannelMessage(conn, uncompressed);
                    break;

                default:
                    Debug.LogWarning($"[Game] Unhandled A sub=0x{msgTypeA:X2}");
                    break;
            }
        }

        private async Task HandleInitialLogin(RRConnection conn, byte[] data)
        {
            Debug.Log($"[Game] HandleInitialLogin: ENTRY client {conn.ConnId}");
            if (data.Length < 5)
            {
                Debug.LogError($"[Game] HandleInitialLogin: need 5 bytes, have {data.Length}");
                return;
            }

            var reader = new LEReader(data);
            byte subtype = reader.ReadByte();
            uint oneTimeKey = reader.ReadUInt32();

            if (!GlobalSessions.TryConsume(oneTimeKey, out var user) || string.IsNullOrEmpty(user))
            {
                Debug.LogError($"[Game] HandleInitialLogin: Invalid OneTimeKey 0x{oneTimeKey:X8}");
                return;
            }

            conn.LoginName = user;
            _users[conn.ConnId] = user;
            Debug.Log($"[Game] HandleInitialLogin: SUCCESS user '{user}'");

            var ack = new LEWriter();
            ack.WriteByte(0x03);
            await SendMessage0x10(conn, 0x0A, ack.ToArray(), "msg10_auth_ack_initial");

            // prime E-lane per gateway
            // await SendCompressedEResponseWithDump(conn, Array.Empty<byte>(), "e_hello_tick");

            // small A/0x03 advance (compatible with our zlib3 builder)
            var advance = new LEWriter();
            advance.WriteUInt24(0x00B2B3B4);
            advance.WriteByte(0x00);
            await SendCompressedAResponseWithDump(conn, advance.ToArray(), "advance_a03");

            // A/0x02 nudge
            await SendCompressedAResponseWithDump(conn, Array.Empty<byte>(), "nudge_a02");
            await Task.Delay(75);

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
                        case 0: // CharacterConnected request from client
                            await SendCharacterConnectedResponse(conn);
                            break;

                        case 1: // UI nudge 4/1 -> send tiny ack on E
                            {
                                var ack = new LEWriter();
                                ack.WriteByte(4);
                                ack.WriteByte(1);
                                ack.WriteUInt32(0);
                                await SendCompressedEResponseWithDump(conn, ack.ToArray(), "char_ui_nudge_4_1_ack");
                                break;
                            }

                        case 3: // Get list
                            await SendCharacterList(conn);
                            break;

                        case 5: // Play
                            await HandleCharacterPlay(conn, data);
                            break;

                        case 2: // Create
                            await HandleCharacterCreate(conn, data);
                            break;

                        default:
                            Debug.LogWarning($"[Game] Unhandled char msg 0x{messageType:X2}");
                            break;
                    }
                    break;

                case 9:
                    await HandleGroupChannelMessages(conn, messageType);
                    break;

                case 13:
                    await HandleZoneChannelMessages(conn, messageType, data);
                    break;

                default:
                    Debug.LogWarning($"[Game] Unhandled channel {channel}");
                    break;
            }
        }

        // Character flow now **sends on E-lane/zlib1**
        private async Task StartCharacterFlow(RRConnection conn)
        {
            Debug.Log($"[Game] StartCharacterFlow: client {conn.ConnId} ({conn.LoginName})");

            await Task.Delay(50);

            var sent = await EnsurePeerThenSendCharConnected(conn);
            if (!sent)
            {
                Debug.LogWarning("[Game] StartCharacterFlow: 4/0 deferred; nudging...");
            }

            // keep one gentle tick on A per python gateway behavior
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500);
                    await SendCompressedAResponseWithDump(conn, Array.Empty<byte>(), "tick_a02_500ms");
                    await Task.Delay(500);
                    if (!_charListSent.TryGetValue(conn.ConnId, out var flag) || !flag)
                        await SendCompressedAResponseWithDump(conn, Array.Empty<byte>(), "tick_a02_1000ms");
                }
                catch (Exception ex) { Debug.LogWarning($"[Game] A/0x02 tick failed: {ex.Message}"); }
            });
        }

        private async Task SendCharacterConnectedResponse(RRConnection conn)
        {
            Debug.Log($"[Game] SendCharacterConnectedResponse: *** ENTRY (DFC-style) *** For client {conn.ConnId}");

            try
            {
                const int count = 2;
                if (!_persistentCharacters.ContainsKey(conn.LoginName))
                {
                    _persistentCharacters[conn.LoginName] = new List<Server.Game.GCObject>(count);
                    Debug.Log($"[Game] SendCharacterConnectedResponse: Created character list for {conn.LoginName}");
                }

                var list = _persistentCharacters[conn.LoginName];
                while (list.Count < count)
                {
                    try
                    {
                        var p = Server.Game.Objects.NewPlayer(conn.LoginName);
                        p.ID = (uint)Server.Game.Objects.NewID();
                        list.Add(p);
                        Debug.Log($"[Game] SendCharacterConnectedResponse: Added DFC player stub ID=0x{p.ID:X8}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Game] SendCharacterConnectedResponse: ERROR creating player stub: {ex.Message}");
                        Debug.LogError(ex.StackTrace);
                        break;
                    }
                }

                var body = new LEWriter();
                body.WriteByte(4);
                body.WriteByte(0);
                var inner = body.ToArray();

                Debug.Log($"[SEND][inner][4/0] {BitConverter.ToString(inner)} (len={inner.Length})");
                Debug.Log($"[SEND][E][prep] 4/0 using peer=0x{GetClientId24(conn.ConnId):X6} dest=0x01 sub=0x0F innerLen={inner.Length}");

                await SendCompressedEResponseWithDump(conn, inner, "char_connected");
                Debug.Log("[Game] SendCharacterConnectedResponse: *** SUCCESS *** Sent DFC-compatible 4/0 (E-lane)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendCharacterConnectedResponse: *** CRITICAL EXCEPTION *** {ex.Message}");
                Debug.LogError(ex.StackTrace);
            }
        }



        private void WriteGoSendPlayer(LEWriter body, Server.Game.GCObject character)
        {
            try
            {
                long startPos = body.ToArray().Length;

                // CRITICAL FIX: First create the Avatar
                var avatar = Server.Game.Objects.LoadAvatar();

                // CRITICAL FIX: Add the Avatar to the Player first
                character.AddChild(avatar);

                // CRITICAL FIX: Then add the ProcModifier to the Player
                var procMod = Server.Game.Objects.NewProcModifier();
                character.AddChild(procMod);

                // Log the UnitContainer children before serialization
                var unitContainer = avatar.Children?.FirstOrDefault(c => c.NativeClass == "UnitContainer");
                if (unitContainer != null)
                {
                    Debug.Log($"[DFC][UnitContainer] ChildCount(before)={unitContainer.Children?.Count ?? 0}");
                    if (unitContainer.Children != null)
                    {
                        for (int i = 0; i < unitContainer.Children.Count; i++)
                        {
                            var child = unitContainer.Children[i];
                            Debug.Log($"[DFC][UnitContainer] Child[{i}] native='{child.NativeClass}' gc='{child.GCClass}'");
                        }
                    }
                }

                Debug.Log($"[Game] WriteGoSendPlayer: Writing character ID={character.ID} with DFC format");
                character.WriteFullGCObject(body);

                long afterPlayer = body.ToArray().Length;
                Debug.Log($"[Game] WriteGoSendPlayer: Player DFC write bytes={afterPlayer - startPos}");

                if (DUPLICATE_AVATAR_RECORD)
                {
                    Debug.Log("[Game] WriteGoSendPlayer: DUPLICATE_AVATAR_RECORD=true, adding standalone avatar");

                    long startAv = body.ToArray().Length;
                    avatar.WriteFullGCObject(body);
                    long afterAv = body.ToArray().Length;
                    Debug.Log($"[Game] WriteGoSendPlayer: Standalone Avatar DFC write bytes={afterAv - startAv}");

                    body.WriteByte(0x01);
                    body.WriteByte(0x01);
                    body.WriteBytes(Encoding.UTF8.GetBytes("Normal"));
                    body.WriteByte(0x00);
                    body.WriteByte(0x01);
                    body.WriteByte(0x01);
                    body.WriteUInt32(0x01);
                }
                else
                {
                    Debug.Log("[Game] WriteGoSendPlayer: Sending only Player with Avatar child (DFC format), no tail");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] WriteGoSendPlayer: EXCEPTION {ex.Message}");
                Debug.LogError($"[Game] WriteGoSendPlayer: Stack trace: {ex.StackTrace}");
            }
        }

        private async Task SendCharacterList(RRConnection conn)
        {
            Debug.Log($"[Game] SendCharacterList: *** ENTRY *** DFC format with djb2 hashes");

            try
            {
                if (!_persistentCharacters.TryGetValue(conn.LoginName, out var characters))
                {
                    Debug.LogError($"[Game] SendCharacterList: *** ERROR *** No characters found for {conn.LoginName}");
                    return;
                }

                Debug.Log($"[Game] SendCharacterList: *** FOUND CHARACTERS *** Count: {characters.Count} for {conn.LoginName}");

                int count = characters.Count;
                if (count > 255)
                {
                    Debug.LogWarning($"[Game] SendCharacterList: Character count {count} exceeds 255; clamping to 255 for wire format");
                    count = 255;
                }

                var body = new LEWriter();
                body.WriteByte(4);
                body.WriteByte(3);
                body.WriteByte((byte)count);

                Debug.Log($"[Game] SendCharacterList: *** WRITING DFC CHARACTERS *** Processing {count} characters");

                for (int i = 0; i < count; i++)
                {
                    var character = characters[i];
                    Debug.Log($"[Game] SendCharacterList: *** CHARACTER {i + 1} *** ID: {character.ID}, Writing DFC character data");

                    try
                    {
                        body.WriteUInt32(character.ID);
                        Debug.Log($"[Game] SendCharacterList: *** CHARACTER {i + 1} *** wrote ID={character.ID}");

                        WriteGoSendPlayer(body, character);
                        Debug.Log($"[Game] SendCharacterList: *** CHARACTER {i + 1} *** DFC WriteGoSendPlayer complete; current bodyLen={body.ToArray().Length}");
                    }
                    catch (Exception charEx)
                    {
                        Debug.LogError($"[Game] SendCharacterList: *** ERROR CHARACTER {i + 1} *** {charEx.Message}");
                        Debug.LogError($"[Game] SendCharacterList: *** CHARACTER {i + 1} STACK TRACE *** {charEx.StackTrace}");
                    }
                }

                var inner = body.ToArray();
                Debug.Log($"[Game] SendCharacterList: *** SENDING DFC MESSAGE *** Total body length: {inner.Length} bytes");
                Debug.Log($"[SEND][inner] CH=4,TYPE=3 DFC: {BitConverter.ToString(inner)} (len={inner.Length})");

                if (!(inner.Length >= 3 && inner[0] == 0x04 && inner[1] == 0x03))
                {
                    Debug.LogError($"[Game][FATAL] SendCharacterList header wrong: {BitConverter.ToString(inner, 0, Math.Min(inner.Length, 8))}");
                }
                else
                {
                    Debug.Log($"[Game] SendCharacterList: Header OK -> 04-03 count={inner[2]} (DFC format)");
                }

                int head = Math.Min(32, inner.Length);
                Debug.Log($"[Game] SendCharacterList: First {head} bytes: {BitConverter.ToString(inner, 0, head)}");

                Debug.Log($"[SEND][E][prep] 4/3 DFC using peer=0x{GetClientId24(conn.ConnId):X6} dest=0x01 sub=0x0F innerLen={inner.Length}");
                await SendCompressedEResponseWithDump(conn, inner, "charlist");
                Debug.Log($"[Game] SendCharacterList: *** SUCCESS *** Sent DFC format with djb2 hashes, {count} characters");

                _charListSent[conn.ConnId] = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendCharacterList: *** CRITICAL EXCEPTION *** {ex.Message}");
                Debug.LogError($"[Game] SendCharacterList: *** STACK TRACE *** {ex.StackTrace}");
            }
        }


        private async Task SendToCharacterCreation(RRConnection conn)
        {
            var create = new LEWriter();
            create.WriteByte(4);
            create.WriteByte(4);
            await SendCompressedEResponseWithDump(conn, create.ToArray(), "char_creation_4_4");
        }

        private async Task HandleCharacterPlay(RRConnection conn, byte[] data)
        {
            Debug.Log($"[Play] HandleCharacterPlay ENTRY: LoginName={conn.LoginName}, DataLen={data.Length}");
            Debug.Log($"[Play] Data bytes: {BitConverter.ToString(data)}");

            var r = new LEReader(data);
            if (r.Remaining < 3)
            {
                Debug.LogError($"[Play] FAIL: Not enough data (remaining={r.Remaining}, need 3)");
                await SendPlayFallback();
                return;
            }

            byte ch = r.ReadByte();
            byte mt = r.ReadByte();
            Debug.Log($"[Play] Read channel={ch}, msgType={mt}");

            if (ch != 0x04 || mt != 0x05)
            {
                Debug.LogError($"[Play] FAIL: Wrong channel/type (expected 4/5, got {ch}/{mt})");
                await SendPlayFallback();
                return;
            }

            if (r.Remaining < 1)
            {
                Debug.LogError($"[Play] FAIL: No slot byte (remaining={r.Remaining})");
                await SendPlayFallback();
                return;
            }

            byte slot = r.ReadByte();
            Debug.Log($"[Play] Client requesting slot={slot}");

            // Check if we have characters for this user
            bool hasChars = _persistentCharacters.TryGetValue(conn.LoginName, out var chars);
            Debug.Log($"[Play] _persistentCharacters has entry for '{conn.LoginName}': {hasChars}");

            if (!hasChars || chars.Count == 0)
            {
                Debug.LogError($"[Play] FAIL: No characters found for '{conn.LoginName}'");
                await SendPlayFallback();
                return;
            }

            Debug.Log($"[Play] Character count for '{conn.LoginName}': {chars.Count}");

            // If slot is out of bounds, default to slot 0
            if (slot >= chars.Count)
            {
                Debug.LogWarning($"[Play] Slot {slot} out of bounds (count={chars.Count}), defaulting to slot 0");
                slot = 0;
            }

            Debug.Log($"[Play] Using slot={slot}");
            for (int i = 0; i < chars.Count; i++)
            {
                Debug.Log($"[Play]   Slot {i}: ID={chars[i].ID}, Name={chars[i].Name}");
            }

            var selectedChar = chars[(int)slot];
            _selectedCharacter[conn.LoginName] = selectedChar;
            Debug.Log($"[Play] ✅ SUCCESS: Selected slot={slot} id={selectedChar.ID} name={selectedChar.Name} for {conn.LoginName}");
            Debug.Log($"[Play] Stored in _selectedCharacter['{conn.LoginName}']");

            var w = new LEWriter();
            w.WriteByte(4);
            w.WriteByte(5);
            await SendCompressedEResponseWithDump(conn, w.ToArray(), "char_play_ack_4_5");

            await Task.Delay(100);
            await SendGroupConnectedResponse(conn);
            return;

            async Task SendPlayFallback()
            {
                Debug.LogWarning($"[Play] Sending fallback response to {conn.LoginName}");
                var fb = new LEWriter();
                fb.WriteByte(4);
                fb.WriteByte(5);
                fb.WriteByte(1);
                await SendCompressedEResponseWithDump(conn, fb.ToArray(), "char_play_fallback");
            }
        }

        private async Task<bool> WaitForPeer24(RRConnection conn, int msTimeout = 1500, int pollMs = 10)
        {
            int waited = 0;
            while (waited < msTimeout)
            {
                if (_peerId24.TryGetValue(conn.ConnId, out var pid) && pid != 0u)
                {
                    Debug.Log($"[Wire] WaitForPeer24: got peer=0x{pid:X6} after {waited}ms");
                    return true;
                }
                await Task.Delay(pollMs);
                waited += pollMs;
            }
            Debug.LogWarning($"[Wire] WaitForPeer24: timed out after {msTimeout}ms; peer unknown");
            return false;
        }

        private async Task<bool> EnsurePeerThenSendCharConnected(RRConnection conn)
        {
            await WaitForPeer24(conn);
            // Even if peer isn't known yet, E-lane uses MSG_SOURCE/DEST constants (gateway semantics),
            // so we go ahead and send 4/0 to wake the Character UI.
            await SendCharacterConnectedResponse(conn);
            return true;
        }

        private async Task InitiateWorldEntry(RRConnection conn)
        {
            await SendGoToZone_V2(conn, "Town");
        }

        private async Task HandleCharacterCreate(RRConnection conn, byte[] data)
        {
            Debug.Log($"[Game] HandleCharacterCreate: Character creation request from client {conn.ConnId}");
            Debug.Log($"[Game] HandleCharacterCreate: Data ({data.Length} bytes): {BitConverter.ToString(data)}");

            string characterName = $"{conn.LoginName}_NewHero";
            uint newCharId = (uint)(conn.ConnId * 100 + 1);

            try
            {
                var newCharacter = Server.Game.Objects.NewPlayer(characterName);
                newCharacter.ID = newCharId;
                Debug.Log($"[Game] HandleCharacterCreate: Created DFC character with ID={newCharId}");

                if (!_persistentCharacters.TryGetValue(conn.LoginName, out var existing))
                {
                    existing = new List<Server.Game.GCObject>();
                    _persistentCharacters[conn.LoginName] = existing;
                    Debug.Log($"[Game] HandleCharacterCreate: No existing list for {conn.LoginName}; created new list");
                }

                existing.Add(newCharacter);
                Debug.Log($"[Game] HandleCharacterCreate: Persisted new DFC character (ID: {newCharId}) for {conn.LoginName}. Total now: {existing.Count}");
            }
            catch (Exception persistEx)
            {
                Debug.LogError($"[Game] HandleCharacterCreate: *** ERROR persisting DFC character *** {persistEx.Message}");
                Debug.LogError($"[Game] HandleCharacterCreate: *** STACK TRACE *** {persistEx.StackTrace}");
            }

            var response = new LEWriter();
            response.WriteByte(4);
            response.WriteByte(2);
            response.WriteByte(1);
            response.WriteUInt32(newCharId);

            await SendCompressedEResponseWithDump(conn, response.ToArray(), "char_create_4_2");
            Debug.Log($"[Game] HandleCharacterCreate: Sent DFC character creation success for {characterName} (ID: {newCharId})");

            await Task.Delay(100);
            await SendUpdatedCharacterList(conn, newCharId, characterName);
        }

        private async Task SendUpdatedCharacterList(RRConnection conn, uint charId, string charName)
        {
            Debug.Log($"[Game] SendUpdatedCharacterList: Sending DFC list with newly created character");

            try
            {
                if (!_persistentCharacters.TryGetValue(conn.LoginName, out var chars))
                {
                    Debug.LogWarning($"[Game] SendUpdatedCharacterList: No persistent list found after create; falling back to single DFC entry build");
                    var w = new LEWriter();
                    w.WriteByte(4);
                    w.WriteByte(3);
                    w.WriteByte(1);

                    var newCharacter = Server.Game.Objects.NewPlayer(charName);
                    newCharacter.ID = charId;
                    w.WriteUInt32(charId);
                    WriteGoSendPlayer(w, newCharacter);

                    var innerSingle = w.ToArray();
                    Debug.Log($"[SEND][inner] CH=4,TYPE=3 (updated single DFC) : {BitConverter.ToString(innerSingle)} (len={innerSingle.Length})");
                    Debug.Log($"[SEND][E][prep] 4/3(DFC SINGLE) peer=0x{GetClientId24(conn.ConnId):X6} dest=0x01 sub=0x0F innerLen={innerSingle.Length}");

                    await SendCompressedEResponseWithDump(conn, innerSingle, "charlist_single");
                    Debug.Log($"[Game] SendUpdatedCharacterList: Sent updated DFC character list (SINGLE fallback) with new character (ID {charId})");
                    return;
                }
                else
                {
                    Debug.Log($"[Game] SendUpdatedCharacterList: Found persistent list (count={chars.Count}); delegating to SendCharacterList() for DFC format");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Game] SendUpdatedCharacterList: Pre-flight check warning: {ex.Message}");
            }

            await SendCharacterList(conn);
        }

        private async Task SendGroupConnectedResponse(RRConnection conn)
        {
            var w = new LEWriter();
            w.WriteByte(9);
            w.WriteByte(0);
            await SendCompressedEResponseWithDump(conn, w.ToArray(), "group_connected_9_0");

            await Task.Delay(50);
            await SendGoToZone_V2(conn, "Town");
        }

        private async Task HandleGroupChannelMessages(RRConnection conn, byte messageType)
        {
            switch (messageType)
            {
                case 0:
                    await SendGoToZone_V2(conn, "Town");
                    break;
                default:
                    Debug.LogWarning($"[Game] Unhandled group msg 0x{messageType:X2}");
                    break;
            }
        }



        private async Task SendGoToZone_V2(RRConnection conn, string zoneName)
        {
            // Prevent duplicate zone initialization
            if (conn.ZoneInitialized)
            {
                Debug.LogWarning($"[Game] SendGoToZone: Zone already initialized for client {conn.ConnId}, skipping");
                return;
            }

            Debug.Log($"[Game] SendGoToZone: Sending player to zone '{zoneName}'");
            conn.ZoneInitialized = true;

            try
            {
                // FIXED: Only send initial connection messages
                // The client will then send Zone Join (13/6), which triggers HandleZoneJoin
                // HandleZoneJoin will send Zone Ready, Instance Count, entity spawn, etc.

                // Step 1: Group connect - E-lane
                var groupWriter = new LEWriter();
                groupWriter.WriteByte(9);
                groupWriter.WriteByte(48);
                groupWriter.WriteUInt32(33752069);
                groupWriter.WriteByte(1);
                groupWriter.WriteByte(1);
                await SendCompressedEResponseWithDump(conn, groupWriter.ToArray(), "group_connect_9_48");
                await Task.Delay(300);

                // Step 2: Zone connect - E-lane
                var w = new LEWriter();
                w.WriteByte(13);
                w.WriteByte(0);
                w.WriteCString(zoneName);
                w.WriteUInt32(30);
                w.WriteByte(0);
                w.WriteUInt32(1);
                await SendCompressedEResponseWithDump(conn, w.ToArray(), "zone_connect_13_0");

                // Step 3: ClientEntity bootstrap - A-lane
                await SendCE_Interval_A(conn);
                await Task.Delay(80);
                await SendCE_RandomSeed_A(conn);
                await Task.Delay(80);
                await SendCE_Connect_A(conn);


                // Step 4: Send Zone/2 with Avatar DFC object - CRITICAL for spawning!
#if false // Disabled: GO flow has no Zone/2
                if (_selectedCharacter.TryGetValue(conn.LoginName, out var character))
                {
                    var spawnWriter = new LEWriter();
                    spawnWriter.WriteByte(13);  // Zone channel
                    spawnWriter.WriteByte(2);   // Zone spawn opcode

                    var avatar = character.Children?.FirstOrDefault(c => c.NativeClass == "Avatar");
                    if (avatar != null)
                    {
                        avatar.WriteFullGCObject(spawnWriter);
                        await SendCompressedEResponseWithDump(conn, spawnWriter.ToArray(), "zone_spawn_enter_world");
                        await Task.Delay(100);
                        Debug.Log($"[Game] SendGoToZone: Sent Zone/2 with Avatar data");
                    }
                    else
                    {
                        Debug.LogWarning($"[Game] SendGoToZone: No Avatar found for character {character.Name}");
                    }
                }
#endif // end disable Zone/2 block



                Debug.Log($"[Game] SendGoToZone: Sent initial messages, waiting for client Zone Join request");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendGoToZone: Error: {ex.Message}");
            }
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
                default:
                    Debug.LogWarning($"[Game] Unhandled zone msg 0x{messageType:X2}");
                    break;
            }
        }

        private async Task HandleZoneJoin(RRConnection conn)
        {
            Debug.Log($"[Game] HandleZoneJoin: Client requested zone join");

            // Match GO server's handleZoneJoin EXACTLY

            // 1. Send Zone Ready (13/1) with minimap - E-lane (FIXED!)
            var w = new LEWriter();
            w.WriteByte(13);
            w.WriteByte(1);
            w.WriteUInt32(1);  // Zone ID
            w.WriteUInt16(0x12);  // Minimap explored bit count
            for (int i = 0; i < 0x12; i++)
                w.WriteUInt32(0xFFFFFFFF);
            await SendCompressedEResponseWithDump(conn, w.ToArray(), "zone_ready_13_1");
            Debug.Log($"[Game] HandleZoneJoin: Sent Zone Ready on E-lane");

            // 2. Send Zone Instance Count (13/5) - E-lane (FIXED!)
            var instanceCount = new LEWriter();
            instanceCount.WriteByte(13);
            instanceCount.WriteByte(5);
            instanceCount.WriteUInt32(1);
            instanceCount.WriteUInt32(1);
            await SendCompressedEResponseWithDump(conn, instanceCount.ToArray(), "zone_instance_count_13_5");
            Debug.Log($"[Game] HandleZoneJoin: Sent Instance Count on E-lane");

            // 3. Send Interval - A-lane
            await SendCE_Interval_A(conn);
            Debug.Log($"[Game] HandleZoneJoin: Sent CE Interval");

            // 4. Trigger PlayerEnteredZone (entity spawn)
            await SendPlayerEntitySpawn(conn);
            Debug.Log($"[Game] HandleZoneJoin: Sent Entity Spawn");

            // 5. Enable client control
            await SendFollowClient(conn);
            Debug.Log($"[Game] HandleZoneJoin: Sent Follow Client");

            // 6. Send message 385 (ClientEntity Now Connected) - AFTER entity spawn like GO server
            await SendCE_NowConnected(conn);
            Debug.Log($"[Game] HandleZoneJoin: Sent CE Now Connected (385)");

            Debug.Log($"[Game] HandleZoneJoin: ✅ Complete zone join sequence finished");
        }

        private async Task HandleZoneConnected(RRConnection conn)
        {
            Debug.Log($"[Game] HandleZoneConnected: Sending zone connected message to client {conn.ConnId}");

            // Keep this message simple - the client expects just the channel and message type
            var w = new LEWriter();
            w.WriteByte(13);  // Zone channel
            w.WriteByte(0);   // Connected message type

            // No additional data - the client is expecting a simple message

            await SendCompressedEResponseWithDump(conn, w.ToArray(), "zone_connected_13_0");

            Debug.Log($"[Game] HandleZoneConnected: Zone connected message sent to client {conn.ConnId}");
        }

        private async Task HandleZoneReady(RRConnection conn)
        {
            // Reply to client’s Zone/8 with Zone/1 (ReadyResponse)
            const uint CharId = 33752069; // keep consistent with what you send elsewhere

            var w = new LEWriter();
            w.WriteByte(13);        // Zone channel
            w.WriteByte(1);         // ReadyResponse
            w.WriteUInt32(CharId);  // character id
            await SendCompressedEResponseWithDump(conn, w.ToArray(), "zone_ready_resp_13_1");

            Debug.Log($"[Game] HandleZoneReady: Zone ready message sent to client {conn.ConnId}");
        }

        private async Task HandleZoneReadyResponse(RRConnection conn)
        {
            var w = new LEWriter();
            w.WriteByte(13);
            w.WriteByte(1);
            // await SendCompressedEResponseWithDump(conn, w.ToArray(), "zone_ready_resp_13_1");
        }

        private async Task HandleZoneInstanceCount(RRConnection conn)
        {
            Debug.Log($"[Game] HandleZoneInstanceCount: Sending zone instance count message to client {conn.ConnId}");

            var w = new LEWriter();
            w.WriteByte(13);  // Zone channel
            w.WriteByte(5);   // Instance count message type
            w.WriteUInt32(1);  // Number of instances

            await SendCompressedEResponseWithDump(conn, w.ToArray(), "zone_instance_count_13_5");

            Debug.Log($"[Game] HandleZoneInstanceCount: Zone instance count message sent to client {conn.ConnId}");
        }

        private async Task HandleType31(RRConnection conn, LEReader reader)
        {
            // unchanged — logs + ack
            Debug.Log($"[Game] HandleType31: remaining {reader.Remaining}");
            await SendType31Ack(conn);
        }

        private async Task SendType31Ack(RRConnection conn)
        {
            try
            {
                var response = new LEWriter();
                response.WriteByte(4);
                response.WriteByte(1);
                response.WriteUInt32(0);
                await SendCompressedEResponseWithDump(conn, response.ToArray(), "type31_ack");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendType31Ack: {ex.Message}");
            }
        }

        // ===================== E-lane receive/dispatch ==============================
        private async Task HandleCompressedE(RRConnection conn, LEReader reader)
        {
            Debug.Log($"[Game] HandleCompressedE: (zlib1) remaining={reader.Remaining}");

            // parse zlib1 header that client sends back (mirror our BuildCompressedEPayload_Zlib1)
            const int MIN_HDR = 3 + 3 + 1 + 3 + 5 + 4;
            if (reader.Remaining < MIN_HDR)
            {
                Debug.LogError($"[Game] HandleCompressedE: insufficient {reader.Remaining}");
                return;
            }

            uint msgDest = reader.ReadUInt24();
            uint compLen = reader.ReadUInt24();
            byte zero = reader.ReadByte();
            uint msgSource = reader.ReadUInt24();
            byte b1 = reader.ReadByte(); // 01
            byte b2 = reader.ReadByte(); // 00
            byte b3 = reader.ReadByte(); // 01
            byte b4 = reader.ReadByte(); // 00
            byte b5 = reader.ReadByte(); // 00
            uint unclen = reader.ReadUInt32();

            int zLen = (int)compLen - 12;
            if (zLen < 0 || reader.Remaining < zLen)
            {
                Debug.LogError($"[Game] HandleCompressedE: bad zLen={zLen} remaining={reader.Remaining}");
                return;
            }

            byte[] comp = reader.ReadBytes(zLen);
            byte[] inner;
            try
            {
                inner = (zLen == 0 || unclen == 0) ? Array.Empty<byte>() : ZlibUtil.Inflate(comp, unclen);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] HandleCompressedE: inflate failed {ex.Message}");
                return;
            }

            // inner begins with [channel][type]...
            await ProcessUncompressedEMessage(conn, inner);
        }

        private async Task ProcessUncompressedEMessage(RRConnection conn, byte[] inner)
        {
            // Never echo empty/short frames back to the client — they crash the entity manager.
            if (inner.Length < 2)
            {
                Debug.Log("[E] Dropping empty/short E-lane frame (len < 2).");
                return;
            }

            byte channel = inner[0];
            byte type = inner[1];

            // Retail client keep-alive: 0/2 -> respond with exactly two bytes (0,2)
            if (channel == 0 && type == 2)
            {
                var ack = new LEWriter();
                ack.WriteByte(0x00);
                ack.WriteByte(0x02);
                await SendCompressedEResponseWithDump(conn, ack.ToArray(), "e_ack_0_2");
                return;
            }

            // Everything else goes through normal channel handling.
            await HandleChannelMessage(conn, inner);
        }

        // ===========================================================================

        private async Task HandleType06(RRConnection conn, LEReader reader)
        {
            Debug.Log($"[Game] HandleType06: For client {conn.ConnId}");
        }

        private async Task<byte[]> SendMessage0x10(RRConnection conn, byte channel, byte[] body, string fullDumpTag = null)
        {
            uint clientId = GetClientId24(conn.ConnId);
            uint bodyLen = (uint)(body?.Length ?? 0);

            var w = new LEWriter();
            w.WriteByte(0x10);
            w.WriteUInt24((int)clientId); // peer is u24

            // Force u24 body length EXACTLY (3 bytes). Avoid any buggy helper.
            w.WriteByte((byte)(bodyLen & 0xFF));
            w.WriteByte((byte)((bodyLen >> 8) & 0xFF));
            w.WriteByte((byte)((bodyLen >> 16) & 0xFF));

            w.WriteByte(channel);
            if (bodyLen > 0) w.WriteBytes(body);

            var payload = w.ToArray();

            // Sanity: 0x10 frame must be 1+3+3+1 + bodyLen = 8 + bodyLen
            int expected = 8 + (int)bodyLen;
            if (payload.Length != expected)
                Debug.LogError($"[Wire][0x10] BAD SIZE: got={payload.Length} expected={expected}");

            if (!string.IsNullOrEmpty(fullDumpTag))
                DumpUtil.DumpFullFrame(fullDumpTag, payload);

            await conn.Stream.WriteAsync(payload, 0, payload.Length);
            Debug.Log($"[Wire][0x10] Sent peer=0x{clientId:X6} bodyLen(u24)={bodyLen} ch=0x{channel:X2} total={payload.Length}");
            return payload;
        }

        private uint GetClientId24(int connId) => _peerId24.TryGetValue(connId, out var id) ? id : 0u;

        public void Stop()
        {
            lock (_gameLoopLock) { _gameLoopRunning = false; }
            Debug.Log("[Game] Server stopping...");
        }

        // Entity Manager Interval Message
        private async Task SendEntityManagerInterval(RRConnection conn)
        {
            // GO: channel=7, type=0x0D, then four 32-bit fields, then two 16-bit budgets, then 0x06 EoS
            // Budget values below (100,20) match the GO defaults you shared.
            var w = new LEWriter();
            w.WriteByte(7);        // ClientEntity channel
            w.WriteByte(0x0D);     // EntityManagerMessageTypeInterval
            w.WriteUInt32(0);      // reserved (GO writes 0)
            w.WriteUInt32(16);     // tick interval in ms (16 ~= 60Hz; GO wrote a TickInterval here)
            w.WriteUInt32(0);      // reserved
            w.WriteUInt32(0);      // reserved
            w.WriteUInt16(100);    // Path budget: PerUpdate
            w.WriteUInt16(20);     // Path budget: PerPath
            w.WriteByte(0x06);     // End of update stream

            await SendCompressedAResponseWithDump(conn, w.ToArray(), "ce_interval_7_0d");
        }






        // Entity Manager Random Seed Message
        private async Task SendEntityManagerRandomSeed(RRConnection conn)
        {
            // GO sends this in the same update-stream style on A-lane.
            var w = new LEWriter();
            w.WriteByte(7);         // ClientEntity channel
            w.WriteByte(0x0C);      // EntityManagerMessageTypeRandomSeed
            w.WriteUInt32(0xC0FFEE01); // any deterministic seed is fine
            w.WriteByte(0x06);      // End of update stream

            await SendCompressedAResponseWithDump(conn, w.ToArray(), "ce_seed_7_0c");
        }
        // === ClientEntity (channel 7) minimal bootstrap over A/zlib3 ===
        // Mirrors the Go writer: BeginStream -> opcode -> payload [-> EndStream] per frame

        /* private async Task SendCE_Interval_A(RRConnection conn)
         {
             var body = new LEWriter();
             body.WriteByte(7);   // ClientEntity channel
             body.WriteByte(13);  // Op_Interval

             // Go server sends: currentTick, tickInterval(ms), 4 policy bytes,
             // then two u32 zeros, then two u16 thresholds, then EndStream (0x06).
             // Use UInt32 writes (LE) to match the on-wire bytes exactly.
             body.WriteUInt32(0);       // currentTick
             body.WriteUInt32(100);     // tickInterval (100 ms works fine)

             body.WriteByte(0x01);      // policy/config (same as Go)
             body.WriteByte(0x02);      // policy/config
             body.WriteByte(0x64);      // 100
             body.WriteByte(0x05);      // 5

             body.WriteUInt32(0);       // reserved
             body.WriteUInt32(0);       // reserved

             body.WriteUInt16(100);     // perUpdate
             body.WriteUInt16(20);      // perPath

             body.WriteByte(0x06);      // EndStream

             await SendCompressedAResponseWithDump(conn, body.ToArray(), "ce_interval_7_13");
         }*/

        private async Task SendCE_Interval_A(RRConnection conn)
        {
            var writer = new LEWriter();
            writer.WriteByte(7);     // ClientEntity channel
            writer.WriteByte(0x0D);  // Interval opcode

            // Tick values
            writer.WriteUInt32(1);   // Current tick
            writer.WriteUInt32(1);   // Tick interval
            writer.WriteUInt32(0);   // Movement buffer

            // PathManager budget
            writer.WriteUInt32(0);      // Unk
            writer.WriteUInt16(100);    // Budget per update
            writer.WriteUInt16(20);     // Budget per path

            writer.WriteByte(0x06);  // End stream

            await SendCompressedAResponseWithDump(conn, writer.ToArray(), "ce_interval_7_13");
        }


        private async Task SendCE_RandomSeed_A(RRConnection conn, uint seed = 0xC0FFEE01)
        {
            var body = new LEWriter();
            body.WriteByte(7);   // ClientEntity channel
            body.WriteByte(12);  // Op_RandomSeed
            body.WriteUInt32(seed);
            body.WriteByte(0x06);  // EndStream
            await SendCompressedAResponseWithDump(conn, body.ToArray(), "ce_randseed_7_12");
        }

        private async Task SendCE_Connect_A(RRConnection conn)
        {
            // The Go server ends a CE bootstrap stream with a single opcode 70 (Connected).
            // No extras, no length, no tail — just [7][70].
            var body = new LEWriter();
            body.WriteByte(7);   // ClientEntity channel
            body.WriteByte(70);  // Op_Connected (end-of-stream indicator)
            await SendCompressedAResponseWithDump(conn, body.ToArray(), "ce_connected_7_70");
        }

        private async Task SendCE_NowConnected(RRConnection conn)
        {
            // Message 385 (0x0181) - ClientEntity Now Connected
            // This uses extended opcode format: Channel, ExtendedMarker (0x81), MessageType (16-bit)
            // GO server sends this AFTER entity spawn completes
            
            Server.Game.GCObject avatar = null;
            if (_selectedCharacter.TryGetValue(conn.LoginName, out var character))
            {
                avatar = character?.Children?.FirstOrDefault(c => c.NativeClass == "Avatar");
            }
            
            var body = new LEWriter();
            body.WriteByte(7);              // ClientEntity channel
            body.WriteByte(0x81);           // Extended opcode marker
            body.WriteUInt16(385);          // Message type 385 (0x0181) in little-endian
            uint avatarId = (avatar != null) ? avatar.ID : 0u;
            body.WriteUInt32(avatarId);     // Avatar ID
            
            await SendCompressedAResponseWithDump(conn, body.ToArray(), "ce_now_connected_385");
            Debug.Log($"[Game] SendCE_NowConnected: Sent message 385 with avatarId={avatarId}");
        }


        // Entity Manager Entity Create Init Message
        // Entity Manager Entity Create Init Message
        private async Task SendEntityManagerEntityCreateInit(RRConnection conn)
        {
            Debug.Log($"[Game] SendEntityManagerEntityCreateInit: Sending entity manager entity create init message");

            var w = new LEWriter();
            w.WriteByte(7);                 // ClientEntity channel
            w.WriteByte(0x08);              // CreateInit
            w.WriteUInt16(0x50);            // Entity ID (Avatar/Player root)
            w.WriteByte(0xFF);              // GCClassRegistry::readType
            WriteCString(w, "Player");       // class
            WriteCString(w, conn.LoginName + "_NewHero"); // name
            w.WriteUInt32(5);
            w.WriteUInt32(5);
            w.WriteByte(0x06);              // EndStream

            await SendCompressedEResponseWithDump(conn, w.ToArray(), "entity_create_init_7_08");
            Debug.Log($"[Game] SendEntityManagerEntityCreateInit: DONE");
        }


        // Entity Manager Component Create Message
        // Entity Manager Component Create Message THIS IS WHAT SENDS TO ZONE AND WE CANT FIGURE OUT 
        // Entity Manager Component Create Message (UnitContainer CreateComponent + Init)
        private async Task SendEntityManagerComponentCreate(RRConnection conn)
        {
            // Matches your GO: 7 / 0x32 CreateComponent + UnitContainer children, with a single 0x06 at the end.
            const ushort avatarId = 0x0050;  // must match your earlier CreateInit entity id
            const ushort unitContainerId = 0x000A;

            var w = new LEWriter();
            w.WriteByte(7);         // ClientEntity channel
            w.WriteByte(0x32);      // CreateComponent

            // Header
            w.WriteUInt16(avatarId);        // parent entity (Avatar)
            w.WriteUInt16(unitContainerId); // component id
            w.WriteByte(0xFF);              // GCClassRegistry::readType (string)
            WriteCString(w, "UnitContainer");// exact class name
            w.WriteByte(0x01);              // required

            // UnitContainer::WriteInit children (must be 7, order matters)
            w.WriteByte(7);
            WriteInventoryChild(w, "avatar.base.Inventory", 1);
            WriteInventoryChild(w, "avatar.base.Bank", 2);
            WriteInventoryChild(w, "avatar.base.Bank2", 2);
            WriteInventoryChild(w, "avatar.base.Bank3", 2);
            WriteInventoryChild(w, "avatar.base.Bank4", 2);
            WriteInventoryChild(w, "avatar.base.Bank5", 2);
            WriteInventoryChild(w, "avatar.base.Hotbar", 2);

            w.WriteByte(0x06);      // End of update stream

            await SendCompressedAResponseWithDump(conn, w.ToArray(), "ce_unitcontainer_createinit");
        }



        private static void WriteInventoryChild(LEWriter w, string gcType, byte inventoryId)
        {
            w.WriteByte(0xFF);       // lookup by string
            WriteCString(w, gcType);  // e.g., "avatar.base.Inventory"
            w.WriteByte(inventoryId); // Inventory ID (1 = backpack, 2 = bank)
            w.WriteByte(0x01);       // Cannot be 0 (from GO server)
            w.WriteByte(0x00);       // Item count (0 = empty inventory)
        }




        // Entity Manager Connect Message
        // Entity Manager Connect Message
        private async Task SendEntityManagerConnect(RRConnection conn)
        {
            Debug.Log($"[Game] SendEntityManagerConnect: Sending entity manager connect message");

            var w = new LEWriter();
            w.WriteByte(7);              // ClientEntity channel
            w.WriteByte(70);             // Connect (0x46)

            w.WriteUInt32(33752069);     // char id (keep consistent across your zone messages)
            w.WriteUInt32(1);            // same as your GO flow

            w.WriteByte(0x06);           // <-- EndStream (this was missing)

            await SendCompressedEResponseWithDump(conn, w.ToArray(), "entity_connect_7_70");
            Debug.Log($"[Game] SendEntityManagerConnect: DONE");
        }






        // ============================================================================
        // PLAYER ENTITY SPAWN - Channel 7 (ClientEntity) Format
        // ============================================================================
        // This is the CORRECT way to spawn a player into the game world.
        // Uses Channel 7 entity creation opcodes, NOT Channel 4 DFC format.
        // ============================================================================

        /// <summary>
        /// Sends the player entity spawn sequence using Channel 7 entity creation opcodes.
        /// This creates the player's avatar and all required components in the game world.
        /// </summary>
        private async Task SendPlayerEntitySpawn(RRConnection conn)
        {
            Debug.Log($"[Game] SendPlayerEntitySpawn: Creating player entity for client {conn.ConnId}");

            try
            {
                var writer = new LEWriter();

                // Channel 7 (ClientEntity) - this is where entity creation happens
                writer.WriteByte(7);

                // Create the complete player entity structure using opcodes
                CreatePlayerEntity(conn, writer);

                // End stream with Connected signal (opcode 0x46)
                writer.WriteByte(0x46);  // EndStreamConnected

                // Send on A-lane (compressed)
                await SendCompressedAResponseWithDump(conn, writer.ToArray(), "player_entity_spawn_7");

                Debug.Log($"[Game] SendPlayerEntitySpawn: ✅ Player entity spawned successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendPlayerEntitySpawn: ❌ ERROR - {ex.Message}");
                Debug.LogError($"[Game] SendPlayerEntitySpawn: Stack trace - {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Sends the follow client message to enable client control of the avatar.
        /// This MUST be sent after entity spawn for the player to be able to move.
        /// </summary>
        private async Task SendFollowClient(RRConnection conn)
        {
            Debug.Log($"[Game] SendFollowClient: FIXED_VERSION_2025_10_03 - Enabling client control for client {conn.ConnId}");

            try
            {
                const ushort AVATAR_ID = 0x0001;         // Avatar entity ID
                const ushort UNIT_BEHAVIOR_ID = 0x0056;  // UnitBehavior component ID

                var writer = new LEWriter();

                // Channel 7 (ClientEntity)
                writer.WriteByte(7);

                // Component Update opcode (0x35)
                writer.WriteByte(0x35);

                // Component ID must be big-endian (high byte first)
                // NOTE: Component Update uses component ID directly, NOT entity ID
                writer.WriteByte((byte)((UNIT_BEHAVIOR_ID >> 8) & 0xFF));  // High byte
                writer.WriteByte((byte)(UNIT_BEHAVIOR_ID & 0xFF));         // Low byte

                // Update type 0x64 = client control
                writer.WriteByte(0x64);
                writer.WriteByte(0x01);  // Enable client control

                // WriteSynch data (from GO server)
                writer.WriteByte(0x02);  // Synch flag (0x02 for dungeon, 0x00 for town)
                writer.WriteUInt32(0);   // Synch value (EntitySynchInfo)

                // Send on A-lane
                await SendCompressedAResponseWithDump(conn, writer.ToArray(), "follow_client_7");

                Debug.Log($"[Game] SendFollowClient: ✅ Client control enabled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendFollowClient: ❌ ERROR - {ex.Message}");
                Debug.LogError($"[Game] SendFollowClient: Stack trace - {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Creates the complete player entity structure using Channel 7 opcodes.
        /// Matches the GO server's WriteCreateNewPlayerEntity method.
        /// </summary>
        private void CreatePlayerEntity(RRConnection conn, LEWriter writer)
        {
            // Entity IDs
            const ushort AVATAR_ID = 0x0001;
            const ushort PLAYER_ID = 0x0002;
            const ushort MANIPULATORS_ID = 0x0052;
            const ushort EQUIPMENT_ID = 0x0053;
            const ushort UNIT_CONTAINER_ID = 0x000A;
            const ushort MODIFIERS_ID = 0x0054;
            const ushort SKILLS_ID = 0x0055;
            const ushort UNIT_BEHAVIOR_ID = 0x0056;  // Component ID for UnitBehavior
            const ushort QUEST_MANAGER_ID = 0x0057;
            const ushort DIALOG_MANAGER_ID = 0x0058;

            Debug.Log($"[Game] CreatePlayerEntity: Building entity structure");

            // 1. Create Avatar entity (opcode 0x01)
            WriteCreateEntity(writer, AVATAR_ID, "Avatar");

            // 2. Create Player entity (opcode 0x01)
            WriteCreateEntity(writer, PLAYER_ID, "Player");

            // 3. Init Player (opcode 0x02)
            WriteInitEntity(writer, PLAYER_ID);
            writer.WriteByte(0x00);  // Init data

            // 4. Create Manipulators component
            WriteCreateComponent(writer, AVATAR_ID, MANIPULATORS_ID, "Manipulators");

            // 5. Create Equipment component
            WriteCreateComponent(writer, AVATAR_ID, EQUIPMENT_ID, "avatar.base.Equipment");

            // 6. Create QuestManager component
            WriteCreateComponent(writer, PLAYER_ID, QUEST_MANAGER_ID, "QuestManager");

            // 7. Create DialogManager component
            WriteCreateComponent(writer, PLAYER_ID, DIALOG_MANAGER_ID, "DialogManager");

            // 8. Create UnitContainer component (7 inventory children)
            // UnitContainer is special - it needs child count as part of component data
            writer.WriteByte(0x32);  // CreateComponent opcode
            // Parent ID must be big-endian (high byte first)
            writer.WriteByte((byte)((AVATAR_ID >> 8) & 0xFF));  // High byte
            writer.WriteByte((byte)(AVATAR_ID & 0xFF));         // Low byte
            // Component ID must be big-endian (high byte first)
            writer.WriteByte((byte)((UNIT_CONTAINER_ID >> 8) & 0xFF));  // High byte
            writer.WriteByte((byte)(UNIT_CONTAINER_ID & 0xFF));         // Low byte
            writer.WriteByte(0xFF);  // Marker
            writer.WriteCString("UnitContainer");  // Type string
            writer.WriteByte(0x01);  // Flags
            // UnitContainer::WriteInit data (from GO server)
            writer.WriteUInt32(1);   // Unknown (from GO server)
            writer.WriteUInt32(1);   // Unknown (from GO server)
            writer.WriteByte(0x03);  // Inventory count (3 inventories - matching GO server!)
            // Write only 3 inventories like GO server does
            WriteInventoryChild(writer, "avatar.base.Inventory", 1);
            WriteInventoryChild(writer, "avatar.base.Bank", 2);
            WriteInventoryChild(writer, "avatar.base.TradeInventory", 2);
            writer.WriteByte(0x00);  // End marker (from GO server)

            // 9. Create Modifiers component
            WriteCreateComponent(writer, AVATAR_ID, MODIFIERS_ID, "Modifiers");

            // 10. Create Skills component
            WriteCreateComponent(writer, AVATAR_ID, SKILLS_ID, "Skills");

            // 11. Create UnitBehavior component
            WriteCreateComponent(writer, AVATAR_ID, UNIT_BEHAVIOR_ID, "UnitBehavior");

            // 12. Init Avatar (opcode 0x02)
            WriteInitEntity(writer, AVATAR_ID);
            writer.WriteByte(0x01);  // Init data
            writer.WriteByte(0x01);  // Init data
            writer.WriteByte(0x00);  // Init data

            Debug.Log($"[Game] CreatePlayerEntity: ✅ Entity structure complete");
        }

        /// <summary>
        /// Writes a Create Entity opcode (0x01) with entity ID and type string.
        /// </summary>
        private void WriteCreateEntity(LEWriter writer, ushort entityId, string typeString)
        {
            writer.WriteByte(0x01);           // Create Entity opcode
            // Entity ID must be big-endian (high byte first)
            writer.WriteByte((byte)((entityId >> 8) & 0xFF));  // High byte
            writer.WriteByte((byte)(entityId & 0xFF));         // Low byte
            writer.WriteByte(0xFF);           // Unknown byte
            writer.WriteCString(typeString);  // Entity type (null-terminated)
        }

        /// <summary>
        /// Writes an Init Entity opcode (0x02) with entity ID.
        /// </summary>
        private void WriteInitEntity(LEWriter writer, ushort entityId)
        {
            writer.WriteByte(0x02);        // Init Entity opcode
            // Entity ID must be big-endian (high byte first)
            writer.WriteByte((byte)((entityId >> 8) & 0xFF));  // High byte
            writer.WriteByte((byte)(entityId & 0xFF));         // Low byte
        }

        /// <summary>
        /// Writes a Create Component opcode (0x32) followed by Init (0x02).
        /// </summary>
        private void WriteCreateComponent(LEWriter writer, ushort parentId, ushort componentId, string typeString)
        {
            writer.WriteByte(0x32);            // Create Component opcode
            // Parent Entity ID must be big-endian (high byte first)
            writer.WriteByte((byte)((parentId >> 8) & 0xFF));     // High byte
            writer.WriteByte((byte)(parentId & 0xFF));            // Low byte
            // Component ID must be big-endian (high byte first)
            writer.WriteByte((byte)((componentId >> 8) & 0xFF));  // High byte
            writer.WriteByte((byte)(componentId & 0xFF));         // Low byte
            writer.WriteByte(0xFF);            // Unknown byte
            writer.WriteCString(typeString);   // Component type (null-terminated)
            writer.WriteByte(0x01);            // Flags
            // Component init data should be written here by caller if needed
            // For most components, this is 0x00 (empty/no children)
            writer.WriteByte(0x00);            // Init data: 0 children/items
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

            // === Added: Proper handling of server list response from Go server ===
            private void HandleServerList(LEReader reader)
            {
                byte serverCount = reader.ReadByte();
                byte lastServerId = reader.ReadByte();

                for (int i = 0; i < serverCount; i++)
                {
                    byte serverId = reader.ReadByte();
                    uint ip = reader.ReadUInt32();
                    uint port = reader.ReadUInt32();   // Go writes port as UInt32
                    ushort currentPlayers = reader.ReadUInt16();
                    ushort maxPlayers = reader.ReadUInt16();
                    byte status = reader.ReadByte();

                    Debug.Log($"[ServerList] ID={serverId} IP={ip} Port={port} Online={currentPlayers}/{maxPlayers} Status={status}");

                    // TODO: Integrate with your _playerCharacters or server/character selection system
                }
            }

        }
    }
}