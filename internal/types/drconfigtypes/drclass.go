package drconfigtypes

import (
	"RainbowRunner/internal/types"
	"errors"
	"fmt"
	"regexp"
	"strconv"
	"strings"
)

type DRClass struct {
	Name             string                        `json:"name,omitempty"`
	GCType           string                        `json:"gcType,omitempty"`
	Extends          string                        `json:"extends,omitempty"`
	Properties       DRClassProperties             `json:"properties,omitempty"`
	Children         map[string]*DRClassChildGroup `json:"children,omitempty"`
	CustomProperties map[string]interface{}        `json:"customProperties,omitempty"`
}

func (c *DRClass) Find(class []string) *DRClass {
	for childName, child := range c.Children {
		if childName == class[0] {
			if len(class) > 1 {
				return c.Find(class[1:])
			} else {
				return child.Entities[0]
			}
		}
	}

	return nil
}

var modRegexp = regexp.MustCompile("^Mod[0-9]+$")

func (c *DRClass) ModCount() int {
	modCount := 0

	for childName, _ := range c.Children {
		//if modRegexp.MatchString(child.Name) {
		//	modCount++
		//}

		if childName != "description" {
			modCount++
		}
	}

	return modCount
}

func (c *DRClass) Slot() (types.EquipmentSlot, error) {
	desc, ok := c.Children["description"]

	// Mods do not have descriptions
	if !ok {
		panic(fmt.Sprintf("%s does not have a description", c.Name))
	}

	// Check if description has entities with properties
	if len(desc.Entities) == 0 {
		return 0, errors.New(fmt.Sprintf("description has no entities for %s", c.Name))
	}

	entity := desc.Entities[0]
	if entity.Properties == nil {
		return 0, errors.New(fmt.Sprintf("description entity has no properties for %s", c.Name))
	}

	slotType, hasSlotType := entity.Properties["SlotType"]
	
	if !hasSlotType {
		// Fallback: infer slot type from class name
		slot := c.inferSlotTypeFromName()
		if slot != types.EquipmentSlotNone {
			return slot, nil
		}
		return 0, errors.New(fmt.Sprintf("could not find SlotType property for %s", c.Name))
	}

	// Check if slotType is empty
	if slotType == "" {
		// Fallback: infer slot type from class name when SlotType is empty
		slot := c.inferSlotTypeFromName()
		if slot != types.EquipmentSlotNone {
			return slot, nil
		}
		return 0, errors.New(fmt.Sprintf("SlotType property is empty for %s", c.Name))
	}

	slotInt, err := strconv.Atoi(slotType)

	if err != nil {
		return 0, errors.New(fmt.Sprintf("could not parse slot type '%s' for %s", slotType, c.Name))
	}

	return types.EquipmentSlot(slotInt), nil
}

func (c *DRClass) inferSlotTypeFromName() types.EquipmentSlot {
	name := strings.ToLower(c.Name)
	
	switch {
	case strings.Contains(name, "boots"):
		return types.EquipmentSlotFoot
	case strings.Contains(name, "helm") || strings.Contains(name, "helmet"):
		return types.EquipmentSlotHead
	case strings.Contains(name, "armor") || strings.Contains(name, "chest") || strings.Contains(name, "torso"):
		return types.EquipmentSlotTorso
	case strings.Contains(name, "gloves") || strings.Contains(name, "gauntlets"):
		return types.EquipmentSlotHand
	case strings.Contains(name, "shoulders") || strings.Contains(name, "pauldrons"):
		return types.EquipmentSlotShoulder
	case strings.Contains(name, "shield"):
		return types.EquipmentSlotOffhand
	case strings.Contains(name, "axe") || strings.Contains(name, "sword") || strings.Contains(name, "weapon"):
		return types.EquipmentSlotWeapon
	case strings.Contains(name, "amulet") || strings.Contains(name, "necklace"):
		return types.EquipmentSlotAmulet
	case strings.Contains(name, "ring"):
		return types.EquipmentSlotLRing // Default to left ring
	default:
		return types.EquipmentSlotNone
	}
}