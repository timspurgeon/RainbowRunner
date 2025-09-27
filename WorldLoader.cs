using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Server.Common;
using UnityEngine;

namespace Server.Game
{
    public static class WorldLoader
    {
        public static async Task SendCompleteWorldLoad(RRConnection conn, string zoneName)
        {
            Debug.Log($"[WorldLoader] Starting complete world load for zone: {zoneName}");
            
            try
            {
                // Step 1: Send zone connection sequence
                await SendZoneConnectionSequence(conn, zoneName);
                
                // Step 2: Send world data
                await SendWorldData(conn, zoneName);
                
                // Step 3: Send spawn data
                await SendSpawnData(conn);
                
                // Step 4: Finalize world load
                await SendWorldReady(conn);
                
                Debug.Log($"[WorldLoader] World load completed for zone: {zoneName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldLoader] Error during world load: {ex.Message}");
                Debug.LogError(ex.StackTrace);
            }
        }

        private static async Task SendZoneConnectionSequence(RRConnection conn, string zoneName)
        {
            Debug.Log($"[WorldLoader] Sending zone connection sequence for {zoneName}");
            
            // Send ZoneConnected (13/0)
            var zoneConnected = new LEWriter();
            zoneConnected.WriteByte(13);
            zoneConnected.WriteByte(0);
            await GameServer.Instance.SendCompressedEResponse(conn, zoneConnected.ToArray());
            
            await Task.Delay(100);
            
            // Send GoToZone (13/48)
            var goToZone = new LEWriter();
            goToZone.WriteByte(13);
            goToZone.WriteByte(48);
            goToZone.WriteCString(zoneName);
            await GameServer.Instance.SendCompressedEResponse(conn, goToZone.ToArray());
            
            await Task.Delay(100);
            
            // Send JoinAck (13/1)
            var joinAck = new LEWriter();
            joinAck.WriteByte(13);
            joinAck.WriteByte(1);
            joinAck.WriteUInt32(1); // instance id
            joinAck.WriteUInt16(4); // visCount
            joinAck.WriteUInt32(1);
            joinAck.WriteUInt32(2);
            joinAck.WriteUInt32(3);
            joinAck.WriteUInt32(4);
            await GameServer.Instance.SendCompressedEResponse(conn, joinAck.ToArray());
            
            await Task.Delay(100);
        }

        private static async Task SendWorldData(RRConnection conn, string zoneName)
        {
            Debug.Log($"[WorldLoader] Sending world data for {zoneName}");
            
            // Create world object
            var world = new GCObject
            {
                ID = 1000,
                NativeClass = "World",
                GCClass = "World",
                Name = zoneName,
                Properties = new List<GCObjectProperty>
                {
                    new StringProperty { Name = "Name", Value = zoneName },
                    new UInt32Property { Name = "ID", Value = 1000 },
                    new UInt32Property { Name = "Type", Value = 1 }
                }
            };
            
            // Add zone data
            var zoneData = new GCObject
            {
                ID = 1001,
                NativeClass = "ZoneData",
                GCClass = "ZoneData",
                Name = $"{zoneName}Data",
                Properties = new List<GCObjectProperty>
                {
                    new StringProperty { Name = "ZoneName", Value = zoneName },
                    new UInt32Property { Name = "ZoneID", Value = 1001 },
                    new UInt32Property { Name = "MaxPlayers", Value = 100 },
                    new StringProperty { Name = "Terrain", Value = "Grass" }
                }
            };
            
            world.AddChild(zoneData);
            
            // Serialize and send
            var writer = new LEWriter();
            world.WriteFullGCObject(writer);
            var worldData = writer.ToArray();
            
            await GameServer.Instance.SendCompressedEResponse(conn, worldData);
            await Task.Delay(200);
        }

        private static async Task SendSpawnData(RRConnection conn)
        {
            Debug.Log("[WorldLoader] Sending spawn data");
            
            // Create spawn point
            var spawnPoint = new GCObject
            {
                ID = 2000,
                NativeClass = "SpawnPoint",
                GCClass = "SpawnPoint",
                Name = "PlayerSpawn",
                Properties = new List<GCObjectProperty>
                {
                    new StringProperty { Name = "Name", Value = "PlayerSpawn" },
                    new UInt32Property { Name = "ID", Value = 2000 },
                    new UInt32Property { Name = "X", Value = 100 },
                    new UInt32Property { Name = "Y", Value = 100 },
                    new UInt32Property { Name = "Z", Value = 0 }
                }
            };
            
            // Serialize and send
            var writer = new LEWriter();
            spawnPoint.WriteFullGCObject(writer);
            var spawnData = writer.ToArray();
            
            await GameServer.Instance.SendCompressedEResponse(conn, spawnData);
            await Task.Delay(100);
        }

        private static async Task SendWorldReady(RRConnection conn)
        {
            Debug.Log("[WorldLoader] Sending world ready signal");
            
            // Send ZoneReady (13/8)
            var zoneReady = new LEWriter();
            zoneReady.WriteByte(13);
            zoneReady.WriteByte(8);
            await GameServer.Instance.SendCompressedEResponse(conn, zoneReady.ToArray());
            await Task.Delay(100);
            
            // Send final world load complete
            var worldReady = new LEWriter();
            worldReady.WriteByte(13);
            worldReady.WriteByte(100); // Custom world ready
            worldReady.WriteCString("WorldLoadComplete");
            await GameServer.Instance.SendCompressedEResponse(conn, worldReady.ToArray());
        }

        public static async Task SendTestWorld(RRConnection conn)
        {
            Debug.Log("[WorldLoader] Sending test world for debugging");
            
            // Simple test world
            var testWorld = new GCObject
            {
                ID = 9999,
                NativeClass = "TestWorld",
                GCClass = "TestWorld",
                Name = "TestZone",
                Properties = new List<GCObjectProperty>
                {
                    new StringProperty { Name = "TestProperty", Value = "WorldLoadingFixed" }
                }
            };

            var writer = new LEWriter();
            testWorld.WriteFullGCObject(writer);
            var testData = writer.ToArray();

            await GameServer.Instance.SendCompressedEResponse(conn, testData);
        }
    }
}