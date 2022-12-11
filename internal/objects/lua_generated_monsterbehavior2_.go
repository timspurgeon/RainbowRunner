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

func registerLuaMonsterBehavior2(state *lua2.LState) {
	mt := state.NewTypeMetatable("MonsterBehavior2")
	state.SetGlobal("MonsterBehavior2", mt)
	state.SetField(mt, "new", state.NewFunction(newLuaMonsterBehavior2))
	state.SetField(mt, "__index", state.SetFuncs(state.NewTable(),
		luaMethodsMonsterBehavior2(),
	))
}

func luaMethodsMonsterBehavior2() map[string]lua2.LGFunction {
	return luaMethodsExtend(map[string]lua2.LGFunction{
		"writeInit": func(l *lua2.LState) int {
			obj := lua.CheckReferenceValue[MonsterBehavior2](l, 1)
			obj.WriteInit(
				lua.CheckReferenceValue[byter.Byter](l, 1),
			)

			return 0
		},
	}, luaMethodsUnitBehavior)
}

func newLuaMonsterBehavior2(l *lua2.LState) int {
	obj := NewMonsterBehavior2(
		l.CheckString(1),
	)
	ud := l.NewUserData()
	ud.Value = obj

	l.SetMetatable(ud, l.GetTypeMetatable("MonsterBehavior2"))
	l.Push(ud)
	return 1
}
