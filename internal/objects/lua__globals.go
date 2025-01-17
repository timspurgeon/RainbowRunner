package objects

import (
	"RainbowRunner/pkg/datatypes"
	lua "github.com/yuin/gopher-lua"
)

//go:generate go run ../../scripts/generateluaregistrations
func RegisterLuaGlobals(state *lua.LState) {
	registerAllLuaFunctions(state)
}

type ILuaConvertible interface {
	ToLua(state *lua.LState) lua.LValue
}

func luaMethodsExtend(child map[string]lua.LGFunction, parents ...func() map[string]lua.LGFunction) map[string]lua.LGFunction {
	newMethods := make(map[string]lua.LGFunction)

	for _, parent := range parents {
		for key, value := range parent() {
			newMethods[key] = value
		}
	}

	for key, value := range child {
		newMethods[key] = value
	}

	return newMethods
}

func registerLuaVector3(s *lua.LState) {
	mt := s.NewTypeMetatable("Vector3")
	s.SetGlobal("Vector3", mt)
	s.SetField(mt, "__index", s.SetFuncs(s.NewTable(),
		map[string]lua.LGFunction{
			"x": luaGenericGetSetNumber[datatypes.Vector3Float32](func(v datatypes.Vector3Float32) *float32 { return &v.X }),
			"y": luaGenericGetSetNumber[datatypes.Vector3Float32](func(v datatypes.Vector3Float32) *float32 { return &v.Y }),
			"z": luaGenericGetSetNumber[datatypes.Vector3Float32](func(v datatypes.Vector3Float32) *float32 { return &v.Z }),
		},
	))
	s.SetField(mt, "new", s.NewFunction(newLuaVector3))
}

func newLuaVector3(state *lua.LState) int {
	v3 := datatypes.Vector3Float32{}

	if state.GetTop() == 3 {
		v3.X = float32(state.CheckNumber(1))
		v3.Y = float32(state.CheckNumber(2))
		v3.Z = float32(state.CheckNumber(3))
	}

	ud := state.NewUserData()
	ud.Value = v3
	state.SetMetatable(ud, state.GetTypeMetatable("Vector3"))

	state.Push(ud)
	return 1
}

func registerLuaVector2(s *lua.LState) {
	mt := s.NewTypeMetatable("Vector2")
	s.SetGlobal("Vector2", mt)
	s.SetField(mt, "__index", s.SetFuncs(s.NewTable(),
		map[string]lua.LGFunction{
			"x": luaGenericGetSetNumber[datatypes.Vector2Float32](func(v datatypes.Vector2Float32) *float32 { return &v.X }),
			"y": luaGenericGetSetNumber[datatypes.Vector2Float32](func(v datatypes.Vector2Float32) *float32 { return &v.Y }),
		},
	))
	s.SetField(mt, "new", s.NewFunction(newLuaVector2))
}

func newLuaVector2(state *lua.LState) int {
	v2 := datatypes.Vector2Float32{}

	if state.GetTop() == 2 {
		v2.X = float32(state.CheckNumber(1))
		v2.Y = float32(state.CheckNumber(2))
	}

	ud := state.NewUserData()
	ud.Value = v2
	state.SetMetatable(ud, state.GetTypeMetatable("Vector2"))

	state.Push(ud)
	return 1
}
