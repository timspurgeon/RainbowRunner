package objects

/**
 * This file is generated by scripts/generatelua/generatelua.go
 * DO NOT EDIT
 */

import (
	lua "RainbowRunner/internal/lua"
	"RainbowRunner/pkg/byter"
	lua2 "github.com/yuin/gopher-lua"
)

func registerLuaZonePortal(state *lua2.LState) {
	mt := state.NewTypeMetatable("ZonePortal")
	state.SetGlobal("ZonePortal", mt)
	state.SetField(mt, "new", state.NewFunction(newLuaZonePortal))
	state.SetField(mt, "__index", state.SetFuncs(state.NewTable(),
		luaMethodsZonePortal(),
	))
}

func luaMethodsZonePortal() map[string]lua2.LGFunction {
	return luaMethodsExtend(map[string]lua2.LGFunction{
		"unk0":   luaGenericGetSetString[ZonePortal](func(v ZonePortal) *string { return &v.Unk0 }),
		"unk1":   luaGenericGetSetString[ZonePortal](func(v ZonePortal) *string { return &v.Unk1 }),
		"width":  luaGenericGetSetNumber[ZonePortal, uint16](func(v ZonePortal) *uint16 { return &v.Width }),
		"height": luaGenericGetSetNumber[ZonePortal, uint16](func(v ZonePortal) *uint16 { return &v.Height }),
		"unk4":   luaGenericGetSetNumber[ZonePortal, uint32](func(v ZonePortal) *uint32 { return &v.Unk4 }),
		"target": luaGenericGetSetString[ZonePortal](func(v ZonePortal) *string { return &v.Target }),
		"activate": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[ZonePortal](l, 1)
			obj.Activate(
				lua.CheckReferenceValue[RRPlayer](l, 1),
				lua.CheckReferenceValue[UnitBehavior](l, 2),
				lua.CheckValue[byte](l, 3),
			)

			return 0
		},
		"writeInit": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[ZonePortal](l, 1)
			obj.WriteInit(
				lua.CheckReferenceValue[byter.Byter](l, 1),
			)

			return 0
		},
	}, luaMethodsWorldEntity)
}

func newLuaZonePortal(l *lua2.LState) int {
	obj := NewZonePortal(
		l.CheckString(1),
		l.CheckString(2),
	)
	ud := l.NewUserData()
	ud.Value = obj

	l.SetMetatable(ud, l.GetTypeMetatable("ZonePortal"))
	l.Push(ud)
	return 1
}
