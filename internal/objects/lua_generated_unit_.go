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

func registerLuaUnit(state *lua2.LState) {
	mt := state.NewTypeMetatable("Unit")
	state.SetGlobal("Unit", mt)
	state.SetField(mt, "new", state.NewFunction(newLuaUnit))
	state.SetField(mt, "__index", state.SetFuncs(state.NewTable(),
		luaMethodsUnit(),
	))
}

func luaMethodsUnit() map[string]lua2.LGFunction {
	return luaMethodsExtend(map[string]lua2.LGFunction{
		"hp":                luaGenericGetSetNumber[Unit, int](func(v Unit) *int { return &v.HP }),
		"mp":                luaGenericGetSetNumber[Unit, int](func(v Unit) *int { return &v.MP }),
		"unk20CaseEntityID": luaGenericGetSetNumber[Unit, uint16](func(v Unit) *uint16 { return &v.Unk20CaseEntityID }),
		"unk40Case0":        luaGenericGetSetNumber[Unit, uint16](func(v Unit) *uint16 { return &v.Unk40Case0 }),
		"unk40Case1":        luaGenericGetSetNumber[Unit, uint16](func(v Unit) *uint16 { return &v.Unk40Case1 }),
		"unk40Case2":        luaGenericGetSetNumber[Unit, uint16](func(v Unit) *uint16 { return &v.Unk40Case2 }),
		"getUnit": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[Unit](l, 1)
			res0 := obj.GetUnit()
			ud := l.NewUserData()
			ud.Value = res0
			l.SetMetatable(ud, l.GetTypeMetatable("Unit"))
			l.Push(ud)

			return 1
		},
		"writeInit": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[Unit](l, 1)
			obj.WriteInit(
				lua.CheckReferenceValue[byter.Byter](l, 1),
			)

			return 0
		},
		"warp": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[Unit](l, 1)
			obj.Warp(
				lua.CheckValue[datatypes.Vector3Float32](l, 1),
			)

			return 0
		},
	}, luaMethodsWorldEntity)
}

func newLuaUnit(l *lua2.LState) int {
	obj := NewUnit(
		l.CheckString(1),
	)
	ud := l.NewUserData()
	ud.Value = obj

	l.SetMetatable(ud, l.GetTypeMetatable("Unit"))
	l.Push(ud)
	return 1
}
