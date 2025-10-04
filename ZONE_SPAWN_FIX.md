# Zone Spawn Fix - Remove Zone/2 Message

## Problem
The Unity server is sending Zone/2 (zone_spawn_enter_world) with Avatar data, but this message doesn't exist in the Dungeon Runners protocol. The GO server never sends Zone/2 - it only defines:
- Zone/0 = Connected
- Zone/1 = Ready
- Zone/2 = Disconnected (not used for spawning!)
- Zone/5 = InstanceCount

## Root Cause
The Unity server sends messages in this order:
1. Zone/0 (Connect)
2. **Zone/2 (Spawn with Avatar)** ← WRONG! This message doesn't exist
3. Client sends Zone/6 (Join request)
4. Zone/1 (Ready)
5. Zone/5 (Instance Count)

The GO server sends:
1. Zone/0 (Connect)
2. Client sends Zone/6 (Join request)
3. Zone/1 (Ready)
4. Zone/5 (Instance Count)
5. **A-lane entity spawn** (lane 0x0e, not Zone channel 0x0d!)

## Solution
Remove the Zone/2 message completely from SendGoToZone_V2. The entity spawn is already being sent correctly in HandleZoneJoin via SendPlayerEntitySpawn.

## Changes Required

### File: GameServer.cs

**Remove lines 1263-1287** (the entire Zone/2 block):
```csharp
// Step 4: Send Zone/2 with Avatar DFC object - CRITICAL for spawning!
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
else
{
    Debug.LogWarning($"[Game] SendGoToZone: No selected character found for {conn.LoginName}");
}
```

**Replace with:**
```csharp
// Entity spawn will be sent when client sends Zone/6 (Join request)
// HandleZoneJoin will send Zone/1, Zone/5, and entity spawn on A-lane
```

## Expected Result
After this fix:
1. Client receives Zone/0 (Connect)
2. Client loads the zone
3. Client sends Zone/6 (Join request)
4. Server sends Zone/1 (Ready) + Zone/5 (Instance Count)
5. Server sends entity spawn on **A-lane** (already working in HandleZoneJoin)
6. Client spawns into the world ✅

## Testing
1. Start server
2. Connect with client
3. Select character
4. Client should spawn into Town and be visible
5. Check logs - should NOT see "Sent Zone/2 with Avatar data"
6. Check logs - should see "HandleZoneJoin: Sent Entity Spawn"