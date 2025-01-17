// Code generated by scripts/generatelua DO NOT EDIT.
package objects

import (
	lua "RainbowRunner/internal/lua"
	"RainbowRunner/pkg/byter"
	"RainbowRunner/pkg/datatypes"
	lua2 "github.com/yuin/gopher-lua"
)

type INPC interface {
	GetNPC() *NPC
}

func (n *NPC) GetNPC() *NPC {
	return n
}

func registerLuaNPC(state *lua2.LState) {
	// Ensure the import is referenced in code
	_ = lua.LuaScript{}

	mt := state.NewTypeMetatable("NPC")
	state.SetGlobal("NPC", mt)
	state.SetField(mt, "new", state.NewFunction(newLuaNPC))
	state.SetField(mt, "__index", state.SetFuncs(state.NewTable(),
		luaMethodsNPC(),
	))
}

func luaMethodsNPC() map[string]lua2.LGFunction {
	return luaMethodsExtend(map[string]lua2.LGFunction{
		"name":  luaGenericGetSetString[INPC](func(v INPC) *string { return &v.GetNPC().Name }),
		"level": luaGenericGetSetNumber[INPC](func(v INPC) *int32 { return &v.GetNPC().Level }),
		"writeInit": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[INPC](l, 1)
			obj := objInterface.GetNPC()
			obj.WriteInit(
				lua.CheckReferenceValue[byter.Byter](l, 2),
			)

			return 0
		},
		"getNPC": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[INPC](l, 1)
			obj := objInterface.GetNPC()
			res0 := obj.GetNPC()
			if res0 != nil {
				l.Push(res0.ToLua(l))
			} else {
				l.Push(lua2.LNil)
			}

			return 1
		},
	}, luaMethodsUnit)
}
func newLuaNPC(l *lua2.LState) int {
	obj := NewNPC(string(l.CheckString(1)), string(l.CheckString(2)),
		lua.CheckValue[datatypes.Vector3Float32](l, 3), float32(l.CheckNumber(4)),
	)
	ud := l.NewUserData()
	ud.Value = obj

	l.SetMetatable(ud, l.GetTypeMetatable("NPC"))
	l.Push(ud)
	return 1
}

func (n *NPC) ToLua(l *lua2.LState) lua2.LValue {
	ud := l.NewUserData()
	ud.Value = n

	l.SetMetatable(ud, l.GetTypeMetatable("NPC"))
	return ud
}
