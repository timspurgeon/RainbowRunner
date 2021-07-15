package main

import "RainbowRunner/internal/database"

type DRClassStack struct {
	Stack []*database.DRClass
	Index int
}

func NewDRClassStack() *DRClassStack {
	return &DRClassStack{
		Stack: make([]*database.DRClass, 1024),
		Index: 0,
	}
}

func (s *DRClassStack) Push(str *database.DRClass) {
	s.Stack[s.Index] = str
	s.Index = s.Index + 1
}

func (s *DRClassStack) Pop() *database.DRClass {
	s.Index = s.Index - 1
	res := s.Stack[s.Index]
	return res
}

func (s *DRClassStack) Current() *database.DRClass {
	return s.Stack[s.Index-1]
}

func (s *DRClassStack) SetCurrent(i *database.DRClass) {
	s.Stack[s.Index-1] = i
}
