// Code generated by scripts/generatelua DO NOT EDIT.
package objects

import (
	lua "RainbowRunner/internal/lua"
	"RainbowRunner/pkg/byter"
	lua2 "github.com/yuin/gopher-lua"
)

type IUnitContainer interface {
	GetUnitContainer() *UnitContainer
}

func (u *UnitContainer) GetUnitContainer() *UnitContainer {
	return u
}

func registerLuaUnitContainer(state *lua2.LState) {
	// Ensure the import is referenced in code
	_ = lua.LuaScript{}

	mt := state.NewTypeMetatable("UnitContainer")
	state.SetGlobal("UnitContainer", mt)
	state.SetField(mt, "new", state.NewFunction(newLuaUnitContainer))
	state.SetField(mt, "__index", state.SetFuncs(state.NewTable(),
		luaMethodsUnitContainer(),
	))
}

func luaMethodsUnitContainer() map[string]lua2.LGFunction {
	return luaMethodsExtend(map[string]lua2.LGFunction{
		"manipulator": luaGenericGetSetValue[IUnitContainer, DRObject](func(v IUnitContainer) *DRObject { return &v.GetUnitContainer().Manipulator }),
		"activeItem":  luaGenericGetSetValue[IUnitContainer, DRObject](func(v IUnitContainer) *DRObject { return &v.GetUnitContainer().ActiveItem }),
		"avatar":      luaGenericGetSetValue[IUnitContainer, *Avatar](func(v IUnitContainer) **Avatar { return &v.GetUnitContainer().Avatar }),
		"readUpdate": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IUnitContainer](l, 1)
			obj := objInterface.GetUnitContainer()
			res0 := obj.ReadUpdate(
				lua.CheckReferenceValue[byter.Byter](l, 2),
			)
			ud := l.NewUserData()
			ud.Value = res0
			l.SetMetatable(ud, l.GetTypeMetatable("error"))
			l.Push(ud)

			return 1
		},
		"writeFullGCObject": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IUnitContainer](l, 1)
			obj := objInterface.GetUnitContainer()
			obj.WriteFullGCObject(
				lua.CheckReferenceValue[byter.Byter](l, 2),
			)

			return 0
		},
		"setActiveItem": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IUnitContainer](l, 1)
			obj := objInterface.GetUnitContainer()
			obj.SetActiveItem(
				lua.CheckValue[DRObject](l, 2),
			)

			return 0
		},
		"writeSetActiveItem": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IUnitContainer](l, 1)
			obj := objInterface.GetUnitContainer()
			obj.WriteSetActiveItem(
				lua.CheckReferenceValue[byter.Byter](l, 2),
			)

			return 0
		},
		"writeClearActiveItem": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IUnitContainer](l, 1)
			obj := objInterface.GetUnitContainer()
			obj.WriteClearActiveItem(
				lua.CheckReferenceValue[byter.Byter](l, 2),
			)

			return 0
		},
		"writeRemoveItem": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IUnitContainer](l, 1)
			obj := objInterface.GetUnitContainer()
			obj.WriteRemoveItem(
				lua.CheckReferenceValue[byter.Byter](l, 2), uint32(l.CheckNumber(3)),
			)

			return 0
		},
		"writeAddItem": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IUnitContainer](l, 1)
			obj := objInterface.GetUnitContainer()
			obj.WriteAddItem(
				lua.CheckReferenceValue[byter.Byter](l, 2),
				lua.CheckValue[DRObject](l, 3),
				lua.CheckReferenceValue[Inventory](l, 4), byte(l.CheckNumber(5)), byte(l.CheckNumber(6)),
			)

			return 0
		},
		"getInventoryByID": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IUnitContainer](l, 1)
			obj := objInterface.GetUnitContainer()
			res0 := obj.GetInventoryByID(byte(l.CheckNumber(2)))
			if res0 != nil {
				l.Push(res0.ToLua(l))
			} else {
				l.Push(lua2.LNil)
			}

			return 1
		},
		"getUnitContainer": func(l *lua2.LState) int {
			objInterface := lua.CheckInterfaceValue[IUnitContainer](l, 1)
			obj := objInterface.GetUnitContainer()
			res0 := obj.GetUnitContainer()
			if res0 != nil {
				l.Push(res0.ToLua(l))
			} else {
				l.Push(lua2.LNil)
			}

			return 1
		},
	}, luaMethodsComponent)
}
func newLuaUnitContainer(l *lua2.LState) int {
	obj := NewUnitContainer(
		lua.CheckValue[DRObject](l, 1), string(l.CheckString(2)),
		lua.CheckReferenceValue[Avatar](l, 3),
	)
	ud := l.NewUserData()
	ud.Value = obj

	l.SetMetatable(ud, l.GetTypeMetatable("UnitContainer"))
	l.Push(ud)
	return 1
}

func (u *UnitContainer) ToLua(l *lua2.LState) lua2.LValue {
	ud := l.NewUserData()
	ud.Value = u

	l.SetMetatable(ud, l.GetTypeMetatable("UnitContainer"))
	return ud
}
