# RainbowRunner Crash Analysis

## Problem Identified

The server is crashing with the error: `could not parse slot type for` followed by an empty string.

## Root Cause Analysis

### The Issue
The code in `internal/types/drconfigtypes/drclass.go` at line 60 is trying to parse `SlotType` from a description entity:

```go
slotInt, err := strconv.Atoi(desc.Entities[0].Properties["SlotType"])
```

### What's Happening
1. The `Slot()` method is being called on equipment classes (like `ChainBoots3`)
2. It finds the "description" child correctly 
3. But the description's `Entities[0].Properties` dictionary is EMPTY - it has no "SlotType" key
4. This causes `strconv.Atoi("")` to fail because the key doesn't exist
5. The error returns `could not parse slot type for [empty name]`

### JSON Structure Investigation
Looking at `resources/Dumps/DRResourceConfigDump.json`, the structure is:

```
ChainPAL -> ChainBoots3 -> Chain -> BaseBoots -> BaseBoots -> Armor
ChainPAL -> ChainBoots3 -> Chain -> BaseBoots -> Description -> ArmorDesc
```

**The problem**: The `ArmorDesc` dictionary is completely empty - it has no entities with properties.

### EquipmentSlot Constants
From `internal/types/types.go`, the valid slot types are:
- EquipmentSlotNone = 0
- EquipmentSlotAmulet = 1  
- EquipmentSlotHand = 2
- EquipmentSlotLRing = 3
- EquipmentSlotRRing = 4
- EquipmentSlotHead = 5
- EquipmentSlotTorso = 6
- EquipmentSlotFoot = 7  (BOOTS)
- EquipmentSlotShoulder = 8
- EquipmentSlotNone2 = 9
- EquipmentSlotWeapon = 10
- EquipmentSlotOffhand = 11

## The Fix

The equipment descriptions are missing the SlotType property. For boots, this should be "7" (EquipmentSlotFoot).

### Two Solutions:

#### 1. Fix the Data (Recommended)
Add SlotType properties to all the empty ArmorDesc entities in the JSON dump or configuration files.

#### 2. Fix the Code (Fallback)
Add a fallback mechanism to infer slot type from class name when SlotType is missing.

## Where This Is Called
The crash happens in:
- `internal/database/database.go:193` in `AddArmours()` function
- `internal/objects/entity_player.go:139` in `AddRandomEquipment()` function

Both call `subType.Slot()` or `class.Slot()` which triggers the parsing error.