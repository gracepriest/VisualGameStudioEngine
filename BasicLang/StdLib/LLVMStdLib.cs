using System;
using System.Collections.Generic;

namespace BasicLang.Compiler.StdLib.LLVM
{
    /// <summary>
    /// LLVM IR implementation of standard library functions
    /// Maps BasicLang stdlib to C runtime library calls (printf, scanf, math.h, etc.)
    /// </summary>
    public class LLVMStdLibProvider : IStdLibProvider, IStdIO, IStdString, IStdMath, IStdArray, IStdConversion
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

            // Conversion
            ["CInt"] = new StdLibFunction { Name = "CInt", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Integer" },
            ["CLng"] = new StdLibFunction { Name = "CLng", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Long" },
            ["CDbl"] = new StdLibFunction { Name = "CDbl", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Double" },
            ["CSng"] = new StdLibFunction { Name = "CSng", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Single" },
            ["CStr"] = new StdLibFunction { Name = "CStr", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "String" },
            ["CBool"] = new StdLibFunction { Name = "CBool", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Boolean" },

            // Collections - List operations (using dynamic arrays)
            ["CreateList"] = new StdLibFunction { Name = "CreateList", Category = StdLibCategory.Collections, ParameterTypes = Array.Empty<string>(), ReturnType = "List" },
            ["ListAdd"] = new StdLibFunction { Name = "ListAdd", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Object" }, ReturnType = "Void" },
            ["ListGet"] = new StdLibFunction { Name = "ListGet", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Integer" }, ReturnType = "Object" },
            ["ListSet"] = new StdLibFunction { Name = "ListSet", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Integer", "Object" }, ReturnType = "Void" },
            ["ListRemove"] = new StdLibFunction { Name = "ListRemove", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Object" }, ReturnType = "Boolean" },
            ["ListRemoveAt"] = new StdLibFunction { Name = "ListRemoveAt", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Integer" }, ReturnType = "Void" },
            ["ListCount"] = new StdLibFunction { Name = "ListCount", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List" }, ReturnType = "Integer" },
            ["ListContains"] = new StdLibFunction { Name = "ListContains", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List", "Object" }, ReturnType = "Boolean" },
            ["ListClear"] = new StdLibFunction { Name = "ListClear", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List" }, ReturnType = "Void" },

            // Collections - Dictionary operations (using hash tables)
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
            // LLVM uses external declarations rather than imports
            yield break;
        }

        /// <summary>
        /// Get external function declarations needed for LLVM
        /// </summary>
        public IEnumerable<string> GetExternalDeclarations(string functionName)
        {
            if (!_functions.TryGetValue(functionName, out var func))
                yield break;

            switch (func.Category)
            {
                case StdLibCategory.IO:
                    yield return "declare i32 @printf(i8*, ...)";
                    yield return "declare i32 @puts(i8*)";
                    yield return "declare i32 @scanf(i8*, ...)";
                    yield return "declare i8* @fgets(i8*, i32, i8*)";
                    yield return "declare i8* @stdin";
                    break;
                case StdLibCategory.String:
                    yield return "declare i64 @strlen(i8*)";
                    yield return "declare i8* @strcpy(i8*, i8*)";
                    yield return "declare i8* @strncpy(i8*, i8*, i64)";
                    yield return "declare i8* @strstr(i8*, i8*)";
                    yield return "declare i8* @malloc(i64)";
                    yield return "declare void @free(i8*)";
                    break;
                case StdLibCategory.Math:
                    yield return "declare double @sqrt(double)";
                    yield return "declare double @pow(double, double)";
                    yield return "declare double @sin(double)";
                    yield return "declare double @cos(double)";
                    yield return "declare double @tan(double)";
                    yield return "declare double @log(double)";
                    yield return "declare double @exp(double)";
                    yield return "declare double @floor(double)";
                    yield return "declare double @ceil(double)";
                    yield return "declare double @fabs(double)";
                    yield return "declare double @round(double)";
                    yield return "declare double @fmin(double, double)";
                    yield return "declare double @fmax(double, double)";
                    yield return "declare i32 @rand()";
                    yield return "declare void @srand(i32)";
                    yield return "declare i64 @time(i64*)";
                    break;
                case StdLibCategory.Conversion:
                    yield return "declare i32 @atoi(i8*)";
                    yield return "declare i64 @atol(i8*)";
                    yield return "declare double @atof(i8*)";
                    yield return "declare i32 @sprintf(i8*, i8*, ...)";
                    break;
                case StdLibCategory.Collections:
                    // Runtime library functions for collections
                    yield return "declare i8* @bl_list_create()";
                    yield return "declare void @bl_list_add(i8*, i8*)";
                    yield return "declare i8* @bl_list_get(i8*, i32)";
                    yield return "declare void @bl_list_set(i8*, i32, i8*)";
                    yield return "declare i1 @bl_list_remove(i8*, i8*)";
                    yield return "declare void @bl_list_remove_at(i8*, i32)";
                    yield return "declare i32 @bl_list_count(i8*)";
                    yield return "declare i1 @bl_list_contains(i8*, i8*)";
                    yield return "declare void @bl_list_clear(i8*)";
                    yield return "declare i8* @bl_dict_create()";
                    yield return "declare void @bl_dict_add(i8*, i8*, i8*)";
                    yield return "declare i8* @bl_dict_get(i8*, i8*)";
                    yield return "declare void @bl_dict_set(i8*, i8*, i8*)";
                    yield return "declare i1 @bl_dict_remove(i8*, i8*)";
                    yield return "declare i32 @bl_dict_count(i8*)";
                    yield return "declare i1 @bl_dict_contains_key(i8*, i8*)";
                    yield return "declare void @bl_dict_clear(i8*)";
                    yield return "declare i8* @bl_set_create()";
                    yield return "declare i1 @bl_set_add(i8*, i8*)";
                    yield return "declare i1 @bl_set_remove(i8*, i8*)";
                    yield return "declare i1 @bl_set_contains(i8*, i8*)";
                    yield return "declare i32 @bl_set_count(i8*)";
                    yield return "declare void @bl_set_clear(i8*)";
                    break;
            }
        }

        public string GetInlineImplementation(string functionName)
        {
            // LLVM needs runtime implementations for some functions
            return functionName.ToLower() switch
            {
                "rnd" => @"
define double @bl_rnd() {
  %r = call i32 @rand()
  %d = sitofp i32 %r to double
  %max = sitofp i32 2147483647 to double
  %result = fdiv double %d, %max
  ret double %result
}",
                "randomize" => @"
define void @bl_randomize() {
  %t = call i64 @time(i64* null)
  %t32 = trunc i64 %t to i32
  call void @srand(i32 %t32)
  ret void
}",
                _ => null
            };
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

        // Note: These return LLVM IR instruction templates
        // The actual code generator fills in register names
        public string EmitPrint(string value) => $"call i32 (i8*, ...) @printf(i8* {value})";
        public string EmitPrintLine(string value) => $"call i32 @puts(i8* {value})";
        public string EmitInput(string prompt) => $"/* Input with prompt: {prompt} */";
        public string EmitReadLine() => "/* ReadLine via fgets */";

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
                _ => null
            };
        }

        public string EmitLen(string str) => $"call i64 @strlen(i8* {str})";
        public string EmitMid(string str, string start, string length) => $"/* Mid({str}, {start}, {length}) - needs runtime */";
        public string EmitLeft(string str, string length) => $"/* Left({str}, {length}) - needs runtime */";
        public string EmitRight(string str, string length) => $"/* Right({str}, {length}) - needs runtime */";
        public string EmitUCase(string str) => $"/* UCase - needs runtime */";
        public string EmitLCase(string str) => $"/* LCase - needs runtime */";
        public string EmitTrim(string str) => $"/* Trim - needs runtime */";
        public string EmitInStr(string str, string search) => $"call i8* @strstr(i8* {str}, i8* {search})";
        public string EmitReplace(string str, string find, string replaceWith) => $"/* Replace - needs runtime */";

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

        public string EmitAbs(string value) => $"call double @fabs(double {value})";
        public string EmitSqrt(string value) => $"call double @sqrt(double {value})";
        public string EmitPow(string baseVal, string exponent) => $"call double @pow(double {baseVal}, double {exponent})";
        public string EmitSin(string value) => $"call double @sin(double {value})";
        public string EmitCos(string value) => $"call double @cos(double {value})";
        public string EmitTan(string value) => $"call double @tan(double {value})";
        public string EmitLog(string value) => $"call double @log(double {value})";
        public string EmitExp(string value) => $"call double @exp(double {value})";
        public string EmitFloor(string value) => $"call double @floor(double {value})";
        public string EmitCeiling(string value) => $"call double @ceil(double {value})";
        public string EmitRound(string value) => $"call double @round(double {value})";
        public string EmitMin(string a, string b) => $"call double @fmin(double {a}, double {b})";
        public string EmitMax(string a, string b) => $"call double @fmax(double {a}, double {b})";
        public string EmitRnd() => "call double @bl_rnd()";
        public string EmitRandomize() => "call void @bl_randomize()";

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

        public string EmitUBound(string array) => $"/* UBound({array}) - needs array size tracking */";
        public string EmitLBound(string array) => "0";
        public string EmitUBoundDim(string array, string dimension) => $"/* UBound({array}, {dimension}) */";
        public string EmitLBoundDim(string array, string dimension) => "0";
        public string EmitLength(string array) => $"/* Length({array}) - needs array size tracking */";
        public string EmitReDim(string array, string newSize) => $"/* ReDim - needs realloc */";

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
                _ => null
            };
        }

        public string EmitCInt(string value) => $"fptosi double {value} to i32";
        public string EmitCLng(string value) => $"fptosi double {value} to i64";
        public string EmitCDbl(string value) => $"sitofp i32 {value} to double";
        public string EmitCSng(string value) => $"sitofp i32 {value} to float";
        public string EmitCStr(string value) => $"/* CStr - needs sprintf */";
        public string EmitCBool(string value) => $"icmp ne i32 {value}, 0";
        public string EmitCChar(string value) => $"trunc i32 {value} to i8";

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
                "listclear" => EmitListClear(args[0]),

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

        // List operations - call runtime library functions
        public string EmitCreateList() => "call i8* @bl_list_create()";
        public string EmitListAdd(string list, string item) => $"call void @bl_list_add(i8* {list}, i8* {item})";
        public string EmitListGet(string list, string index) => $"call i8* @bl_list_get(i8* {list}, i32 {index})";
        public string EmitListSet(string list, string index, string value) => $"call void @bl_list_set(i8* {list}, i32 {index}, i8* {value})";
        public string EmitListRemove(string list, string item) => $"call i1 @bl_list_remove(i8* {list}, i8* {item})";
        public string EmitListRemoveAt(string list, string index) => $"call void @bl_list_remove_at(i8* {list}, i32 {index})";
        public string EmitListCount(string list) => $"call i32 @bl_list_count(i8* {list})";
        public string EmitListContains(string list, string item) => $"call i1 @bl_list_contains(i8* {list}, i8* {item})";
        public string EmitListClear(string list) => $"call void @bl_list_clear(i8* {list})";

        // Dictionary operations
        public string EmitCreateDictionary() => "call i8* @bl_dict_create()";
        public string EmitDictAdd(string dict, string key, string value) => $"call void @bl_dict_add(i8* {dict}, i8* {key}, i8* {value})";
        public string EmitDictGet(string dict, string key) => $"call i8* @bl_dict_get(i8* {dict}, i8* {key})";
        public string EmitDictSet(string dict, string key, string value) => $"call void @bl_dict_set(i8* {dict}, i8* {key}, i8* {value})";
        public string EmitDictRemove(string dict, string key) => $"call i1 @bl_dict_remove(i8* {dict}, i8* {key})";
        public string EmitDictCount(string dict) => $"call i32 @bl_dict_count(i8* {dict})";
        public string EmitDictContainsKey(string dict, string key) => $"call i1 @bl_dict_contains_key(i8* {dict}, i8* {key})";
        public string EmitDictClear(string dict) => $"call void @bl_dict_clear(i8* {dict})";

        // HashSet operations
        public string EmitCreateHashSet() => "call i8* @bl_set_create()";
        public string EmitSetAdd(string set, string item) => $"call i1 @bl_set_add(i8* {set}, i8* {item})";
        public string EmitSetRemove(string set, string item) => $"call i1 @bl_set_remove(i8* {set}, i8* {item})";
        public string EmitSetContains(string set, string item) => $"call i1 @bl_set_contains(i8* {set}, i8* {item})";
        public string EmitSetCount(string set) => $"call i32 @bl_set_count(i8* {set})";
        public string EmitSetClear(string set) => $"call void @bl_set_clear(i8* {set})";

        #endregion
    }
}
