<old_str>           private async Task SendGoToZone(RRConnection conn, string zoneName)
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
           }</old_str><new_str>           private async Task SendGoToZone(RRConnection conn, string zoneName)
           {
               Debug.Log($"[Game] SendGoToZone: Starting zone transition to {zoneName} for {conn.LoginName}");
               
               try
               {
                   // Use the new ZoneManager for complete zone handling
                   await ZoneManager.EnterZone(conn, zoneName);
                   Debug.Log($"[Game] SendGoToZone: Zone transition completed for {conn.LoginName}");
               }
               catch (Exception ex)
               {
                   Debug.LogError($"[Game] SendGoToZone: Error during zone transition: {ex.Message}");
                   
                   // Fallback to original method with enhanced logging
                   Debug.Log($"[Game] SendGoToZone: Using fallback for {zoneName}");
                   
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
           }</new_str>