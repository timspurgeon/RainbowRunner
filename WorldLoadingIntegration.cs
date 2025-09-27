using System;
using UnityEngine;

namespace Server.Game
{
    /// <summary>
    /// Integration helper for world loading fixes
    /// </summary>
    public static class WorldLoadingIntegration
    {
        public static void Initialize()
        {
            Debug.Log("[WorldLoadingIntegration] Initializing world loading fixes...");
            
            // Log available zones
            var zones = ZoneManager.GetAvailableZones();
            Debug.Log($"[WorldLoadingIntegration] Available zones: {string.Join(", ", zones)}");
            
            Debug.Log("[WorldLoadingIntegration] World loading integration ready!");
        }

        public static async Task TestWorldLoading(RRConnection conn)
        {
            Debug.Log("[WorldLoadingIntegration] Testing world loading...");
            await WorldLoader.SendTestWorld(conn);
        }
    }
}