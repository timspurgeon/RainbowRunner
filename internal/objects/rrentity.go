package objects

import (
	"RainbowRunner/pkg/byter"
)

type RREntityProperties struct {
	OwnerID int
	ID      uint16
}

type RREntity struct {
	Object  DRObject
	OwnerID int
	ID      uint16
}

func (R *RREntity) WriteFullGCObject(b *byter.Byter) {
	panic("implement me")
}

func (R *RREntity) WriteInit(b *byter.Byter) {
	panic("implement me")
}

func (R *RREntity) WriteUpdate(b *byter.Byter) {
	panic("implement me")
}

func (R *RREntity) AddChild(avatar *RREntity) {

}
