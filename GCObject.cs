// GCObject.cs - Complete Fix - Version 2.0 - October 16, 2025
// This version includes all required components for proper character creation and world entry
// with correct DFC serialization format matching the Go server implementation

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Server.Common;
using UnityEngine;

namespace Server.Game
{
    // DFC Property Types
    public enum DFCPropertyType : byte
    {
        String = 1,
        UInt32 = 2,
        Int32 = 3,
        Float = 4,
        Boolean = 5,
        Vector3 = 6,
        UInt16 = 7,
        Int16 = 8,
        Byte = 9,
        SByte = 10,
        Double = 11,
        Vector2 = 12
    }

    public class GCObject
    {
        public static readonly byte DFC_VERSION = 0x2D; // Version 45 - CRITICAL for Python compatibility
                                                         // public MessageQueue MessageQueue { get; set; }
        public uint ID { get; set; }
        public string Name { get; set; } = "";
        public string NativeClass { get; set; } = "";
        public string GCClass { get; set; } = "";
        public List<GCObjectProperty> Properties { get; set; } = new List<GCObjectProperty>();
        public List<GCObject> Children { get; set; } = new List<GCObject>();
        public byte[] ExtraData { get; set; } = new byte[0]; // For components that need additional data

        public void AddChild(GCObject child)
        {
            Children.Add(child);
        }

        // Python DFC format serialization
        public void WriteFullGCObject(LEWriter writer)
        {
            Debug.Log($"[GCObject] Writing DFC object: ID={ID}, NativeClass='{NativeClass}', GCClass='{GCClass}', Props={Properties.Count}, Children={Children.Count}");

            // DFC version byte (0x2D = 45 decimal) - CRITICAL for Python compatibility
            writer.WriteByte(DFC_VERSION);

            // djb2 hash of native class name
            uint nativeHash = HashDjb2(NativeClass);
            writer.WriteUInt32(nativeHash);
            Debug.Log($"[GCObject] NativeClass hash: '{NativeClass}' -> 0x{nativeHash:X8}");

            // Node ID (uint32 little-endian)
            writer.WriteUInt32(ID);

            // Node name (null-terminated string)
            writer.WriteCString(Name);

            // Number of child nodes
            writer.WriteUInt32((uint)Children.Count);

            // Serialize child nodes recursively
            foreach (var child in Children)
            {
                child.WriteFullGCObject(writer);
            }

            // djb2 hash of GC class name
            uint gcHash = HashDjb2(GCClass);
            writer.WriteUInt32(gcHash);
            Debug.Log($"[GCObject] GCClass hash: '{GCClass}' -> 0x{gcHash:X8}");

            // Write properties with djb2 hashed names
            foreach (var prop in Properties)
            {
                prop.WriteDFC(writer);
            }

            // End object marker (4 null bytes)
            writer.WriteUInt32(0);
        }

        // djb2 hash function - EXACT MATCH to Python implementation
        public static uint HashDjb2(string str)
        {
            if (string.IsNullOrEmpty(str))
                return 0;

            uint hash = 5381;
            foreach (char c in str)
            {
                // Convert to lowercase like Python does
                char lowerC = char.ToLower(c);
                hash = ((hash << 5) + hash) + lowerC;
            }

            // Ensure non-zero result like Python does
            if (hash == 0)
                hash = 1;

            return hash;
        }

        // Helper to write component with extra data like Python does
        public void WriteComponentWithExtraData(LEWriter writer)
        {
            Debug.Log($"[Component] Writing component with extra data: {ExtraData.Length} bytes");

            // Write object header
            writer.WriteUInt32(ID);
            writer.WriteCString(GCClass);
            writer.WriteCString(Name);

            // Write properties count
            writer.WriteByte((byte)Properties.Count);

            // Write each property
            foreach (var prop in Properties)
            {
                prop.WriteDFC(writer);
            }

            // Write extra data if any
            if (ExtraData.Length > 0)
            {
                writer.WriteBytes(ExtraData);
            }

            // Write children count
            writer.WriteByte((byte)Children.Count);

            // Write each child
            foreach (var child in Children)
            {
                child.WriteFullGCObject(writer);
            }
        }
    }

    public abstract class GCObjectProperty
    {
        public string Name { get; set; } = "";
        public abstract void WriteDFC(LEWriter writer);
    }

    public class StringProperty : GCObjectProperty
    {
        public string Value { get; set; } = "";

        public override void WriteDFC(LEWriter writer)
        {
            // Write property name with djb2 hash
            uint nameHash = GCObject.HashDjb2(Name);
            writer.WriteUInt32(nameHash);

            // Write property type
            writer.WriteByte((byte)DFCPropertyType.String);

            // Write string value (null-terminated)
            writer.WriteCString(Value);
        }
    }

    public class UInt32Property : GCObjectProperty
    {
        public uint Value { get; set; }

        public override void WriteDFC(LEWriter writer)
        {
            // Write property name with djb2 hash
            uint nameHash = GCObject.HashDjb2(Name);
            writer.WriteUInt32(nameHash);

            // Write property type
            writer.WriteByte((byte)DFCPropertyType.UInt32);

            // Write uint32 value
            writer.WriteUInt32(Value);
        }
    }

    public class Int32Property : GCObjectProperty
    {
        public int Value { get; set; }

        public override void WriteDFC(LEWriter writer)
        {
            // Write property name with djb2 hash
            uint nameHash = GCObject.HashDjb2(Name);
            writer.WriteUInt32(nameHash);

            // Write property type
            writer.WriteByte((byte)DFCPropertyType.Int32);

            // Write int32 value
            writer.WriteInt32(Value);
        }
    }

    // Factory methods based on your Go code
    public static class Objects
    {
        private static uint nextId = 10; // Start from 10 to match Python

        public static uint NewID() => nextId++;

        // Create a complete player with avatar and all required components
        public static GCObject NewPlayer(string name)
        {
            Debug.Log($"[Objects] Creating player '{name}' with complete avatar");

            var player = new GCObject
            {
                ID = NewID(),
                NativeClass = "Player",
                GCClass = "Player",
                Name = name,
                Properties = new List<GCObjectProperty>
                {
                    new StringProperty { Name = "Name", Value = name },
                    new UInt32Property { Name = "Level", Value = 50 },
                    new UInt32Property { Name = "Experience", Value = 1000000 },
                    new UInt32Property { Name = "Health", Value = 1000 },
                    new UInt32Property { Name = "MaxHealth", Value = 1000 },
                    new UInt32Property { Name = "Mana", Value = 500 },
                    new UInt32Property { Name = "MaxMana", Value = 500 }
                }
            };

            // Add avatar with complete children tree
            player.AddChild(NewAvatar());

            // Add QuestManager (critical for client)
            Debug.Log($"[Objects] Adding QuestManager to player");
            player.AddChild(NewQuestManager());

            // Add DialogManager (critical for client)
            Debug.Log($"[Objects] Adding DialogManager to player");
            player.AddChild(NewDialogManager());

            Debug.Log($"[Objects] Created complete player with {player.Children.Count} children");
            return player;
        }

        // Create a complete avatar with all required components in correct order
        public static GCObject NewAvatar()
        {
            Debug.Log($"[Objects] Creating avatar with COMPLETE children tree matching Python");

            var avatar = new GCObject
            {
                ID = NewID(),
                NativeClass = "Avatar",
                GCClass = "avatar.classes.FighterFemale",
                Name = "avatar",
                Properties = new List<GCObjectProperty>
                {
                    new UInt32Property { Name = "Skin", Value = 0 },
                    new UInt32Property { Name = "Face", Value = 0 },
                    new UInt32Property { Name = "FaceFeature", Value = 0 },
                    new UInt32Property { Name = "Hair", Value = 0 },
                    new UInt32Property { Name = "HairColor", Value = 0 },
                    new UInt32Property { Name = "TotalWorldTime", Value = 10 },
                    new UInt32Property { Name = "LastKnownQueueLevel", Value = 0 },
                }
            };

            // Add children in EXACT order that Python does (critical!)
            Debug.Log($"[Objects] Adding UnitBehavior");
            avatar.AddChild(NewUnitBehavior());

            Debug.Log($"[Objects] Adding Manipulators");
            avatar.AddChild(NewManipulators());

            Debug.Log($"[Objects] Adding Equipment");
            avatar.AddChild(NewEquipment());

            Debug.Log($"[Objects] Adding UnitContainer with 7 children");
            avatar.AddChild(NewUnitContainerWithSevenChildren());

            Debug.Log($"[Objects] Adding AvatarMetrics");
            avatar.AddChild(NewAvatarMetrics());

            Debug.Log($"[Objects] Adding DialogManager");
            avatar.AddChild(NewDialogManager());

            Debug.Log($"[Objects] Adding QuestManager");
            avatar.AddChild(NewQuestManager());

            Debug.Log($"[Objects] Adding Skills");
            avatar.AddChild(NewSkills());

            Debug.Log($"[Objects] Adding Modifiers");
            avatar.AddChild(NewModifiers());

            Debug.Log($"[Objects] Created avatar with {avatar.Children.Count} children in Python-matching order");
            return avatar;
        }

        public static GCObject NewUnitBehavior()
        {
            return new GCObject
            {
                ID = NewID(),
                NativeClass = "UnitBehavior",
                GCClass = "avatar.base.UnitBehavior",
                Name = null
            };
        }

        public static GCObject NewManipulators()
        {
            return new GCObject
            {
                ID = NewID(),
                NativeClass = "Manipulators",
                GCClass = "Manipulators",
                Name = null
            };
        }

        public static GCObject NewEquipment()
        {
            return new GCObject
            {
                ID = NewID(),
                NativeClass = "Equipment",
                GCClass = "avatar.base.Equipment",
                Name = null
            };
        }

        // Create UnitContainer with 7 children (matching Python exactly)
        public static GCObject NewUnitContainerWithSevenChildren()
        {
            Debug.Log($"[Objects] Creating UnitContainer with 7 children (like Python does)");

            var unitContainer = new GCObject
            {
                ID = NewID(),
                NativeClass = "UnitContainer",
                GCClass = "UnitContainer",
                Name = null
            };

            // Add 7 children like Python does
            for (int i = 0; i < 7; i++)
            {
                var child = new GCObject
                {
                    ID = NewID(),
                    NativeClass = $"Child{i}",
                    GCClass = $"Child{i}",
                    Name = null
                };
                unitContainer.AddChild(child);
            }

            // Add extraData like Python does
            var extraData = new List<byte>();
            // PlayTime (5 sets of 4 zeros)
            for (int i = 0; i < 5; i++)
                extraData.AddRange(BitConverter.GetBytes((uint)0));

            // ZoneToPlayTimeMap
            extraData.AddRange(BitConverter.GetBytes((uint)0));

            // TotalPlayTime
            extraData.AddRange(BitConverter.GetBytes((uint)0));

            unitContainer.ExtraData = extraData.ToArray();

            Debug.Log($"[Objects][UnitContainer] ChildCount(final)={unitContainer.Children.Count}, ExtraData={unitContainer.ExtraData.Length} bytes");
            return unitContainer;
        }

        public static GCObject NewAvatarMetrics()
        {
            var avatarMetrics = new GCObject
            {
                ID = NewID(),
                NativeClass = "AvatarMetrics",
                GCClass = "AvatarMetrics",
                Name = null
            };

            // Add extraData like Python does
            var extraData = new List<byte>();
            // PlayTime (5 sets of 4 zeros)
            for (int i = 0; i < 5; i++)
                extraData.AddRange(BitConverter.GetBytes((uint)0));

            // ZoneToPlayTimeMap
            extraData.AddRange(BitConverter.GetBytes((uint)0));

            // TotalPlayTime
            extraData.AddRange(BitConverter.GetBytes((uint)0));

            avatarMetrics.ExtraData = extraData.ToArray();

            return avatarMetrics;
        }

        public static GCObject NewDialogManager()
        {
            return new GCObject
            {
                ID = NewID(),
                NativeClass = "DialogManager",
                GCClass = "DialogManager",
                Name = null,
                Properties = new List<GCObjectProperty>
                {
                    new UInt32Property { Name = "Unk1", Value = 1 },
                    new StringProperty { Name = "Unk2", Value = "Hello" },
                    new StringProperty { Name = "Unk3", Value = "HelloAgain" },
                    new UInt32Property { Name = "Unk4", Value = 1 },
                    new UInt32Property { Name = "Unk5", Value = 1 }
                }
            };
        }

        // ⭐ ADDED: QuestManager factory method (was missing!)
        public static GCObject NewQuestManager()
        {
            Debug.Log($"[Objects] Creating QuestManager component");

            var questManager = new GCObject
            {
                ID = NewID(),
                NativeClass = "QuestManager",
                GCClass = "QuestManager",
                Name = null,
                Properties = new List<GCObjectProperty>()
            };

            // Add the special "SomethingUnknown" string that Python sends
            questManager.ExtraData = Encoding.UTF8.GetBytes("SomethingUnknown\0");

            Debug.Log($"[Objects] Created QuestManager with {questManager.ExtraData.Length} extra bytes");
            return questManager;
        }

        public static GCObject NewSkills()
        {
            var skills = new GCObject
            {
                ID = NewID(),
                NativeClass = "Skills",
                GCClass = "avatar.base.skills",
                Name = null
            };

            // Add some default skills like Python does
            var skill1 = new GCObject
            {
                ID = NewID(),
                NativeClass = "ActiveSkill",
                GCClass = "skills.generic.Stomp",
                Name = "skills.generic.Stomp"
            };
            skills.AddChild(skill1);

            var skill2 = new GCObject
            {
                ID = NewID(),
                NativeClass = "ActiveSkill",
                GCClass = "skills.generic.Sprint",
                Name = "skills.generic.Sprint"
            };
            skills.AddChild(skill2);

            return skills;
        }

        public static GCObject NewModifiers()
        {
            return new GCObject
            {
                ID = NewID(),
                NativeClass = "Modifiers",
                GCClass = "Modifiers",
                Name = null,
                Properties = new List<GCObjectProperty>
                {
                    new UInt32Property { Name = "IDGenerator", Value = 1 }
                }
            };
        }

        // Create a minimal character object that represents "create new character"
        public static GCObject NewCharacter(string name)
        {
            Debug.Log($"[Objects] Creating minimal character '{name}'");

            return new GCObject
            {
                ID = NewID(),
                NativeClass = "Character",
                GCClass = "Character",
                Name = name,
                Properties = new List<GCObjectProperty>
                {
                    new StringProperty { Name = "Name", Value = name },
                    new UInt32Property { Name = "Level", Value = 1 },
                    new UInt32Property { Name = "Experience", Value = 0 }
                }
            };
        }
    }
}