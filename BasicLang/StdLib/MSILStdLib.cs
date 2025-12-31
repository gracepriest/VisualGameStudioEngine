using System;
using System.Collections.Generic;

namespace BasicLang.Compiler.StdLib.MSIL
{
    /// <summary>
    /// MSIL (.NET IL) implementation of standard library functions
    /// Maps BasicLang stdlib to .NET Base Class Library calls in IL form
    /// </summary>
    public class MSILStdLibProvider : IStdLibProvider, IStdIO, IStdString, IStdMath, IStdArray, IStdConversion
    {
        private static readonly Dictionary<string, StdLibFunction> _functions = new Dictionary<string, StdLibFunction>(StringComparer.OrdinalIgnoreCase)
        {
            // I/O
            ["Print"] = new StdLibFunction { Name = "Print", Category = StdLibCategory.IO, ParameterTypes = new[] { "Object" }, ReturnType = "Void" },
            ["PrintLine"] = new StdLibFunction { Name = "PrintLine", Category = StdLibCategory.IO, ParameterTypes = new[] { "Object" }, ReturnType = "Void" },
            ["Input"] = new StdLibFunction { Name = "Input", Category = StdLibCategory.IO, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["ReadLine"] = new StdLibFunction { Name = "ReadLine", Category = StdLibCategory.IO, ParameterTypes = Array.Empty<string>(), ReturnType = "String" },

            // String
            ["Len"] = new StdLibFunction { Name = "Len", Category = StdLibCategory.String, ParameterTypes = new[] { "String" }, ReturnType = "Integer" },
            ["Mid"] = new StdLibFunction { Name = "Mid", Category = StdLibCategory.String, ParameterTypes = new[] { "String", "Integer", "Integer" }, ReturnType = "String" },
            ["Left"] = new StdLibFunction { Name = "Left", Category = StdLibCategory.String, ParameterTypes = new[] { "String", "Integer" }, ReturnType = "String" },
            ["Right"] = new StdLibFunction { Name = "Right", Category = StdLibCategory.String, ParameterTypes = new[] { "String", "Integer" }, ReturnType = "String" },
            ["UCase"] = new StdLibFunction { Name = "UCase", Category = StdLibCategory.String, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["LCase"] = new StdLibFunction { Name = "LCase", Category = StdLibCategory.String, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["Trim"] = new StdLibFunction { Name = "Trim", Category = StdLibCategory.String, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["InStr"] = new StdLibFunction { Name = "InStr", Category = StdLibCategory.String, ParameterTypes = new[] { "String", "String" }, ReturnType = "Integer" },
            ["Replace"] = new StdLibFunction { Name = "Replace", Category = StdLibCategory.String, ParameterTypes = new[] { "String", "String", "String" }, ReturnType = "String" },

            // Math
            ["Abs"] = new StdLibFunction { Name = "Abs", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double" }, ReturnType = "Double" },
            ["Sqrt"] = new StdLibFunction { Name = "Sqrt", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double" }, ReturnType = "Double" },
            ["Pow"] = new StdLibFunction { Name = "Pow", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double", "Double" }, ReturnType = "Double" },
            ["Sin"] = new StdLibFunction { Name = "Sin", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double" }, ReturnType = "Double" },
            ["Cos"] = new StdLibFunction { Name = "Cos", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double" }, ReturnType = "Double" },
            ["Tan"] = new StdLibFunction { Name = "Tan", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double" }, ReturnType = "Double" },
            ["Log"] = new StdLibFunction { Name = "Log", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double" }, ReturnType = "Double" },
            ["Exp"] = new StdLibFunction { Name = "Exp", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double" }, ReturnType = "Double" },
            ["Floor"] = new StdLibFunction { Name = "Floor", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double" }, ReturnType = "Double" },
            ["Ceiling"] = new StdLibFunction { Name = "Ceiling", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double" }, ReturnType = "Double" },
            ["Round"] = new StdLibFunction { Name = "Round", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double" }, ReturnType = "Double" },
            ["Min"] = new StdLibFunction { Name = "Min", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double", "Double" }, ReturnType = "Double" },
            ["Max"] = new StdLibFunction { Name = "Max", Category = StdLibCategory.Math, ParameterTypes = new[] { "Double", "Double" }, ReturnType = "Double" },
            ["Rnd"] = new StdLibFunction { Name = "Rnd", Category = StdLibCategory.Math, ParameterTypes = Array.Empty<string>(), ReturnType = "Double" },
            ["Randomize"] = new StdLibFunction { Name = "Randomize", Category = StdLibCategory.Math, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },

            // Array
            ["UBound"] = new StdLibFunction { Name = "UBound", Category = StdLibCategory.Array, ParameterTypes = new[] { "Array", "Integer" }, ReturnType = "Integer" },
            ["LBound"] = new StdLibFunction { Name = "LBound", Category = StdLibCategory.Array, ParameterTypes = new[] { "Array", "Integer" }, ReturnType = "Integer" },

            // Conversion
            ["CInt"] = new StdLibFunction { Name = "CInt", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Integer" },
            ["CLng"] = new StdLibFunction { Name = "CLng", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Long" },
            ["CDbl"] = new StdLibFunction { Name = "CDbl", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Double" },
            ["CSng"] = new StdLibFunction { Name = "CSng", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Single" },
            ["CStr"] = new StdLibFunction { Name = "CStr", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "String" },
            ["CBool"] = new StdLibFunction { Name = "CBool", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Boolean" },

            // Collections - List operations
            ["CreateList"] = new StdLibFunction { Name = "CreateList", Category = StdLibCategory.Collections, ParameterTypes = Array.Empty<string>(), ReturnType = "List" },
            ["ListAdd"] = new StdLibFunction { Name = "ListAdd", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Object" }, ReturnType = "Void" },
            ["ListGet"] = new StdLibFunction { Name = "ListGet", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Integer" }, ReturnType = "Object" },
            ["ListSet"] = new StdLibFunction { Name = "ListSet", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Integer", "Object" }, ReturnType = "Void" },
            ["ListRemove"] = new StdLibFunction { Name = "ListRemove", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Object" }, ReturnType = "Boolean" },
            ["ListRemoveAt"] = new StdLibFunction { Name = "ListRemoveAt", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Integer" }, ReturnType = "Void" },
            ["ListCount"] = new StdLibFunction { Name = "ListCount", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List" }, ReturnType = "Integer" },
            ["ListContains"] = new StdLibFunction { Name = "ListContains", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Object" }, ReturnType = "Boolean" },
            ["ListIndexOf"] = new StdLibFunction { Name = "ListIndexOf", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Object" }, ReturnType = "Integer" },
            ["ListClear"] = new StdLibFunction { Name = "ListClear", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List" }, ReturnType = "Void" },
            ["ListInsert"] = new StdLibFunction { Name = "ListInsert", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Integer", "Object" }, ReturnType = "Void" },

            // Collections - Dictionary operations
            ["CreateDictionary"] = new StdLibFunction { Name = "CreateDictionary", Category = StdLibCategory.Collections, ParameterTypes = Array.Empty<string>(), ReturnType = "Dictionary" },
            ["DictAdd"] = new StdLibFunction { Name = "DictAdd", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object", "Object" }, ReturnType = "Void" },
            ["DictGet"] = new StdLibFunction { Name = "DictGet", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object" }, ReturnType = "Object" },
            ["DictSet"] = new StdLibFunction { Name = "DictSet", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object", "Object" }, ReturnType = "Void" },
            ["DictRemove"] = new StdLibFunction { Name = "DictRemove", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object" }, ReturnType = "Boolean" },
            ["DictCount"] = new StdLibFunction { Name = "DictCount", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary" }, ReturnType = "Integer" },
            ["DictContainsKey"] = new StdLibFunction { Name = "DictContainsKey", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object" }, ReturnType = "Boolean" },
            ["DictClear"] = new StdLibFunction { Name = "DictClear", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary" }, ReturnType = "Void" },

            // Collections - HashSet operations
            ["CreateHashSet"] = new StdLibFunction { Name = "CreateHashSet", Category = StdLibCategory.Collections, ParameterTypes = Array.Empty<string>(), ReturnType = "HashSet" },
            ["SetAdd"] = new StdLibFunction { Name = "SetAdd", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "Object" }, ReturnType = "Boolean" },
            ["SetRemove"] = new StdLibFunction { Name = "SetRemove", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "Object" }, ReturnType = "Boolean" },
            ["SetContains"] = new StdLibFunction { Name = "SetContains", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "Object" }, ReturnType = "Boolean" },
            ["SetCount"] = new StdLibFunction { Name = "SetCount", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet" }, ReturnType = "Integer" },
            ["SetClear"] = new StdLibFunction { Name = "SetClear", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet" }, ReturnType = "Void" },
        };

        public bool CanHandle(string functionName) => _functions.ContainsKey(functionName);

        public string EmitCall(string functionName, string[] arguments)
        {
            if (!_functions.TryGetValue(functionName, out var func))
                return null;

            return func.Category switch
            {
                StdLibCategory.IO => EmitIOCall(functionName, arguments),
                StdLibCategory.String => EmitStringCall(functionName, arguments),
                StdLibCategory.Math => EmitMathCall(functionName, arguments),
                StdLibCategory.Array => EmitArrayCall(functionName, arguments),
                StdLibCategory.Conversion => EmitConversionCall(functionName, arguments),
                StdLibCategory.Collections => EmitCollectionsCall(functionName, arguments),
                _ => null
            };
        }

        public IEnumerable<string> GetRequiredImports(string functionName)
        {
            // MSIL references mscorlib by default
            yield return "[mscorlib]System";
        }

        public string GetInlineImplementation(string functionName)
        {
            // MSIL uses BCL calls, no inline implementations needed
            return null;
        }

        /// <summary>
        /// Get the function's return type in MSIL format
        /// </summary>
        public string GetReturnType(string functionName)
        {
            if (_functions.TryGetValue(functionName, out var func))
            {
                return func.ReturnType switch
                {
                    "Integer" => "int32",
                    "Long" => "int64",
                    "Single" => "float32",
                    "Double" => "float64",
                    "String" => "string",
                    "Boolean" => "bool",
                    "Void" => "void",
                    _ => "object"
                };
            }
            return "object";
        }

        #region I/O Emissions

        private string EmitIOCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "print" => EmitPrint(args.Length > 0 ? args[0] : ""),
                "printline" => EmitPrintLine(args.Length > 0 ? args[0] : ""),
                "input" => EmitInput(args.Length > 0 ? args[0] : "\"\""),
                "readline" => EmitReadLine(),
                _ => null
            };
        }

        // Returns IL instruction sequences (each line is a separate instruction)
        public string EmitPrint(string value) => "call void [mscorlib]System.Console::Write(object)";
        public string EmitPrintLine(string value) => "call void [mscorlib]System.Console::WriteLine(object)";
        public string EmitInput(string prompt) =>
            "call void [mscorlib]System.Console::Write(string)\n" +
            "call string [mscorlib]System.Console::ReadLine()";
        public string EmitReadLine() => "call string [mscorlib]System.Console::ReadLine()";

        /// <summary>
        /// Get typed WriteLine call based on argument type
        /// </summary>
        public string EmitPrintLineTyped(string argType)
        {
            return argType switch
            {
                "int32" => "call void [mscorlib]System.Console::WriteLine(int32)",
                "int64" => "call void [mscorlib]System.Console::WriteLine(int64)",
                "float32" => "call void [mscorlib]System.Console::WriteLine(float32)",
                "float64" => "call void [mscorlib]System.Console::WriteLine(float64)",
                "string" => "call void [mscorlib]System.Console::WriteLine(string)",
                "bool" => "call void [mscorlib]System.Console::WriteLine(bool)",
                _ => "call void [mscorlib]System.Console::WriteLine(object)"
            };
        }

        #endregion

        #region String Emissions

        private string EmitStringCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "len" => EmitLen(args[0]),
                "mid" => EmitMid(args[0], args[1], args[2]),
                "left" => EmitLeft(args[0], args[1]),
                "right" => EmitRight(args[0], args[1]),
                "ucase" => EmitUCase(args[0]),
                "lcase" => EmitLCase(args[0]),
                "trim" => EmitTrim(args[0]),
                "instr" => EmitInStr(args[0], args[1]),
                "replace" => EmitReplace(args[0], args[1], args[2]),
                _ => null
            };
        }

        public string EmitLen(string str) => "callvirt instance int32 [mscorlib]System.String::get_Length()";
        public string EmitMid(string str, string start, string length) =>
            "ldc.i4.1\n" +
            "sub\n" +  // Convert 1-based to 0-based
            "callvirt instance string [mscorlib]System.String::Substring(int32, int32)";
        public string EmitLeft(string str, string length) =>
            "ldc.i4.0\n" +
            "callvirt instance string [mscorlib]System.String::Substring(int32, int32)";
        public string EmitRight(string str, string length) =>
            "// Right - needs stack manipulation for length calculation\n" +
            "callvirt instance string [mscorlib]System.String::Substring(int32)";
        public string EmitUCase(string str) => "callvirt instance string [mscorlib]System.String::ToUpper()";
        public string EmitLCase(string str) => "callvirt instance string [mscorlib]System.String::ToLower()";
        public string EmitTrim(string str) => "callvirt instance string [mscorlib]System.String::Trim()";
        public string EmitInStr(string str, string search) =>
            "callvirt instance int32 [mscorlib]System.String::IndexOf(string)\n" +
            "ldc.i4.1\n" +
            "add"; // Convert 0-based to 1-based
        public string EmitReplace(string str, string find, string replaceWith) =>
            "callvirt instance string [mscorlib]System.String::Replace(string, string)";

        #endregion

        #region Math Emissions

        private string EmitMathCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "abs" => EmitAbs(args[0]),
                "sqrt" => EmitSqrt(args[0]),
                "pow" => EmitPow(args[0], args[1]),
                "sin" => EmitSin(args[0]),
                "cos" => EmitCos(args[0]),
                "tan" => EmitTan(args[0]),
                "log" => EmitLog(args[0]),
                "exp" => EmitExp(args[0]),
                "floor" => EmitFloor(args[0]),
                "ceiling" => EmitCeiling(args[0]),
                "round" => EmitRound(args[0]),
                "min" => EmitMin(args[0], args[1]),
                "max" => EmitMax(args[0], args[1]),
                "rnd" => EmitRnd(),
                "randomize" => EmitRandomize(),
                _ => null
            };
        }

        public string EmitAbs(string value) => "call float64 [mscorlib]System.Math::Abs(float64)";
        public string EmitSqrt(string value) => "call float64 [mscorlib]System.Math::Sqrt(float64)";
        public string EmitPow(string baseVal, string exponent) => "call float64 [mscorlib]System.Math::Pow(float64, float64)";
        public string EmitSin(string value) => "call float64 [mscorlib]System.Math::Sin(float64)";
        public string EmitCos(string value) => "call float64 [mscorlib]System.Math::Cos(float64)";
        public string EmitTan(string value) => "call float64 [mscorlib]System.Math::Tan(float64)";
        public string EmitLog(string value) => "call float64 [mscorlib]System.Math::Log(float64)";
        public string EmitExp(string value) => "call float64 [mscorlib]System.Math::Exp(float64)";
        public string EmitFloor(string value) => "call float64 [mscorlib]System.Math::Floor(float64)";
        public string EmitCeiling(string value) => "call float64 [mscorlib]System.Math::Ceiling(float64)";
        public string EmitRound(string value) => "call float64 [mscorlib]System.Math::Round(float64)";
        public string EmitMin(string a, string b) => "call float64 [mscorlib]System.Math::Min(float64, float64)";
        public string EmitMax(string a, string b) => "call float64 [mscorlib]System.Math::Max(float64, float64)";

        public string EmitRnd() =>
            "newobj instance void [mscorlib]System.Random::.ctor()\n" +
            "callvirt instance float64 [mscorlib]System.Random::NextDouble()";

        public string EmitRandomize() => "// Randomize - no-op in .NET (Random is auto-seeded)";

        #endregion

        #region Array Emissions

        private string EmitArrayCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "ubound" => args.Length > 1 ? EmitUBoundDim(args[0], args[1]) : EmitUBound(args[0]),
                "lbound" => args.Length > 1 ? EmitLBoundDim(args[0], args[1]) : EmitLBound(args[0]),
                "length" => EmitLength(args[0]),
                _ => null
            };
        }

        public string EmitUBound(string array) =>
            "ldlen\n" +
            "conv.i4\n" +
            "ldc.i4.1\n" +
            "sub";
        public string EmitLBound(string array) => "ldc.i4.0";
        public string EmitUBoundDim(string array, string dimension) =>
            "callvirt instance int32 [mscorlib]System.Array::GetUpperBound(int32)";
        public string EmitLBoundDim(string array, string dimension) =>
            "callvirt instance int32 [mscorlib]System.Array::GetLowerBound(int32)";
        public string EmitLength(string array) => "ldlen\nconv.i4";
        public string EmitReDim(string array, string newSize) =>
            "call void [mscorlib]System.Array::Resize<object>(!!0[]&, int32)";

        #endregion

        #region Conversion Emissions

        private string EmitConversionCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "cint" => EmitCInt(args[0]),
                "clng" => EmitCLng(args[0]),
                "cdbl" => EmitCDbl(args[0]),
                "csng" => EmitCSng(args[0]),
                "cstr" => EmitCStr(args[0]),
                "cbool" => EmitCBool(args[0]),
                "cchar" => EmitCChar(args[0]),
                _ => null
            };
        }

        public string EmitCInt(string value) => "conv.i4";
        public string EmitCLng(string value) => "conv.i8";
        public string EmitCDbl(string value) => "conv.r8";
        public string EmitCSng(string value) => "conv.r4";
        public string EmitCStr(string value) => "callvirt instance string [mscorlib]System.Object::ToString()";
        public string EmitCBool(string value) =>
            "ldc.i4.0\n" +
            "cgt.un";
        public string EmitCChar(string value) => "conv.u2";

        #endregion

        #region Collections Emissions

        private string EmitCollectionsCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                // List operations
                "createlist" => EmitCreateList(),
                "listadd" => EmitListAdd(args[0], args[1]),
                "listget" => EmitListGet(args[0], args[1]),
                "listset" => EmitListSet(args[0], args[1], args[2]),
                "listremove" => EmitListRemove(args[0], args[1]),
                "listremoveat" => EmitListRemoveAt(args[0], args[1]),
                "listcount" => EmitListCount(args[0]),
                "listcontains" => EmitListContains(args[0], args[1]),
                "listindexof" => EmitListIndexOf(args[0], args[1]),
                "listclear" => EmitListClear(args[0]),
                "listinsert" => EmitListInsert(args[0], args[1], args[2]),

                // Dictionary operations
                "createdictionary" => EmitCreateDictionary(),
                "dictadd" => EmitDictAdd(args[0], args[1], args[2]),
                "dictget" => EmitDictGet(args[0], args[1]),
                "dictset" => EmitDictSet(args[0], args[1], args[2]),
                "dictremove" => EmitDictRemove(args[0], args[1]),
                "dictcount" => EmitDictCount(args[0]),
                "dictcontainskey" => EmitDictContainsKey(args[0], args[1]),
                "dictclear" => EmitDictClear(args[0]),

                // HashSet operations
                "createhashset" => EmitCreateHashSet(),
                "setadd" => EmitSetAdd(args[0], args[1]),
                "setremove" => EmitSetRemove(args[0], args[1]),
                "setcontains" => EmitSetContains(args[0], args[1]),
                "setcount" => EmitSetCount(args[0]),
                "setclear" => EmitSetClear(args[0]),

                _ => null
            };
        }

        // List operations using System.Collections.Generic.List<object>
        public string EmitCreateList() => "newobj instance void class [mscorlib]System.Collections.Generic.List`1<object>::.ctor()";
        public string EmitListAdd(string list, string item) => "callvirt instance void class [mscorlib]System.Collections.Generic.List`1<object>::Add(!0)";
        public string EmitListGet(string list, string index) => "callvirt instance !0 class [mscorlib]System.Collections.Generic.List`1<object>::get_Item(int32)";
        public string EmitListSet(string list, string index, string value) => "callvirt instance void class [mscorlib]System.Collections.Generic.List`1<object>::set_Item(int32, !0)";
        public string EmitListRemove(string list, string item) => "callvirt instance bool class [mscorlib]System.Collections.Generic.List`1<object>::Remove(!0)";
        public string EmitListRemoveAt(string list, string index) => "callvirt instance void class [mscorlib]System.Collections.Generic.List`1<object>::RemoveAt(int32)";
        public string EmitListCount(string list) => "callvirt instance int32 class [mscorlib]System.Collections.Generic.List`1<object>::get_Count()";
        public string EmitListContains(string list, string item) => "callvirt instance bool class [mscorlib]System.Collections.Generic.List`1<object>::Contains(!0)";
        public string EmitListIndexOf(string list, string item) => "callvirt instance int32 class [mscorlib]System.Collections.Generic.List`1<object>::IndexOf(!0)";
        public string EmitListClear(string list) => "callvirt instance void class [mscorlib]System.Collections.Generic.List`1<object>::Clear()";
        public string EmitListInsert(string list, string index, string item) => "callvirt instance void class [mscorlib]System.Collections.Generic.List`1<object>::Insert(int32, !0)";

        // Dictionary operations using System.Collections.Generic.Dictionary<object, object>
        public string EmitCreateDictionary() => "newobj instance void class [mscorlib]System.Collections.Generic.Dictionary`2<object,object>::.ctor()";
        public string EmitDictAdd(string dict, string key, string value) => "callvirt instance void class [mscorlib]System.Collections.Generic.Dictionary`2<object,object>::Add(!0, !1)";
        public string EmitDictGet(string dict, string key) => "callvirt instance !1 class [mscorlib]System.Collections.Generic.Dictionary`2<object,object>::get_Item(!0)";
        public string EmitDictSet(string dict, string key, string value) => "callvirt instance void class [mscorlib]System.Collections.Generic.Dictionary`2<object,object>::set_Item(!0, !1)";
        public string EmitDictRemove(string dict, string key) => "callvirt instance bool class [mscorlib]System.Collections.Generic.Dictionary`2<object,object>::Remove(!0)";
        public string EmitDictCount(string dict) => "callvirt instance int32 class [mscorlib]System.Collections.Generic.Dictionary`2<object,object>::get_Count()";
        public string EmitDictContainsKey(string dict, string key) => "callvirt instance bool class [mscorlib]System.Collections.Generic.Dictionary`2<object,object>::ContainsKey(!0)";
        public string EmitDictClear(string dict) => "callvirt instance void class [mscorlib]System.Collections.Generic.Dictionary`2<object,object>::Clear()";

        // HashSet operations using System.Collections.Generic.HashSet<object>
        public string EmitCreateHashSet() => "newobj instance void class [mscorlib]System.Collections.Generic.HashSet`1<object>::.ctor()";
        public string EmitSetAdd(string set, string item) => "callvirt instance bool class [mscorlib]System.Collections.Generic.HashSet`1<object>::Add(!0)";
        public string EmitSetRemove(string set, string item) => "callvirt instance bool class [mscorlib]System.Collections.Generic.HashSet`1<object>::Remove(!0)";
        public string EmitSetContains(string set, string item) => "callvirt instance bool class [mscorlib]System.Collections.Generic.HashSet`1<object>::Contains(!0)";
        public string EmitSetCount(string set) => "callvirt instance int32 class [mscorlib]System.Collections.Generic.HashSet`1<object>::get_Count()";
        public string EmitSetClear(string set) => "callvirt instance void class [mscorlib]System.Collections.Generic.HashSet`1<object>::Clear()";

        #endregion
    }
}
