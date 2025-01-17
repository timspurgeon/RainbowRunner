package main

import (
	"RainbowRunner/internal/gosucks"
	"RainbowRunner/scripts/common"
	"bytes"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"golang.org/x/tools/go/packages"
	"os"
	"path/filepath"
	"strings"
	"text/template"
)

var (
	typeName = flag.String("type", "", "Comma separated list of types to generate lua wrappers for")
	extends  = flag.String("extends", "", "Comma separated list of types to extend")
)

var (
	fileStructs = make(map[string]*StructDef)
	imports     = make(map[string]*ImportDef)
	allStructs  = make(map[string]*StructDef)
	funcDefs    = make(map[string]*FuncDef)
	currentPkg  *packages.Package
)

func main() {
	flag.Parse()

	splitExtends := strings.Split(*extends, ",")

	if splitExtends[0] == "" {
		splitExtends = nil
	}

	fileName := os.Getenv("GOFILE")
	cwd, err := os.Getwd()
	if err != nil {
		panic(err)
	}

	filePath := filepath.Join(cwd, fileName)

	//err := getAllStructDefinitions(structs)
	pkg, err := packages.Load(&packages.Config{
		Mode: packages.NeedName | packages.NeedTypes | packages.NeedTypesSizes | packages.NeedTypesInfo | packages.NeedSyntax,
	}, cwd)

	if err != nil {
		panic(err)
	}

	currentPkg = pkg[0]

	addAllImports(imports, currentPkg)

	err = parseFileStructDefinitionsFromString(currentPkg, fileStructs, filePath)

	if err != nil {
		panic(err)
	}

	addAllMemberFunctions(fileStructs, funcDefs, currentPkg)

	err = getAllStructDefinitions(allStructs, cwd)

	if err != nil {
		panic(err)
	}

	extendFuncs, err := getExtendFuncs(splitExtends, funcDefs)

	if err != nil {
		panic(err)
	}

	//extendStructs, err := getExtendStructs(splitExtends, allStructs)
	//
	//if err != nil {
	//	panic(err)
	//}

	typeNames := strings.Split(*typeName, ",")

	err = executeGenerate(extendFuncs, imports, fileStructs, typeNames, cwd)

	if err != nil {
		panic(err)
	}

	data, err := json.MarshalIndent(fileStructs, "", "  ")

	if err != nil {
		panic(err)
	}

	gosucks.VAR(data)
	//fmt.Println(string(data))
}

func executeGenerate(splitExtends []*FuncDef, imports map[string]*ImportDef, structs map[string]*StructDef, typeNames []string, cwd string) error {
	fmt.Printf("Running %s go on %s\n", os.Args[0], os.Getenv("GOFILE"))

	for _, name := range typeNames {
		if _, ok := structs[name]; !ok {
			return errors.New(fmt.Sprintf("could not find type %s in file", name))
		}

		data, err := generateWrapper(splitExtends, imports, structs[name])

		if err != nil {
			return err
		}

		data = common.FormatScript(data)

		outputFile := filepath.Join(cwd, fmt.Sprintf("lua_generated_%s.go", strings.ToLower(name)))

		err = os.WriteFile(outputFile, data, 0755)

		if err != nil {
			return err
		}
	}

	return nil
}

func generateWrapper(extends []*FuncDef, imports map[string]*ImportDef, def *StructDef) ([]byte, error) {
	t := template.New("wrapper")

	t = t.Funcs(templateFuncMap)

	t, err := t.Parse(templateString)

	if err != nil {
		return nil, err
	}

	requiredImports := def.GetRequiredImports(imports)

	data := &TemplateData{
		Struct:  def,
		Imports: requiredImports,
		Extends: extends,
	}

	buf := &bytes.Buffer{}

	err = t.Execute(buf, data)

	if err != nil {
		return nil, err
	}

	return buf.Bytes(), nil
}
