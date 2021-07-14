package objects

import (
	"RainbowRunner/internal/logging"
	byter "RainbowRunner/pkg/byter"
	"fmt"
	"regexp"
	"strings"
)

//var currentID = uint32(0)

type EntityMessageHandler interface {
	WriteInit(b *byter.Byter)
	WriteUpdate(b *byter.Byter)
	WriteSynch(b *byter.Byter)

	ReadUpdate(reader *byter.Byter) error
}

type GCObject struct {
	EntityProperties RREntityProperties
	Version          uint8
	GCNativeType     string
	GCName           string
	children         []DRObject
	GCType           string
	Properties       []GCObjectProperty
	EntityHandler    EntityMessageHandler
}

func (g *GCObject) ReadUpdate(reader *byter.Byter) error {
	fmt.Printf("Unhandled readupdate for %s (%s : %s) ID: %x\n", g.GCName, g.GCType, g.GCNativeType, g.EntityProperties.ID)
	return nil
}

func (g *GCObject) WriteSynch(b *byter.Byter) {
	b.WriteByte(0x00)
}

func (g *GCObject) Tick() {

}

func (g *GCObject) GetGCObject() *GCObject {
	return g
}

func (g *GCObject) WriteInit(b *byter.Byter) {
	fmt.Printf("GCObject init for %s (%s: %s) not implemented but ignoring", g.GCName, g.GCType, g.GCNativeType)
}

func (g *GCObject) WriteUpdate(b *byter.Byter) {
	panic("implement me")
}

func (g *GCObject) Children() []DRObject {
	return g.children
}

func (g *GCObject) RREntityProperties() *RREntityProperties {
	return &g.EntityProperties
}

type GCObjectProperty struct {
	Name  interface{}
	Value interface{}
}

//func Uint8Prop(name string, val uint8) GCObjectProperty {
//	return GCObjectProperty{
//		Name:  name,
//		Value: val,
//	}
//}
//
//func Uint16Prop(name string, val uint16) GCObjectProperty {
//	return GCObjectProperty{
//		Name:  name,
//		Value: val,
//	}
//}

func Uint32Prop(name interface{}, val uint32) GCObjectProperty {
	return GCObjectProperty{
		Name:  name,
		Value: val,
	}
}

func StringProp(name interface{}, val string) GCObjectProperty {
	return GCObjectProperty{
		Name:  name,
		Value: val,
	}
}

//func NewID() (ID uint32) {
//	ID = currentID
//	currentID++
//	return ID
//}

func NewGCObject(nativeType string) *GCObject {
	return &GCObject{
		// At version 2A or above you must use a hash I think
		//Version:    0x29, // No hash required
		Version:      0x2D,
		GCNativeType: nativeType,
		GCType:       strings.ToLower(nativeType),
	}
}

var indent = 0

func (o *GCObject) WriteFullGCObject(byter *byter.Byter) {
	byter.WriteByte(o.Version)

	useHashes := o.Version >= 0x2a

	logSerialise("========== GCObject ===========")
	logSerialise(`ID: %d
Name: %s
NativeClass: %s
GCType: %s
---`, o.EntityProperties.ID, o.GCName, o.GCNativeType, o.GCType)

	if useHashes {
		byter.WriteUInt32(GetTypeHash(o.GCNativeType))
	} else {
		byter.WriteCString(o.GCNativeType)
	}

	byter.WriteUInt32(uint32(o.EntityProperties.ID))
	byter.WriteCString(o.GCName)

	byter.WriteUInt32(uint32(len(o.children)))

	indent++
	for _, child := range o.children {
		child.WriteFullGCObject(byter)
	}
	indent--

	if useHashes {
		byter.WriteUInt32(GetTypeHash(o.GCType))
	} else {
		byter.WriteCString(o.GCType)
	}

	for _, prop := range o.Properties {
		prop.Serialise(byter, useHashes)
	}

	byter.WriteUInt32(0)
}

func logSerialise(format string, args ...interface{}) {
	regex := regexp.MustCompile("(?m)^")

	if logging.LoggingOpts.LogGCObjectSerialise {
		indentString := strings.Repeat("\t", indent)
		log := fmt.Sprintf(format, args...)
		log = regex.ReplaceAllString(log, indentString)
		fmt.Print(log + "\n")
	}
}

func (o *GCObject) AddChild(child DRObject) {
	if o.children == nil {
		o.children = make([]DRObject, 0, 128)
	}

	o.children = append(o.children, child)
}

func (p GCObjectProperty) Serialise(b *byter.Byter, useHash bool) {
	switch name := p.Name.(type) {
	case string:
		if useHash {
			b.WriteUInt32(GetTypeHash(name))
		} else {
			b.WriteCString(name)
		}
	case int:
		b.WriteUInt32(uint32(name))
	case uint32:
		b.WriteUInt32(name)
	}

	switch p.Value.(type) {
	case string:
		b.WriteCString(p.Value.(string))
	case uint32:
		b.WriteUInt32(p.Value.(uint32))
	case uint8:
		b.WriteByte(p.Value.(uint8))
	case uint16:
		b.WriteUInt16(p.Value.(uint16))
	}
}

func GetTypeHash(name string) uint32 {
	result := uint32(5381) // eax

	a1 := len(name)

	if a1 > 0 {
		for _, v4 := range name {
			if v4 >= 0x41 && v4 <= 0x5A {
				v4 = v4 + 32
			}

			result += uint32(v4) + 32*result
		}

		if result == 0 {
			result = 1
		}
	}

	if logging.LoggingOpts.LogHashes {
		fmt.Printf("(%x) %s\n", result, name)
	}

	return result
}

func (p *GCObject) GetChildByGCNativeType(s string) DRObject {
	for _, child := range p.children {
		if strings.ToLower(child.GetGCObject().GCNativeType) == strings.ToLower(s) {
			return child
		}
	}

	return nil
}

func (p *GCObject) GetChildByGCType(s string) DRObject {
	for _, child := range p.children {
		if strings.ToLower(child.GetGCObject().GCType) == strings.ToLower(s) {
			return child
		}
	}

	return nil
}
