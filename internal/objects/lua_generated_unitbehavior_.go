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

func registerLuaUnitBehavior(state *lua2.LState) {
	mt := state.NewTypeMetatable("UnitBehavior")
	state.SetGlobal("UnitBehavior", mt)
	state.SetField(mt, "new", state.NewFunction(newLuaUnitBehavior))
	state.SetField(mt, "__index", state.SetFuncs(state.NewTable(),
		luaMethodsUnitBehavior(),
	))
}

func luaMethodsUnitBehavior() map[string]lua2.LGFunction {
	return luaMethodsExtend(map[string]lua2.LGFunction{
		"rotation": luaGenericGetSetNumber[UnitBehavior, float32](func(v UnitBehavior) *float32 { return &v.Rotation }),
		"getUnitBehavior": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[UnitBehavior](l, 1)
			res0 := obj.GetUnitBehavior()
			ud := l.NewUserData()
			ud.Value = res0
			l.SetMetatable(ud, l.GetTypeMetatable("UnitBehavior"))
			l.Push(ud)

			return 1
		},
		"writeMoveUpdate": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[UnitBehavior](l, 1)
			obj.WriteMoveUpdate(
				lua.CheckReferenceValue[byter.Byter](l, 1),
			)

			return 0
		},
		"writeInit": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[UnitBehavior](l, 1)
			obj.WriteInit(
				lua.CheckReferenceValue[byter.Byter](l, 1),
			)

			return 0
		},
		"readUpdate": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[UnitBehavior](l, 1)
			res0 := obj.ReadUpdate(
				lua.CheckReferenceValue[byter.Byter](l, 1),
			)
			ud := l.NewUserData()
			ud.Value = res0
			l.SetMetatable(ud, l.GetTypeMetatable("error"))
			l.Push(ud)

			return 1
		},
		"warp": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[UnitBehavior](l, 1)
			obj.Warp(
				float32(l.CheckNumber(1)),
				float32(l.CheckNumber(2)),
				float32(l.CheckNumber(3)),
			)

			return 0
		},
		"writeWarp": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[UnitBehavior](l, 1)
			obj.WriteWarp(
				lua.CheckReferenceValue[ClientEntityWriter](l, 1),
			)

			return 0
		},
	}, luaMethodsComponent)
}

func newLuaUnitBehavior(l *lua2.LState) int {
	obj := NewUnitBehavior(
		l.CheckString(1),
	)
	ud := l.NewUserData()
	ud.Value = obj

	l.SetMetatable(ud, l.GetTypeMetatable("UnitBehavior"))
	l.Push(ud)
	return 1
}
