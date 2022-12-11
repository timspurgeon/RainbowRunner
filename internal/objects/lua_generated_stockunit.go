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

type IStockUnit interface {
	GetStockUnit() *StockUnit
}

func (s *StockUnit) GetStockUnit() *StockUnit {
	return s
}

func registerLuaStockUnit(state *lua2.LState) {
	// Ensure the import is referenced in code
	_ = lua.LuaScript{}

	mt := state.NewTypeMetatable("StockUnit")
	state.SetGlobal("StockUnit", mt)
	state.SetField(mt, "new", state.NewFunction(newLuaStockUnit))
	state.SetField(mt, "__index", state.SetFuncs(state.NewTable(),
		luaMethodsStockUnit(),
	))
}

func luaMethodsStockUnit() map[string]lua2.LGFunction {
	return luaMethodsExtend(map[string]lua2.LGFunction{
		"writeInit": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IStockUnit](l, 1)
			obj := objInterface.GetStockUnit()
			obj.WriteInit(
				lua.CheckReferenceValue[byter.Byter](l, 2),
			)

			return 0
		},
		"getStockUnit": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IStockUnit](l, 1)
			obj := objInterface.GetStockUnit()
			res0 := obj.GetStockUnit()
			ud := l.NewUserData()
			ud.Value = res0
			l.SetMetatable(ud, l.GetTypeMetatable("StockUnit"))
			l.Push(ud)

			return 1
		},
	}, luaMethodsUnit)
}
func newLuaStockUnit(l *lua2.LState) int {
	obj := NewStockUnit(string(l.CheckString(1)))
	ud := l.NewUserData()
	ud.Value = obj

	l.SetMetatable(ud, l.GetTypeMetatable("StockUnit"))
	l.Push(ud)
	return 1
}

func (s *StockUnit) ToLua(l *lua2.LState) lua2.LValue {
	ud := l.NewUserData()
	ud.Value = s

	l.SetMetatable(ud, l.GetTypeMetatable("StockUnit"))
	return ud
}
