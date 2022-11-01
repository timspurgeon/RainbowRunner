package objects

import (
	"RainbowRunner/internal/database"
	"RainbowRunner/internal/helpers"
	"RainbowRunner/internal/lua"
	"RainbowRunner/pkg/byter"
	"RainbowRunner/pkg/datatypes"
	log "github.com/sirupsen/logrus"
	lua2 "github.com/yuin/gopher-lua"
	"sync"
)

type Zone struct {
	sync.RWMutex
	Name       string
	entities   map[uint16]DRObject
	players    map[uint16]*RRPlayer
	Scripts    *lua.LuaScriptGroup
	BaseConfig *database.ZoneConfig
}

func (z *Zone) Entities() []DRObject {
	z.RLock()
	defer z.RUnlock()

	l := make([]DRObject, 0)

	for _, drObject := range z.entities {
		l = append(l, drObject)
	}

	return l
}

func (z *Zone) Players() []*RRPlayer {
	z.RLock()
	defer z.RUnlock()

	l := make([]*RRPlayer, 0)

	for _, player := range z.players {
		l = append(l, player)
	}

	return l
}

func (z *Zone) RemovePlayer(id int) {
	z.Lock()
	defer z.Unlock()

	delete(z.players, uint16(id))

	toDelete := make([]uint16, 0, 1024)

	for index, entity := range z.entities {
		if entity == nil || entity.RREntityProperties().OwnerID == id {
			toDelete = append(toDelete, index)
		}
	}

	for _, index := range toDelete {
		z.entities[index].RREntityProperties().Zone = nil
		delete(z.entities, index)
	}
}

func (z *Zone) AddEntity(entity DRObject) {
	z.setZone(entity)

	z.entities[uint16(entity.RREntityProperties().ID)] = entity
}

func (z *Zone) AddPlayer(player *RRPlayer) {
	z.players[uint16(player.Conn.GetID())] = player
}

func (z *Zone) Spawn(entity DRObject) {
	Entities.RegisterAll(nil, entity)

	log.Infof("spawning entity '%s' in zone '%s'", entity.GetGCObject().GCType, z.Name)

	z.AddEntity(entity)
}

func (z *Zone) setZone(entities ...DRObject) {
	for _, entity := range entities {
		entity.RREntityProperties().Zone = z
		z.setZone(entity.Children()...)
	}
}

func (z *Zone) SendToAll(body *byter.Byter) {
	for _, player := range z.players {
		helpers.WriteCompressedASimple(player.Conn, body)
	}
}

func (z *Zone) SpawnInit(entity DRObject, position *datatypes.Vector3Float32, rotation *float32) {
	if _, ok := entity.(IWorldEntity); !ok {
		log.Errorf("cannot init non-world entity")
		return
	}

	worldEntity := entity.(IWorldEntity).GetWorldEntity()

	if position != nil {
		worldEntity.WorldPosition = *position
	}

	if rotation != nil {
		worldEntity.Rotation = *rotation
	}

	z.Spawn(entity)
}

func (z *Zone) Init() {
	log.Infof("initialising zone %s", z.Name)

	config, err := database.GetZoneConfig(z.Name)

	if err != nil {
		panic(err)
	}

	z.BaseConfig = config

	script := z.Scripts.Get("init")

	if script == nil {
		return
	}

	state := lua2.NewState()
	defer state.Close()

	RegisterLuaGlobals(state)
	AddZoneToState(state, z)

	err = script.Execute(state)

	if err != nil {
		log.Errorf("failed to execute zone init script %s: %s", z.Name, err.Error())
	}
}

func (z *Zone) ClearEntities() {
	z.entities = make(map[uint16]DRObject)
}