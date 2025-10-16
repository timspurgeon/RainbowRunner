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
using Unity.VisualScripting.Antlr3.Runtime.Tree;

namespace Server.Game
{
    public class RRConnection
    {
        public int ConnId { get; set; }
        public string LoginName { get; set; }
        public TcpClient TcpClient { get; set; }
        public NetworkStream Stream { get; set; }
        public bool IsConnected { get; set; } = true;
        public bool ZoneInitialized { get; set; } = false; // Track if zone join has been processed
    }

    public class GameServer
    {
        private readonly NetServer net;
        private readonly string bindIp;
        private readonly int port;

        private static int NextConnId = 1;
        private readonly ConcurrentDictionary<int, RRConnection> _connections = new ConcurrentDictionary<int, RRConnection>();
        private readonly ConcurrentDictionary<int, string> _users = new ConcurrentDictionary<int, string>();
        private readonly ConcurrentDictionary<int, uint> _peerId24 = new ConcurrentDictionary<int, uint>();
        
        // Track player characters
        private readonly ConcurrentDictionary<int, List<GCObject>> _playerCharacters = new ConcurrentDictionary<int, List<GCObject>>();
        private readonly ConcurrentDictionary<string, GCObject> _selectedCharacter = new ConcurrentDictionary<string, GCObject>();
        private readonly ConcurrentDictionary<string, bool> _spawnedPlayers = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, MessageQueue> _messageQueues = new ConcurrentDictionary<string, MessageQueue>();
        
        // Entity ID tracking
        private uint _nextEntityId = 10; // Start from 10 to match Python
        
        private bool _gameLoopRunning = false;
        private readonly object _gameLoopLock = new object();

        public GameServer(string ip, int port)
        {
            bindIp = ip;
            this.port = port;
            net = new NetServer(ip, port, HandleClient);
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
                while (_gameLoopRunning)
                {
                    try
                    {
                        // Process message queues
                        foreach (var kvp in _messageQueues)
                        {
                            if (kvp.Value != null)
                            {
                                kvp.Value.ProcessImmediately();
                            }
                        }
                        
                        await Task.Delay(100); // Process every 100ms
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[GameLoop] Error: {ex.Message}");
                    }
                }
            });
        }

        private async Task HandleClient(TcpClient tcpClient)
        {
            try
            {
                int connId = NextConnId++;
                var conn = new RRConnection
                {
                    ConnId = connId,
                    TcpClient = tcpClient,
                    Stream = tcpClient.GetStream(),
                    LoginName = $"User{connId}"
                };

                _connections[connId] = conn;
                Debug.Log($"[Game] HandleClient: New connection #{connId}");

                // Send initial connection response
                await SendInitialConnectionResponse(conn);

                byte[] buffer = new byte[4096];
                while (conn.IsConnected && tcpClient.Connected)
                {
                    try
                    {
                        int bytesRead = await conn.Stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0)
                        {
                            Debug.Log($"[Game] HandleClient: Connection #{connId} closed by client");
                            conn.IsConnected = false;
                            break;
                        }

                        byte[] data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);

                        await ProcessClientData(conn, data);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Game] HandleClient: Error reading from connection #{connId}: {ex.Message}");
                        conn.IsConnected = false;
                        break;
                    }
                }

                // Clean up connection
                _connections.TryRemove(connId, out _);
                Debug.Log($"[Game] HandleClient: Connection #{connId} cleanup complete");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] HandleClient: Failed to handle client: {ex.Message}");
            }
        }

        private async Task SendInitialConnectionResponse(RRConnection conn)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x01); // Channel 1
            writer.WriteByte(0x01); // Message type 1
            
            // Write connection ID
            writer.WriteUInt32((uint)conn.ConnId);
            
            // Write server name
            writer.WriteCString("RainbowRunner Unity Server");
            
            // Write server version
            writer.WriteCString("1.0.0");
            
            await SendCompressedAResponseWithDump(conn, 0x01, 0x0F, writer.ToArray(), "initial_connection");
        }

        private async Task ProcessClientData(RRConnection conn, byte[] data)
        {
            try
            {
                // Decompress if needed
                byte[] decompressedData = data;
                if (data.Length > 2 && data[0] == 0x78 && data[1] == 0xDA)
                {
                    decompressedData = Zlib.Decompress(data);
                }
                
                if (decompressedData.Length < 2)
                {
                    Debug.LogWarning($"[Game] ProcessClientData: Data too short ({decompressedData.Length} bytes)");
                    return;
                }
                
                byte channel = decompressedData[0];
                byte messageType = decompressedData[1];
                
                byte[] messageData = new byte[decompressedData.Length - 2];
                Array.Copy(decompressedData, 2, messageData, 0, messageData.Length);
                
                Debug.Log($"[Game] ProcessClientData: Channel {channel}, Type {messageType}, Data {messageData.Length} bytes");
                
                switch (channel)
                {
                    case 4: // Character channel
                        await HandleCharacterChannelMessages(conn, messageType, messageData);
                        break;
                    case 13: // Zone channel
                        await HandleZoneChannelMessages(conn, messageType, messageData);
                        break;
                    case 7: // Client entity channel
                        await HandleClientEntityChannelMessages(conn, messageType, messageData);
                        break;
                    default:
                        Debug.LogWarning($"[Game] ProcessClientData: Unhandled channel {channel}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] ProcessClientData: Error processing client data: {ex.Message}");
                Debug.Log($"[Game] ProcessClientData: Data dump: {BitConverter.ToString(data)}");
            }
        }

        private async Task HandleCharacterChannelMessages(RRConnection conn, byte messageType, byte[] data)
        {
            Debug.Log($"[Game] HandleCharacterChannelMessages: Type 0x{messageType:X2} for client {conn.ConnId}");

            switch (messageType)
            {
                case 3: // Character connected
                    Debug.Log($"[Game] HandleCharacterChannelMessages: Character connected");
                    await SendCharacterConnectedResponse(conn);
                    break;
                case 4: // Get character list
                    Debug.Log($"[Game] HandleCharacterChannelMessages: Get character list");
                    await SendCharacterList(conn);
                    break;
                case 5: // Character play
                    Debug.Log($"[Game] HandleCharacterChannelMessages: Character play");
                    await HandleCharacterPlay(conn, data);
                    break;
                case 2: // Character create
                    Debug.Log($"[Game] HandleCharacterChannelMessages: Character create");
                    await HandleCharacterCreate(conn, data);
                    break;
                default:
                    Debug.LogWarning($"[Game] HandleCharacterChannelMessages: Unhandled character msg 0x{messageType:X2}");
                    break;
            }
        }

        private async Task SendCharacterConnectedResponse(RRConnection conn)
        {
            Debug.Log($"[Game] SendCharacterConnectedResponse: For client {conn.ConnId} - using 0x10 format like Go server");

            // Send using 0x10 message format like Go server
            var response = new LEWriter();
            response.WriteByte(0x0A);  // Channel
            response.WriteByte(0x03);  // Message type

            await SendMessage0x10(conn, 0x01, response.ToArray());
            Debug.Log($"[Game] Sent character connected via 0x10 message");
        }

        private async Task SendCharacterList(RRConnection conn)
        {
            Debug.Log($"[Game] SendCharacterList: For client {conn.ConnId} - using 0x10 format like Go server");

            // Check if we already have characters for this user
            if (_playerCharacters.TryGetValue(conn.ConnId, out var existingChars) && existingChars.Count > 0)
            {
                // Send existing character list
                var w = new LEWriter();
                w.WriteByte(4);   // Channel 4  
                w.WriteByte(3);   // Character list message
                w.WriteByte((byte)existingChars.Count);   // Character count

                foreach (var character in existingChars)
                {
                    w.WriteUInt32(character.ID);
                    WritePlayerWithGCObject(w, character.Name);
                }

                await SendCompressedAResponse(conn, 0x01, 0x0F, w.ToArray());
                Debug.Log($"[Game] SendCharacterList: Sent existing character list ({existingChars.Count} characters) to client {conn.ConnId}");
            }
            else
            {
                // Send empty character list to trigger creation like Go server
                var response = new LEWriter();
                response.WriteByte(4);
                response.WriteByte(3);
                response.WriteByte(0);   // 0 characters to trigger creation

                await SendCompressedAResponse(conn, 0x01, 0x0F, response.ToArray());
                Debug.Log($"[Game] SendCharacterList: Sent empty list via 0x10 to trigger character creation");
            }
        }

        private async Task HandleCharacterPlay(RRConnection conn, byte[] data)
        {
            Debug.Log($"[Game] HandleCharacterPlay: For client {conn.ConnId}");
            Debug.Log($"[Game] HandleCharacterPlay: Data: {BitConverter.ToString(data)}");

            if (data.Length >= 6)
            {
                var reader = new LEReader(data);
                reader.ReadByte(); // Skip channel (4)
                reader.ReadByte(); // Skip message type (5)

                if (reader.Remaining >= 4)
                {
                    uint selectedCharId = reader.ReadUInt32();
                    Debug.Log($"[Game] HandleCharacterPlay: Selected character ID: {selectedCharId}");

                    if (selectedCharId == 0)
                    {
                        // Character ID 0 means "create new character"
                        Debug.Log($"[Game] HandleCharacterPlay: Redirecting to character creation");
                        await InitiateCharacterCreation(conn);
                        return;
                    }
                    else
                    {
                        // Find the selected character
                        if (_playerCharacters.TryGetValue(conn.ConnId, out var characters))
                        {
                            var character = characters.FirstOrDefault(c => c.ID == selectedCharId);
                            if (character != null)
                            {
                                _selectedCharacter[conn.LoginName] = character;
                                Debug.Log($"[Game] HandleCharacterPlay: Selected character {character.Name} for {conn.LoginName}");
                            }
                        }
                    }
                }
            }

            // Handle normal character selection
            Debug.Log($"[Game] HandleCharacterPlay: Normal character selection");
            var response = new LEWriter();
            response.WriteByte(4);
            response.WriteByte(5);
            response.WriteByte(1); // Success
            await SendCompressedAResponse(conn, 0x01, 0x0F, response.ToArray());
            
            // Send zone entry completion
            await Task.Delay(100);
            await SendZoneEntryComplete(conn);
        }

        private async Task InitiateCharacterCreation(RRConnection conn)
        {
            Debug.Log($"[Game] InitiateCharacterCreation: Starting character creation for client {conn.ConnId}");

            // Send character creation initiation
            var createInit = new LEWriter();
            createInit.WriteByte(4);  // Channel 4
            createInit.WriteByte(1);  // Character create initiation
            createInit.WriteByte(1);  // Success

            await SendCompressedAResponse(conn, 0x01, 0x0F, createInit.ToArray());
            Debug.Log($"[Game] InitiateCharacterCreation: Sent character create initiation");

            await Task.Delay(100);

            // Send go-to-zone message
            await SendGoToZone(conn, "town");

            await Task.Delay(100);

            // Send zone ready state
            var zoneReady = new LEWriter();
            zoneReady.WriteByte(13); // Zone channel
            zoneReady.WriteByte(1);  // Zone ready
            zoneReady.WriteUInt32(1); // Zone ID

            await SendCompressedAResponse(conn, 0x01, 0x0F, zoneReady.ToArray());
            Debug.Log($"[Game] InitiateWorldEntry: Sent zone ready state");
        }

        private async Task HandleCharacterCreate(RRConnection conn, byte[] data)
        {
            Debug.Log($"[Game] HandleCharacterCreate: Character creation request from client {conn.ConnId}");
            Debug.Log($"[Game] HandleCharacterCreate: Data ({data.Length} bytes): {BitConverter.ToString(data)}");

            // Parse character creation data if needed
            string characterName = $"{conn.LoginName}_NewHero";
            uint newCharId = (uint)(conn.ConnId * 100 + 1);

            var response = new LEWriter();
            response.WriteByte(4);  // Channel 4
            response.WriteByte(2);  // Character create response
            response.WriteByte(1);  // Success
            response.WriteUInt32(newCharId);

            // Write the new character object
            WritePlayerWithGCObject(response, characterName);

            await SendCompressedAResponse(conn, 0x01, 0x0F, response.ToArray());
            Debug.Log($"[Game] HandleCharacterCreate: Sent character creation success for {characterName} (ID: {newCharId})");

            // After creation, send updated character list
            await Task.Delay(100);
            await SendUpdatedCharacterList(conn, newCharId, characterName);
        }

        private async Task SendUpdatedCharacterList(RRConnection conn, uint charId, string charName)
        {
            Debug.Log($"[Game] SendUpdatedCharacterList: Sending list with newly created character");

            var w = new LEWriter();
            w.WriteByte(4);   // Channel 4  
            w.WriteByte(3);   // Character list message
            w.WriteByte(1);   // 1 character now

            w.WriteUInt32(charId);
            WritePlayerWithGCObject(w, charName);

            await SendCompressedAResponse(conn, 0x01, 0x0F, w.ToArray());
            Debug.Log($"[Game] SendUpdatedCharacterList: Sent updated character list with new character");
        }

        private async Task SendZoneEntryComplete(RRConnection conn)
        {
            Debug.Log($"[Game] SendZoneEntryComplete: For client {conn.ConnId}");

            try
            {
                // Send zone entry complete - this might transition client to game world
                var zoneComplete = new LEWriter();
                zoneComplete.WriteByte(13); // Zone channel
                zoneComplete.WriteByte(9);  // Zone entry complete
                zoneComplete.WriteUInt32(1); // Zone ID
                zoneComplete.WriteByte(1);   // Success

                await SendCompressedAResponse(conn, 0x01, 0x0F, zoneComplete.ToArray());
                Debug.Log($"[Game] SendZoneEntryComplete: Sent zone entry complete");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendZoneEntryComplete: Failed: {ex.Message}");
            }
        }

        private void WritePlayerWithGCObject(LEWriter writer, string name)
        {
            Debug.Log($"[Game] WritePlayerWithGCObject: Writing player '{name}'");

            // Create a complete player object like the Go server does
            var player = Objects.NewPlayer(name);
            
            // Write the player object using DFC format
            player.WriteFullGCObject(writer);
        }

        private async Task SendGoToZone(RRConnection conn, string zoneName)
        {
            Debug.Log($"[Game] SendGoToZone: Sending '{zoneName}' to client {conn.ConnId}");

            var w = new LEWriter();
            w.WriteByte(9);
            w.WriteByte(48);

            var zoneBytes = Encoding.UTF8.GetBytes(zoneName);
            w.WriteBytes(zoneBytes);
            w.WriteByte(0);

            byte[] goToZoneData = w.ToArray();
            Debug.Log($"[Game] SendGoToZone: Go-to-zone data ({goToZoneData.Length} bytes): {BitConverter.ToString(goToZoneData)}");

            await SendCompressedAResponse(conn, 0x01, 0x0F, goToZoneData);
            Debug.Log($"[Game] SendGoToZone: Sent go-to-zone '{zoneName}' to client {conn.ConnId}");
        }

        private async Task HandleZoneChannelMessages(RRConnection conn, byte messageType, byte[] data)
        {
            Debug.Log($"[Game] HandleZoneChannelMessages: Type 0x{messageType:X2} for client {conn.ConnId}");

            switch (messageType)
            {
                case 6: // Zone join request
                    Debug.Log($"[Game] HandleZoneChannelMessages: Zone join request");
                    await HandleZoneJoinRequest(conn);
                    break;
                case 8: // Zone ready
                    Debug.Log($"[Game] HandleZoneChannelMessages: Zone ready response");
                    await HandleZoneReady(conn);
                    break;
                case 0: // Zone connected
                    Debug.Log($"[Game] HandleZoneChannelMessages: Zone connected");
                    await HandleZoneConnected(conn);
                    break;
                case 1: // Zone ready response
                    Debug.Log($"[Game] HandleZoneChannelMessages: Zone ready confirmation");
                    await HandleZoneReadyResponse(conn);
                    break;
                case 5: // Zone instance count
                    Debug.Log($"[Game] HandleZoneChannelMessages: Zone instance count");
                    await HandleZoneInstanceCount(conn);
                    break;
                default:
                    Debug.LogWarning($"[Game] HandleZoneChannelMessages: Unhandled zone msg 0x{messageType:X2}");
                    break;
            }
        }

        private async Task HandleZoneJoinRequest(RRConnection conn)
        {
            // ====== ADD THESE LINES HERE ======
            Debug.LogError($"[ZONE-JOIN-ENTRY] ==================== HandleZoneJoinRequest ENTRY for {conn.LoginName} ====================");
            Debug.LogError($"[ZONE-JOIN-ENTRY] ZoneInitialized: {conn.ZoneInitialized}");
            Debug.LogError($"[ZONE-JOIN-ENTRY] This method should only run ONCE per player!");
            // ===================================

            Debug.Log($"[Game] HandleZoneJoinRequest: ==================== ENTRY ====================");
            Debug.Log($"[Game] HandleZoneJoinRequest: Client {conn.LoginName} sent Zone/6 (join request)");

            // ✓ PREVENT DUPLICATE ZONE JOINS
            if (conn.ZoneInitialized)
            {
                Debug.LogWarning($"[Game] HandleZoneJoinRequest: ⚠️ Zone already initialized for {conn.LoginName}, ignoring duplicate 13/6 request");
                return;
            }

            Debug.Log($"[Game] HandleZoneJoinRequest: ⭐ Processing zone join for {conn.LoginName} ⭐");

            // Mark as initialized NOW to prevent race conditions
            conn.ZoneInitialized = true;
            Debug.Log($"[Game] HandleZoneJoinRequest: Set ZoneInitialized=true for {conn.LoginName}");

            // ==================== STEP 1: Send Zone Ready (13/1) ====================
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 1] Building Zone Ready message (13/1)...");
            var zoneReady = new LEWriter();
            zoneReady.WriteByte(13);  // Zone channel
            zoneReady.WriteByte(1);   // Ready message
            zoneReady.WriteUInt32(1);
            zoneReady.WriteUInt16(0x12);
            for (int i = 0; i < 0x12; i++)
                zoneReady.WriteUInt32(0xFFFFFFFF);

            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 1] Sending Zone Ready (13/1) - {zoneReady.ToArray().Length} bytes");
            await SendCompressedEResponseWithDump(conn, zoneReady.ToArray(), "zone_ready_13_1");
            Debug.Log($"[Game] HandleZoneJoinRequest: ✓ [STEP 1] Zone Ready (13/1) sent successfully");

            // ⭐ CRITICAL: Wait for client to process 13/1
            await Task.Delay(50);
            Debug.Log($"[Game] HandleZoneJoinRequest: Waited 50ms for client to process Zone Ready");

            // ==================== STEP 2: Send Instance Count (13/5) ====================
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 2] Building Instance Count message (13/5)...");
            var instanceCount = new LEWriter();
            instanceCount.WriteByte(13);  // Zone channel
            instanceCount.WriteByte(5);   // Instance count message
            instanceCount.WriteUInt32(1); // Current instance
            instanceCount.WriteUInt32(1); // Total instances

            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 2] Sending Instance Count (13/5) - {instanceCount.ToArray().Length} bytes");
            await SendCompressedEResponseWithDump(conn, instanceCount.ToArray(), "zone_instance_count_13_5");
            Debug.Log($"[Game] HandleZoneJoinRequest: ✓ [STEP 2] Instance Count (13/5) sent successfully");

            // ==================== CRITICAL: Wait for client to reach State 115 ====================
            Debug.Log($"[Game] HandleZoneJoinRequest: ⭐⭐⭐ WAITING 400ms for client to enter State 115 and prepare for A-lane messages ⭐⭐⭐");
            await Task.Delay(400);
            Debug.Log($"[Game] HandleZoneJoinRequest: Client should now be in State 115 and ready for spawn data");

            // ==================== STEP 3: BUILD SPAWN DIRECTLY - NO QUEUE! ====================
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 3] ⭐⭐⭐ BUILDING SPAWN DATA DIRECTLY (BYPASSING MessageQueue) ⭐⭐⭐");
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 3] This matches Go server pattern: build once, send immediately");

            byte[] spawnOps = BuildPlayerSpawnOperationsArray(conn);
            if (spawnOps == null)
            {
                Debug.LogError($"[Game] HandleZoneJoinRequest: ❌ Failed to build spawn operations!");
                return;
            }

            Debug.Log($"[Game] HandleZoneJoinRequest: ✓ [STEP 3] Built spawn operations: {spawnOps.Length} bytes (should be 834)");

            // ==================== STEP 4: WRAP AND SEND IMMEDIATELY ====================
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 4] Wrapping spawn operations with BeginStream/EndStreamConnected/EndStream");

            var finalWriter = new LEWriter();
            finalWriter.WriteByte(0x07);  // BeginStream
            finalWriter.WriteBytes(spawnOps);  // All spawn operations (includes PathManager + entities)
            finalWriter.WriteByte(0x46);  // EndStreamConnected (MESSAGE 385!)
            finalWriter.WriteByte(0x06);  // EndStream

            var finalPacket = finalWriter.ToArray();
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 4] Final spawn packet: {finalPacket.Length} bytes (should be 837)");
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 4] Structure: 0x07 + {spawnOps.Length} ops + 0x46 + 0x06 = {finalPacket.Length} bytes");

            // ⭐⭐⭐ SEND IMMEDIATELY - NO QUEUE! ⭐⭐⭐
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 4] ⭐⭐⭐ SENDING SPAWN PACKET IMMEDIATELY (like Go server) ⭐⭐⭐");
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 4] ⏰ Current time: {DateTime.Now:HH:mm:ss.fff}");

            await SendCompressedAResponseWithDump(conn, 0x01, 0x0F, finalPacket, "player_spawn_IMMEDIATE");

            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 4] ⏰ Spawn sent at: {DateTime.Now:HH:mm:ss.fff}");
            Debug.Log($"[Game] HandleZoneJoinRequest: ✓✓✓ [STEP 4] SPAWN DATA SENT ON A-LANE! Client should receive message 385!");

            // ==================== STEP 5: Mark Spawned ====================
            _spawnedPlayers[conn.LoginName] = true;
            Debug.Log($"[Game] HandleZoneJoinRequest: ✓ [STEP 5] Marked {conn.LoginName} as spawned in _spawnedPlayers");

            // ==================== STEP 6: NOW Create MessageQueue for Future Messages ====================
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 6] Creating MessageQueue for FUTURE messages (not spawn!)");
            if (!_messageQueues.ContainsKey(conn.LoginName))
            {
                _messageQueues[conn.LoginName] = new MessageQueue(
                    async (d, t, data, tag) => await SendCompressedAResponseWithDump(conn, d, t, data, tag),
                    () => _spawnedPlayers.ContainsKey(conn.LoginName) && _spawnedPlayers[conn.LoginName]
                );
                _messageQueues[conn.LoginName].Start();
                Debug.Log($"[Game] HandleZoneJoinRequest: ✓ [STEP 6] MessageQueue created and started for future updates");
            }
            else
            {
                Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 6] MessageQueue already exists for {conn.LoginName}");
            }

            // ⭐⭐⭐ NEW: Mark spawn complete after delay ⭐⭐⭐
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 6.5] Setting up spawn complete timer (2000ms delay)");
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Wait 2000ms for client to process spawn packet

                    if (_messageQueues.TryGetValue(conn.LoginName, out var queue))
                    {
                        queue.MarkSpawnComplete();
                        Debug.Log($"[GameServer] ⭐ Spawn processing time elapsed, marked complete for {conn.LoginName}");
                    }
                    else
                    {
                        Debug.LogWarning($"[GameServer] No MessageQueue found for {conn.LoginName} when trying to mark spawn complete");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GameServer] Error marking spawn complete: {ex.Message}");
                }
            });
            Debug.Log($"[Game] HandleZoneJoinRequest: ✓ [STEP 6.5] Spawn complete timer started");

            // ==================== STEP 7: Send Zone/8 Final Ready ====================
            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 7] Waiting 100ms before sending final Zone/8...");
            await Task.Delay(100);

            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 7] Building final Zone Ready message (13/8)...");
            var zoneReadyFinal = new LEWriter();
            zoneReadyFinal.WriteByte(13);  // Zone channel
            zoneReadyFinal.WriteByte(8);   // Final ready message

            Debug.Log($"[Game] HandleZoneJoinRequest: [STEP 7] Sending Zone/8 (spawn complete) - {zoneReadyFinal.ToArray().Length} bytes");
            await SendCompressedEResponseWithDump(conn, zoneReadyFinal.ToArray(), "zone_ready_final_13_8");
            Debug.Log($"[Game] HandleZoneJoinRequest: ✓ [STEP 7] Zone/8 (final ready) sent successfully");

            // ==================== COMPLETE ====================
            Debug.Log($"[Game] HandleZoneJoinRequest: ⭐⭐⭐ SEQUENCE COMPLETE ⭐⭐⭐");
            Debug.Log($"[Game] HandleZoneJoinRequest: Summary:");
            Debug.Log($"[Game] HandleZoneJoinRequest:   - Sent 13/1 (Zone Ready)");
            Debug.Log($"[Game] HandleZoneJoinRequest:   - Sent 13/5 (Instance Count)");
            Debug.Log($"[Game] HandleZoneJoinRequest:   - Waited 400ms for client State 115");
            Debug.Log($"[Game] HandleZoneJoinRequest:   - Built spawn data directly (NO MessageQueue!)");
            Debug.Log($"[Game] HandleZoneJoinRequest:   - Sent A-lane spawn packet immediately (837 bytes)");
            Debug.Log($"[Game] HandleZoneJoinRequest:   - Created MessageQueue for future messages");
            Debug.Log($"[Game] HandleZoneJoinRequest:   - Started spawn complete timer (2000ms)");
            Debug.Log($"[Game] HandleZoneJoinRequest:   - Sent 13/8 (Final Ready)");
            Debug.Log($"[Game] HandleZoneJoinRequest: Player {conn.LoginName} should now be spawned in the world!");
            Debug.Log($"[Game] HandleZoneJoinRequest: ==================== EXIT ====================");
        }

        private byte[] BuildPlayerSpawnOperationsArray(RRConnection conn)
        {
            Debug.Log($"[Game] BuildPlayerSpawnOperationsArray: *** STARTING *** for {conn.LoginName}");
            Debug.Log($"[Game] BuildPlayerSpawnOperationsArray: This method builds spawn ops as byte array (NOT queueing)");

            if (!_selectedCharacter.TryGetValue(conn.LoginName, out var character))
            {
                Debug.LogError($"[Game] BuildPlayerSpawnOperationsArray: ERROR - No character found for {conn.LoginName}");
                return null;
            }

            var player = character;
            var avatar = player.Children.FirstOrDefault(c => c.NativeClass == "Avatar");
            if (avatar == null)
            {
                Debug.LogError($"[Game] BuildPlayerSpawnOperationsArray: ERROR - No avatar found for player {player.ID}");
                return null;
            }

            Debug.Log($"[Game] BuildPlayerSpawnOperationsArray: Found player ID={player.ID}, avatar ID={avatar.ID}");

            var writer = new LEWriter();
            int operationCount = 0;
            int totalBytes = 0;

            Debug.Log($"[Game] BuildPlayerSpawnOperationsArray: Starting to build operations...");
            Debug.Log($"[GO-SERVER-CORRECT-ORDER] *** MATCHING Go server WriteCreateNewPlayerEntity() EXACT order ***");

            int totalStartPos = writer.ToArray().Length;

            // OPERATION 1: Interval (PathManager Budget) - MUST BE FIRST!
            Debug.Log($"[INTERVAL-FIX] *** Building correct Interval operation ***");
            int intervalStart = writer.ToArray().Length;
            var opInterval = new LEWriter();
            opInterval.WriteByte(0x0D);
            opInterval.WriteInt32(1);
            opInterval.WriteInt32(100);
            opInterval.WriteInt32(0);
            opInterval.WriteInt32(0);
            opInterval.WriteUInt16(100);
            opInterval.WriteUInt16(20);
            opInterval.WriteByte(0x06);  // ✓ ADD THIS LINE
            writer.WriteBytes(opInterval.ToArray());
            operationCount++;
            totalBytes += opInterval.ToArray().Length;
            Debug.Log($"[Game] ⭐ Built operation {operationCount}: Interval (0x0D) PathManager Budget ({opInterval.ToArray().Length} bytes)");
            LogOperationSize("Interval", intervalStart, writer);

            // OPERATION 2: Create Avatar
            int avatarStart = writer.ToArray().Length;
            var opCreateAvatar = new LEWriter();
            opCreateAvatar.WriteByte(0x01);  // Create entity
            opCreateAvatar.WriteUInt16((ushort)avatar.ID);
            opCreateAvatar.WriteByte(0xFF);  // String marker
            opCreateAvatar.WriteCString(avatar.GCClass);
            writer.WriteBytes(opCreateAvatar.ToArray());
            operationCount++;
            totalBytes += opCreateAvatar.ToArray().Length;
            Debug.Log($"[Game] ⭐ Built operation {operationCount}: Create Avatar (ID={avatar.ID:X4}, {opCreateAvatar.ToArray().Length} bytes)");
            LogOperationSize("Create Avatar", avatarStart, writer);

            // OPERATION 3: Create Player
            int playerStart = writer.ToArray().Length;
            var opCreatePlayer = new LEWriter();
            opCreatePlayer.WriteByte(0x01);  // Create entity
            opCreatePlayer.WriteUInt16((ushort)player.ID);
            opCreatePlayer.WriteByte(0xFF);  // String marker
            opCreatePlayer.WriteCString(player.GCClass);
            writer.WriteBytes(opCreatePlayer.ToArray());
            operationCount++;
            totalBytes += opCreatePlayer.ToArray().Length;
            Debug.Log($"[Game] ⭐ Built operation {operationCount}: Create Player (ID={player.ID:X4}, {opCreatePlayer.ToArray().Length} bytes)");
            LogOperationSize("Create Player", playerStart, writer);

            // OPERATION 4: Init Player
            int initPlayerStart = writer.ToArray().Length;
            var opInitPlayer = new LEWriter();
            opInitPlayer.WriteByte(0x02);  // Init entity
            opInitPlayer.WriteUInt16((ushort)player.ID);
            opInitPlayer.WriteCString(player.Name);
            opInitPlayer.WriteUInt32(0);
            opInitPlayer.WriteUInt32(0);
            opInitPlayer.WriteByte(0xFF);
            opInitPlayer.WriteUInt32(1);
            writer.WriteBytes(opInitPlayer.ToArray());
            operationCount++;
            totalBytes += opInitPlayer.ToArray().Length;
            Debug.Log($"[Game] ⭐ Built operation {operationCount}: Init Player (ID={player.ID:X4}, {opInitPlayer.ToArray().Length} bytes)");
            LogOperationSize("Init Player", initPlayerStart, writer);

            // OPERATION 5-13: Init Components (Manipulators, Equipment, UnitContainer, etc.)
            var componentNames = new[] { "Manipulators", "Equipment", "UnitContainer", "Modifiers", "Skills", "UnitBehavior" };
            foreach (var compName in componentNames)
            {
                int componentStart = writer.ToArray().Length;
                var component = avatar.Children.FirstOrDefault(c => c.NativeClass == compName);
                if (component != null)
                {
                    var opInitComponent = new LEWriter();
                    opInitComponent.WriteByte(0x02);  // Init component
                    opInitComponent.WriteUInt16((ushort)component.ID);
                    // Add component-specific initialization data
                    opInitComponent.WriteUInt32(0); // Default init data
                    writer.WriteBytes(opInitComponent.ToArray());
                    operationCount++;
                    totalBytes += opInitComponent.ToArray().Length;
                    Debug.Log($"[Game] ⭐ Built operation {operationCount}: Init {compName} (ID={component.ID:X4}, {opInitComponent.ToArray().Length} bytes)");
                    LogOperationSize($"Init {compName}", componentStart, writer);
                }
            }

            // OPERATION 14: Init Avatar (must be last)
            int initAvatarStart = writer.ToArray().Length;
            var opInitAvatar = new LEWriter();
            opInitAvatar.WriteByte(0x02);  // Init entity
            opInitAvatar.WriteUInt16((ushort)avatar.ID);
            opInitAvatar.WriteByte(0x00);  // Face
            opInitAvatar.WriteByte(0x00);  // Hair
            opInitAvatar.WriteByte(0x00);  // HairColour
            writer.WriteBytes(opInitAvatar.ToArray());
            operationCount++;
            totalBytes += opInitAvatar.ToArray().Length;
            Debug.Log($"[Game] ⭐ Built operation {operationCount}: Init Avatar (ID={avatar.ID:X4}, OWNED, {opInitAvatar.ToArray().Length} bytes)");
            LogOperationSize("Init Avatar", initAvatarStart, writer);

            Debug.Log($"[Game] *** BuildPlayerSpawnOperationsArray COMPLETE ***");
            Debug.Log($"[Game] Total operations built: {operationCount}");
            Debug.Log($"[Game] Total bytes: {totalBytes}");
            return writer.ToArray();
        }

        private async Task HandleZoneConnected(RRConnection conn)
        {
            var w = new LEWriter();
            w.WriteByte(13);
            w.WriteByte(0);
            await SendCompressedAResponse(conn, 0x01, 0x0F, w.ToArray());
            Debug.Log($"[Game] Sent zone connected response");
        }

        private async Task HandleZoneReady(RRConnection conn)
        {
            var w = new LEWriter();
            w.WriteByte(13);
            w.WriteByte(8);
            await SendCompressedAResponse(conn, 0x01, 0x0F, w.ToArray());
            Debug.Log($"[Game] Sent zone ready response");
        }

        private async Task HandleZoneReadyResponse(RRConnection conn)
        {
            var w = new LEWriter();
            w.WriteByte(13);
            w.WriteByte(1);
            await SendCompressedAResponse(conn, 0x01, 0x0F, w.ToArray());
            Debug.Log($"[Game] Sent zone ready confirmation");
        }

        private async Task HandleZoneInstanceCount(RRConnection conn)
        {
            // Send interval message like Go server does
            await SendCE_Interval_A(conn);
        }

        private async Task HandleClientEntityChannelMessages(RRConnection conn, byte messageType, byte[] data)
        {
            Debug.Log($"[Game] HandleClientEntityChannelMessages: Type 0x{messageType:X2} for client {conn.ConnId}");

            switch (messageType)
            {
                case 4: // Client request respawn
                    Debug.Log($"[Game] Client requested respawn - sending spawn sequence");
                    
                    // Send the spawn data
                    // await SendPlayerEntitySpawnGO(conn);
                    
                    // CRITICAL: Send the "Now Connected" message with proper structure
                    await Task.Delay(100);
                    var connected = new LEWriter();
                    connected.WriteByte(0x07);  // Channel 7
                    connected.WriteByte(0x46);  // Message type 70 (0x46 hex)
                    connected.WriteByte(0x06);  // END OF STREAM MARKER - MUST BE HERE!
                    await SendCompressedAResponseWithDump(conn, 0x01, 0x0F, connected.ToArray(), "now_connected_7_46");
                    
                    Debug.Log($"[Game] Sent 385 connected message after spawn");
                    break;
                default:
                    Debug.LogWarning($"[Game] Unhandled ClientEntity message: 0x{messageType:X2}");
                    break;
            }
        }

        // ADD this helper method to your GameServer class:
        private void LogOperationSize(string operationName, int startPos, LEWriter writer)
        {
            int size = writer.ToArray().Length - startPos;
            Debug.Log($"[SPAWN-SIZE] {operationName}: {size} bytes");
        }

        private async Task SendCE_Interval_A(RRConnection conn)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);  // Channel 7
            writer.WriteByte(0x0D);  // Interval opcode
            writer.WriteInt32(1);
            writer.WriteInt32(100);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteUInt16(100);
            writer.WriteUInt16(20);
            writer.WriteByte(0x06);  // EndStream
            await SendCompressedAResponseWithDump(conn, writer.ToArray(), "interval_7_13");
        }

        // Helper methods for sending responses
        private async Task SendCompressedAResponse(RRConnection conn, byte dispatchType, byte dispatchSubType, byte[] data)
        {
            try
            {
                var response = new byte[data.Length + 2];
                response[0] = dispatchType;
                response[1] = dispatchSubType;
                Array.Copy(data, 0, response, 2, data.Length);
                
                var compressed = Zlib.Compress(response);
                await conn.Stream.WriteAsync(compressed, 0, compressed.Length);
                await conn.Stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendCompressedAResponse: Error sending response: {ex.Message}");
            }
        }

        private async Task SendCompressedAResponseWithDump(RRConnection conn, byte dispatchType, byte dispatchSubType, byte[] data, string tag)
        {
            Debug.Log($"[SEND-DUMP:{tag}] Sending {data.Length} bytes: {BitConverter.ToString(data.Take(50).ToArray())}");
            await SendCompressedAResponse(conn, dispatchType, dispatchSubType, data);
        }

        private async Task SendCompressedAResponseWithDump(RRConnection conn, byte[] data, string tag)
        {
            Debug.Log($"[SEND-DUMP:{tag}] Sending {data.Length} bytes: {BitConverter.ToString(data.Take(50).ToArray())}");
            await SendCompressedAResponse(conn, data[0], data[1], data.Skip(2).ToArray());
        }

        private async Task SendCompressedEResponseWithDump(RRConnection conn, byte[] data, string tag)
        {
            Debug.Log($"[SEND-DUMP:{tag}] Sending {data.Length} bytes: {BitConverter.ToString(data.Take(50).ToArray())}");
            
            try
            {
                var compressed = Zlib.Compress(data);
                await conn.Stream.WriteAsync(compressed, 0, compressed.Length);
                await conn.Stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendCompressedEResponseWithDump: Error sending response: {ex.Message}");
            }
        }

        private async Task SendMessage0x10(RRConnection conn, byte dispatchType, byte[] data)
        {
            try
            {
                var response = new byte[data.Length + 1];
                response[0] = dispatchType;
                Array.Copy(data, 0, response, 1, data.Length);
                
                var compressed = Zlib.Compress(response);
                await conn.Stream.WriteAsync(compressed, 0, compressed.Length);
                await conn.Stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Game] SendMessage0x10: Error sending response: {ex.Message}");
            }
        }

        // Entity ID management
        private void ReassignEntityIDs(GCObject obj)
        {
            if (obj.ID == 0)
                obj.ID = NewEntityID();

            foreach (var child in obj.Children)
            {
                ReassignEntityIDs(child);
            }
        }

        private uint NewEntityID()
        {
            return _nextEntityId++;
        }
    }
}