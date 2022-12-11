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

func registerLuaAvatarMetrics(state *lua2.LState) {
	mt := state.NewTypeMetatable("AvatarMetrics")
	state.SetGlobal("AvatarMetrics", mt)
	state.SetField(mt, "new", state.NewFunction(newLuaAvatarMetrics))
	state.SetField(mt, "__index", state.SetFuncs(state.NewTable(),
		luaMethodsAvatarMetrics(),
	))
}

func luaMethodsAvatarMetrics() map[string]lua2.LGFunction {
	return luaMethodsExtend(map[string]lua2.LGFunction{
		"writeFullGCObject": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[AvatarMetrics](l, 1)
			obj.WriteFullGCObject(
				lua.CheckReferenceValue[byter.Byter](l, 1),
			)

			return 0
		},
	}, luaMethodsComponent)
}

func newLuaAvatarMetrics(l *lua2.LState) int {
	obj := NewAvatarMetrics(
		uint32(l.CheckNumber(1)),
		l.CheckString(2),
	)
	ud := l.NewUserData()
	ud.Value = obj

	l.SetMetatable(ud, l.GetTypeMetatable("AvatarMetrics"))
	l.Push(ud)
	return 1
}
