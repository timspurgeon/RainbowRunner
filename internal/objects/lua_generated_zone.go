package objects

/**
 * This file is generated by scripts/generatelua/generatelua.go
 * DO NOT EDIT
 */

import (
	lua "RainbowRunner/internal/lua"
	"RainbowRunner/pkg/byter"
	"RainbowRunner/pkg/datatypes"
	lua2 "github.com/yuin/gopher-lua"
)

type IZone interface {
	GetZone() *Zone
}

func (z *Zone) GetZone() *Zone {
	return z
}

func registerLuaZone(state *lua2.LState) {
	// Ensure the import is referenced in code
	_ = lua.LuaScript{}

	mt := state.NewTypeMetatable("Zone")
	state.SetGlobal("Zone", mt)
	state.SetField(mt, "new", state.NewFunction(newLuaZone))
	state.SetField(mt, "__index", state.SetFuncs(state.NewTable(),
		luaMethodsZone(),
	))
}

func luaMethodsZone() map[string]lua2.LGFunction {
	return luaMethodsExtend(map[string]lua2.LGFunction{
		"name": luaGenericGetSetString[IZone](func(v IZone) *string { return &v.GetZone().Name }),
		"id":   luaGenericGetSetNumber[IZone](func(v IZone) *uint32 { return &v.GetZone().ID }),
		"getZone": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			res0 := obj.GetZone()
			ud := l.NewUserData()
			ud.Value = res0
			l.SetMetatable(ud, l.GetTypeMetatable("Zone"))
			l.Push(ud)

			return 1
		},
		"entities": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			res0 := obj.Entities()
			ud := l.NewUserData()
			ud.Value = res0
			l.SetMetatable(ud, l.GetTypeMetatable("[]DRObject"))
			l.Push(ud)

			return 1
		},
		"players": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			res0 := obj.Players()
			ud := l.NewUserData()
			ud.Value = res0
			l.SetMetatable(ud, l.GetTypeMetatable("[]*RRPlayer"))
			l.Push(ud)

			return 1
		},
		"removePlayer": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			obj.RemovePlayer(int(l.CheckNumber(2)))

			return 0
		},
		"addEntity": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			obj.AddEntity(func(v uint16) *uint16 { return &v }(uint16(l.CheckNumber(2))),
				lua.CheckValue[DRObject](l, 3),
			)

			return 0
		},
		"addPlayer": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			obj.AddPlayer(
				lua.CheckReferenceValue[RRPlayer](l, 2),
			)

			return 0
		},
		"sendToAll": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			obj.SendToAll(
				lua.CheckReferenceValue[byter.Byter](l, 2),
			)

			return 0
		},
		"spawn": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			obj.Spawn(
				lua.CheckValue[DRObject](l, 2),
				lua.CheckValue[datatypes.Vector3Float32](l, 3), float32(l.CheckNumber(4)),
			)

			return 0
		},
		"loadNPCFromConfig": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			res0 := obj.LoadNPCFromConfig(string(l.CheckString(2)))
			ud := l.NewUserData()
			ud.Value = res0
			l.SetMetatable(ud, l.GetTypeMetatable("NPC"))
			l.Push(ud)

			return 1
		},
		"init": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			obj.Init()

			return 0
		},
		"clearEntities": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			obj.ClearEntities()

			return 0
		},
		"reloadPathMap": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			obj.ReloadPathMap()

			return 0
		},
		"tick": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			obj.Tick()

			return 0
		},
		"findEntityByID": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			res0 := obj.FindEntityByID(uint16(l.CheckNumber(2)))
			ud := l.NewUserData()
			ud.Value = res0
			l.SetMetatable(ud, l.GetTypeMetatable("DRObject"))
			l.Push(ud)

			return 1
		},
		"giveID": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IZone](l, 1)
			obj := objInterface.GetZone()
			obj.GiveID(
				lua.CheckValue[DRObject](l, 2),
			)

			return 0
		},
	})
}
func newLuaZone(l *lua2.LState) int {
	obj := NewZone(string(l.CheckString(1)), uint32(l.CheckNumber(2)))
	ud := l.NewUserData()
	ud.Value = obj

	l.SetMetatable(ud, l.GetTypeMetatable("Zone"))
	l.Push(ud)
	return 1
}

func (z *Zone) ToLua(l *lua2.LState) lua2.LValue {
	ud := l.NewUserData()
	ud.Value = z

	l.SetMetatable(ud, l.GetTypeMetatable("Zone"))
	return ud
}
