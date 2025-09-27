using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Server.Common;
using UnityEngine;

namespace Server.Game
{
    public static class ZoneManager
    {
        private static readonly Dictionary<string, ZoneInfo> Zones = new Dictionary<string, ZoneInfo>
        {
            { "Town", new ZoneInfo { ID = 1, Name = "Town", MaxPlayers = 100, Terrain = "Grass" } },
            { "World", new ZoneInfo { ID = 2, Name = "World", MaxPlayers = 200, Terrain = "Mixed" } },
            { "Dungeon", new ZoneInfo { ID = 3, Name = "Dungeon", MaxPlayers = 50, Terrain = "Stone" } },
            { "Forest", new ZoneInfo { ID = 4, Name = "Forest", MaxPlayers = 150, Terrain = "Forest" } }
        };

        public static async Task EnterZone(RRConnection conn, string zoneName)
        {
            if (!Zones.TryGetValue(zoneName, out var zone))
            {
                Debug.LogError($"[ZoneManager] Unknown zone: {zoneName}");
                return;
            }

            Debug.Log($"[ZoneManager] Player {conn.LoginName} entering zone: {zoneName}");

            try
            {
                // Send zone entry sequence
                await SendZoneEntry(conn, zone);
                
                // Send zone data
                await SendZoneData(conn, zone);
                
                // Send player spawn
                await SendPlayerSpawn(conn, zone);
                
                // Finalize zone entry
                await FinalizeZoneEntry(conn, zone);
                
                Debug.Log($"[ZoneManager] Zone entry completed for {conn.LoginName} in {zoneName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ZoneManager] Error during zone entry: {ex.Message}");
            }
        }

        private static async Task SendZoneEntry(RRConnection conn, ZoneInfo zone)
        {
            Debug.Log($"[ZoneManager] Sending zone entry sequence for {zone.Name}");

            // Zone connected
            var zoneConnected = new LEWriter();
            zoneConnected.WriteByte(13);
            zoneConnected.WriteByte(0);
            await GameServer.Instance.SendCompressedEResponse(conn, zoneConnected.ToArray());
            await Task.Delay(50);

            // Go to zone
            var goToZone = new LEWriter();
            goToZone.WriteByte(13);
            goToZone.WriteByte(48);
            goToZone.WriteCString(zone.Name);
            await GameServer.Instance.SendCompressedEResponse(conn, goToZone.ToArray());
            await Task.Delay(50);

            // Join acknowledgment
            var joinAck = new LEWriter();
            joinAck.WriteByte(13);
            joinAck.WriteByte(1);
            joinAck.WriteUInt32(1); // instance ID
            joinAck.WriteUInt16(4); // visibility count
            joinAck.WriteUInt32(1);
            joinAck.WriteUInt32(2);
            joinAck.WriteUInt32(3);
            joinAck.WriteUInt32(4);
            await GameServer.Instance.SendCompressedEResponse(conn, joinAck.ToArray());
            await Task.Delay(50);
        }

        private static async Task SendZoneData(RRConnection conn, ZoneInfo zone)
        {
            Debug.Log($"[ZoneManager] Sending zone data for {zone.Name}");

            // Create zone object
            var zoneObj = new GCObject
            {
                ID = (uint)zone.ID,
                NativeClass = "Zone",
                GCClass = "Zone",
                Name = zone.Name,
                Properties = new List<GCObjectProperty>
                {
                    new StringProperty { Name = "Name", Value = zone.Name },
                    new UInt32Property { Name = "ID", Value = (uint)zone.ID },
                    new UInt32Property { Name = "MaxPlayers", Value = (uint)zone.MaxPlayers },
                    new StringProperty { Name = "Terrain", Value = zone.Terrain }
                }
            };

            // Add terrain data
            var terrain = new GCObject
            {
                ID = (uint)(zone.ID + 1000),
                NativeClass = "Terrain",
                GCClass = "Terrain",
                Name = $"{zone.Name}Terrain",
                Properties = new List<GCObjectProperty>
                {
                    new StringProperty { Name = "Type", Value = zone.Terrain },
                    new UInt32Property { Name = "Width", Value = 1000 },
                    new UInt32Property { Name = "Height", Value = 1000 }
                }
            };

            zoneObj.AddChild(terrain);

            // Serialize and send
            var writer = new LEWriter();
            zoneObj.WriteFullGCObject(writer);
            var zoneData = writer.ToArray();

            await GameServer.Instance.SendCompressedEResponse(conn, zoneData);
            await Task.Delay(100);
        }

        private static async Task SendPlayerSpawn(RRConnection conn, ZoneInfo zone)
        {
            Debug.Log($"[ZoneManager] Sending player spawn for {conn.LoginName} in {zone.Name}");

            // Get player's character
            if (!GameServer.Instance._playerCharacters.TryGetValue(conn.ConnId, out var chars) || chars.Count == 0)
            {
                Debug.LogError($"[ZoneManager] No character found for {conn.LoginName}");
                return;
            }

            var character = chars[0];

            // Create spawn data
            var spawnData = new GCObject
            {
                ID = 2000,
                NativeClass = "PlayerSpawn",
                GCClass = "PlayerSpawn",
                Name = $"{conn.LoginName}Spawn",
                Properties = new List<GCObjectProperty>
                {
                    new StringProperty { Name = "PlayerName", Value = conn.LoginName },
                    new UInt32Property { Name = "CharacterID", Value = character.ID },
                    new UInt32Property { Name = "X", Value = 500 },
                    new UInt32Property { Name = "Y", Value = 500 },
                    new UInt32Property { Name = "Z", Value = 0 }
                }
            };

            // Add character to spawn
            spawnData.AddChild(character);

            // Serialize and send
            var writer = new LEWriter();
            spawnData.WriteFullGCObject(writer);
            var spawnBytes = writer.ToArray();

            await GameServer.Instance.SendCompressedEResponse(conn, spawnBytes);
            await Task.Delay(100);
        }

        private static async Task FinalizeZoneEntry(RRConnection conn, ZoneInfo zone)
        {
            Debug.Log($"[ZoneManager] Finalizing zone entry for {conn.LoginName}");

            // Send zone ready
            var zoneReady = new LEWriter();
            zoneReady.WriteByte(13);
            zoneReady.WriteByte(8);
            await GameServer.Instance.SendCompressedEResponse(conn, zoneReady.ToArray());
            await Task.Delay(50);

            // Send world ready confirmation
            var worldReady = new LEWriter();
            worldReady.WriteByte(13);
            worldReady.WriteByte(100);
            worldReady.WriteCString("ZoneLoadComplete");
            await GameServer.Instance.SendCompressedEResponse(conn, worldReady.ToArray());
            await Task.Delay(50);
        }

        public static List<string> GetAvailableZones()
        {
            return new List<string>(Zones.Keys);
        }

        private class ZoneInfo
        {
            public int ID { get; set; }
            public string Name { get; set; }
            public int MaxPlayers { get; set; }
            public string Terrain { get; set; }
        }
    }
}