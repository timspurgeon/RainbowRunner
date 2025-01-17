package datatypes

import (
	"fmt"
	lua "github.com/yuin/gopher-lua"
	"math"
)

type Vector2 struct {
	X, Y int32
}

func (v Vector2) Distance(other Vector2) float64 {
	xd := other.X - v.X
	yd := other.Y - v.Y

	a := math.Pow(float64(xd), 2)
	b := math.Pow(float64(yd), 2)

	return math.Sqrt(a + b)
}

func (v Vector2) ToVector3Float32() Vector3Float32 {
	return Vector3Float32{
		X: float32(v.X),
		Y: float32(v.Y),
	}
}

type Vector2Float32 struct {
	X, Y float32
}

func (f Vector2Float32) DivideByFloat32(f2 float32) Vector2Float32 {
	return Vector2Float32{
		X: f.X / f2,
		Y: f.Y / f2,
	}
}

type Vector3 struct {
	X, Y, Z int32
}

func (v Vector3) ToVector2() Vector2 {
	return Vector2{
		X: v.X,
		Y: v.Y,
	}
}

type Vector3Short struct {
	X, Y, Z int16
}

type Vector3Float32 struct {
	X, Y, Z float32
}

func (v Vector3Float32) ToVector2Float32() Vector2Float32 {
	return Vector2Float32{
		X: v.X,
		Y: v.Y,
	}
}

func (f Vector3Float32) Add(other Vector3Float32) Vector3Float32 {
	return Vector3Float32{
		X: f.X + other.X,
		Y: f.Y + other.Y,
		Z: f.Z + other.Z,
	}
}

func (f Vector3Float32) Sub(other Vector3Float32) Vector3Float32 {
	return Vector3Float32{
		X: f.X - other.X,
		Y: f.Y - other.Y,
		Z: f.Z - other.Z,
	}
}

func (f Vector3Float32) String() string {
	return fmt.Sprintf("(%f, %f, %f)", f.X, f.Y, f.Z)
}

func (f Vector3Float32) DivideByFloat32(i float32) Vector3Float32 {
	return Vector3Float32{
		X: f.X / i,
		Y: f.Y / i,
		Z: f.Z / i,
	}
}

func (f Vector3Float32) MultiplyByFloat32(i float32) Vector3Float32 {
	return Vector3Float32{
		X: f.X * i,
		Y: f.Y * i,
		Z: f.Z * i,
	}
}

type Transform struct {
	Position Vector3Float32
	Rotation float32
}

func (f Vector3Float32) ToLua(state *lua.LState) lua.LValue {
	ud := state.NewUserData()
	ud.Value = f
	state.SetMetatable(ud, state.GetTypeMetatable("Vector3"))
	return ud
}
