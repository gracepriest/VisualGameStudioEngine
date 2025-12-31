using System;
using System.Collections.Generic;

namespace BasicLang.Compiler.StdLib.Cpp
{
    /// <summary>
    /// C++ implementation of standard library functions
    /// Maps BasicLang stdlib to C++ STL and standard library calls
    /// </summary>
    public class CppStdLibProvider : IStdLibProvider, IStdIO, IStdString, IStdMath, IStdArray, IStdConversion
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

            // Conversion
            ["CInt"] = new StdLibFunction { Name = "CInt", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Integer" },
            ["CLng"] = new StdLibFunction { Name = "CLng", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Long" },
            ["CDbl"] = new StdLibFunction { Name = "CDbl", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Double" },
            ["CSng"] = new StdLibFunction { Name = "CSng", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Single" },
            ["CStr"] = new StdLibFunction { Name = "CStr", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "String" },
            ["CBool"] = new StdLibFunction { Name = "CBool", Category = StdLibCategory.Conversion, ParameterTypes = new[] { "Object" }, ReturnType = "Boolean" },

            // Collections - List operations (std::vector)
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

            // Collections - Dictionary operations (std::unordered_map)
            ["CreateDictionary"] = new StdLibFunction { Name = "CreateDictionary", Category = StdLibCategory.Collections, ParameterTypes = Array.Empty<string>(), ReturnType = "Dictionary" },
            ["DictAdd"] = new StdLibFunction { Name = "DictAdd", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object", "Object" }, ReturnType = "Void" },
            ["DictGet"] = new StdLibFunction { Name = "DictGet", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object" }, ReturnType = "Object" },
            ["DictSet"] = new StdLibFunction { Name = "DictSet", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object", "Object" }, ReturnType = "Void" },
            ["DictRemove"] = new StdLibFunction { Name = "DictRemove", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object" }, ReturnType = "Boolean" },
            ["DictCount"] = new StdLibFunction { Name = "DictCount", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary" }, ReturnType = "Integer" },
            ["DictContainsKey"] = new StdLibFunction { Name = "DictContainsKey", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object" }, ReturnType = "Boolean" },
            ["DictClear"] = new StdLibFunction { Name = "DictClear", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary" }, ReturnType = "Void" },

            // Collections - HashSet operations (std::unordered_set)
            ["CreateHashSet"] = new StdLibFunction { Name = "CreateHashSet", Category = StdLibCategory.Collections, ParameterTypes = Array.Empty<string>(), ReturnType = "HashSet" },
            ["SetAdd"] = new StdLibFunction { Name = "SetAdd", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "Object" }, ReturnType = "Boolean" },
            ["SetRemove"] = new StdLibFunction { Name = "SetRemove", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "Object" }, ReturnType = "Boolean" },
            ["SetContains"] = new StdLibFunction { Name = "SetContains", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "Object" }, ReturnType = "Boolean" },
            ["SetCount"] = new StdLibFunction { Name = "SetCount", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet" }, ReturnType = "Integer" },
            ["SetClear"] = new StdLibFunction { Name = "SetClear", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet" }, ReturnType = "Void" },
        };

        private readonly HashSet<string> _requiredIncludes = new HashSet<string>();

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
            if (!_functions.TryGetValue(functionName, out var func))
                yield break;

            switch (func.Category)
            {
                case StdLibCategory.IO:
                    yield return "<iostream>";
                    break;
                case StdLibCategory.String:
                    yield return "<string>";
                    yield return "<algorithm>";
                    yield return "<cctype>";
                    break;
                case StdLibCategory.Math:
                    yield return "<cmath>";
                    break;
                case StdLibCategory.Array:
                    yield return "<vector>";
                    break;
                case StdLibCategory.Conversion:
                    yield return "<string>";
                    yield return "<sstream>";
                    break;
                case StdLibCategory.Collections:
                    yield return "<vector>";
                    yield return "<unordered_map>";
                    yield return "<unordered_set>";
                    yield return "<algorithm>";
                    break;
            }
        }

        public string GetInlineImplementation(string functionName)
        {
            // Some C++ functions need helper implementations
            return functionName.ToLower() switch
            {
                "ucase" => @"
inline std::string bl_ucase(const std::string& s) {
    std::string result = s;
    std::transform(result.begin(), result.end(), result.begin(), ::toupper);
    return result;
}",
                "lcase" => @"
inline std::string bl_lcase(const std::string& s) {
    std::string result = s;
    std::transform(result.begin(), result.end(), result.begin(), ::tolower);
    return result;
}",
                "trim" => @"
inline std::string bl_trim(const std::string& s) {
    size_t start = s.find_first_not_of("" \t\n\r"");
    size_t end = s.find_last_not_of("" \t\n\r"");
    return (start == std::string::npos) ? """" : s.substr(start, end - start + 1);
}",
                "replace" => @"
inline std::string bl_replace(std::string s, const std::string& from, const std::string& to) {
    size_t pos = 0;
    while ((pos = s.find(from, pos)) != std::string::npos) {
        s.replace(pos, from.length(), to);
        pos += to.length();
    }
    return s;
}",
                _ => null
            };
        }

        #region I/O Emissions

        private string EmitIOCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "print" => EmitPrint(args[0]),
                "printline" => EmitPrintLine(args[0]),
                "input" => EmitInput(args[0]),
                "readline" => EmitReadLine(),
                _ => null
            };
        }

        public string EmitPrint(string value) => $"std::cout << {value}";
        public string EmitPrintLine(string value) => $"std::cout << {value} << std::endl";
        public string EmitInput(string prompt) => $"(std::cout << {prompt}, [](){{ std::string s; std::getline(std::cin, s); return s; }}())";
        public string EmitReadLine() => "[](){{ std::string s; std::getline(std::cin, s); return s; }}()";

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

        public string EmitLen(string str) => $"static_cast<int32_t>({str}.length())";
        public string EmitMid(string str, string start, string length) => $"{str}.substr({start} - 1, {length})";
        public string EmitLeft(string str, string length) => $"{str}.substr(0, {length})";
        public string EmitRight(string str, string length) => $"{str}.substr({str}.length() - {length})";
        public string EmitUCase(string str) => $"bl_ucase({str})";
        public string EmitLCase(string str) => $"bl_lcase({str})";
        public string EmitTrim(string str) => $"bl_trim({str})";
        public string EmitInStr(string str, string search) => $"static_cast<int32_t>({str}.find({search}) + 1)";
        public string EmitReplace(string str, string find, string replaceWith) => $"bl_replace({str}, {find}, {replaceWith})";

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
                _ => null
            };
        }

        public string EmitAbs(string value) => $"std::abs({value})";
        public string EmitSqrt(string value) => $"std::sqrt({value})";
        public string EmitPow(string baseVal, string exponent) => $"std::pow({baseVal}, {exponent})";
        public string EmitSin(string value) => $"std::sin({value})";
        public string EmitCos(string value) => $"std::cos({value})";
        public string EmitTan(string value) => $"std::tan({value})";
        public string EmitLog(string value) => $"std::log({value})";
        public string EmitExp(string value) => $"std::exp({value})";
        public string EmitFloor(string value) => $"std::floor({value})";
        public string EmitCeiling(string value) => $"std::ceil({value})";
        public string EmitRound(string value) => $"std::round({value})";
        public string EmitMin(string a, string b) => $"std::min({a}, {b})";
        public string EmitMax(string a, string b) => $"std::max({a}, {b})";

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

        // For 1D arrays (std::vector)
        public string EmitUBound(string array) => $"(static_cast<int32_t>({array}.size()) - 1)";
        public string EmitLBound(string array) => "0";

        // For multi-dimensional arrays
        public string EmitUBoundDim(string array, string dimension) => $"(static_cast<int32_t>({array}.size()) - 1)";
        public string EmitLBoundDim(string array, string dimension) => "0";

        public string EmitLength(string array) => $"static_cast<int32_t>({array}.size())";
        public string EmitReDim(string array, string newSize) => $"{array}.resize({newSize})";

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

        public string EmitCInt(string value) => $"static_cast<int32_t>({value})";
        public string EmitCLng(string value) => $"static_cast<int64_t>({value})";
        public string EmitCDbl(string value) => $"static_cast<double>({value})";
        public string EmitCSng(string value) => $"static_cast<float>({value})";
        public string EmitCStr(string value) => $"std::to_string({value})";
        public string EmitCBool(string value) => $"static_cast<bool>({value})";
        public string EmitCChar(string value) => $"static_cast<char>({value})";

        #endregion

        #region Collections Emissions

        private string EmitCollectionsCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                // List operations (std::vector)
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

                // Dictionary operations (std::unordered_map)
                "createdictionary" => EmitCreateDictionary(),
                "dictadd" => EmitDictAdd(args[0], args[1], args[2]),
                "dictget" => EmitDictGet(args[0], args[1]),
                "dictset" => EmitDictSet(args[0], args[1], args[2]),
                "dictremove" => EmitDictRemove(args[0], args[1]),
                "dictcount" => EmitDictCount(args[0]),
                "dictcontainskey" => EmitDictContainsKey(args[0], args[1]),
                "dictclear" => EmitDictClear(args[0]),

                // HashSet operations (std::unordered_set)
                "createhashset" => EmitCreateHashSet(),
                "setadd" => EmitSetAdd(args[0], args[1]),
                "setremove" => EmitSetRemove(args[0], args[1]),
                "setcontains" => EmitSetContains(args[0], args[1]),
                "setcount" => EmitSetCount(args[0]),
                "setclear" => EmitSetClear(args[0]),

                _ => null
            };
        }

        // List operations using std::vector<std::any> or pointer wrapper
        public string EmitCreateList() => "std::vector<std::any>()";
        public string EmitListAdd(string list, string item) => $"{list}.push_back({item})";
        public string EmitListGet(string list, string index) => $"{list}.at({index})";
        public string EmitListSet(string list, string index, string value) => $"{list}[{index}] = {value}";
        public string EmitListRemove(string list, string item) =>
            $"([&]() {{ auto it = std::find({list}.begin(), {list}.end(), {item}); if (it != {list}.end()) {{ {list}.erase(it); return true; }} return false; }})()";
        public string EmitListRemoveAt(string list, string index) => $"{list}.erase({list}.begin() + {index})";
        public string EmitListCount(string list) => $"static_cast<int32_t>({list}.size())";
        public string EmitListContains(string list, string item) =>
            $"(std::find({list}.begin(), {list}.end(), {item}) != {list}.end())";
        public string EmitListIndexOf(string list, string item) =>
            $"static_cast<int32_t>(std::distance({list}.begin(), std::find({list}.begin(), {list}.end(), {item})))";
        public string EmitListClear(string list) => $"{list}.clear()";
        public string EmitListInsert(string list, string index, string item) => $"{list}.insert({list}.begin() + {index}, {item})";

        // Dictionary operations using std::unordered_map
        public string EmitCreateDictionary() => "std::unordered_map<std::any, std::any>()";
        public string EmitDictAdd(string dict, string key, string value) => $"{dict}.insert({{ {key}, {value} }})";
        public string EmitDictGet(string dict, string key) => $"{dict}.at({key})";
        public string EmitDictSet(string dict, string key, string value) => $"{dict}[{key}] = {value}";
        public string EmitDictRemove(string dict, string key) => $"({dict}.erase({key}) > 0)";
        public string EmitDictCount(string dict) => $"static_cast<int32_t>({dict}.size())";
        public string EmitDictContainsKey(string dict, string key) => $"({dict}.find({key}) != {dict}.end())";
        public string EmitDictClear(string dict) => $"{dict}.clear()";

        // HashSet operations using std::unordered_set
        public string EmitCreateHashSet() => "std::unordered_set<std::any>()";
        public string EmitSetAdd(string set, string item) => $"{set}.insert({item}).second";
        public string EmitSetRemove(string set, string item) => $"({set}.erase({item}) > 0)";
        public string EmitSetContains(string set, string item) => $"({set}.find({item}) != {set}.end())";
        public string EmitSetCount(string set) => $"static_cast<int32_t>({set}.size())";
        public string EmitSetClear(string set) => $"{set}.clear()";

        #endregion
    }
}
