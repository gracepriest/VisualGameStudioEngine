using System;
using System.Collections.Generic;

namespace BasicLang.Compiler.StdLib.CSharp
{
    /// <summary>
    /// C# implementation of standard library functions
    /// Maps BasicLang stdlib to .NET BCL calls
    /// </summary>
    public class CSharpStdLibProvider : IStdLibProvider, IStdIO, IStdString, IStdMath, IStdArray, IStdConversion, IStdFileIO, IStdDateTime, IStdCollections, IStdNetworking, IStdJson, IStdRegex, IStdEnvironment, IStdConsole, IStdProcess, IStdCrypto
    {
        private static readonly Dictionary<string, StdLibFunction> _functions = new Dictionary<string, StdLibFunction>(StringComparer.OrdinalIgnoreCase)
        {
            // I/O
            ["Print"] = new StdLibFunction { Name = "Print", Category = StdLibCategory.IO, ParameterTypes = new[] { "Object" }, ReturnType = "Void" },
            ["PrintLine"] = new StdLibFunction { Name = "PrintLine", Category = StdLibCategory.IO, ParameterTypes = new[] { "Object" }, ReturnType = "Void" },
            ["Input"] = new StdLibFunction { Name = "Input", Category = StdLibCategory.IO, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["ReadLine"] = new StdLibFunction { Name = "ReadLine", Category = StdLibCategory.IO, ParameterTypes = Array.Empty<string>(), ReturnType = "String" },

            // File I/O - Simple operations
            ["FileRead"] = new StdLibFunction { Name = "FileRead", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["FileWrite"] = new StdLibFunction { Name = "FileWrite", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String", "String" }, ReturnType = "Void" },
            ["FileAppend"] = new StdLibFunction { Name = "FileAppend", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String", "String" }, ReturnType = "Void" },
            ["FileExists"] = new StdLibFunction { Name = "FileExists", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "Boolean" },
            ["FileDelete"] = new StdLibFunction { Name = "FileDelete", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "Void" },
            ["FileCopy"] = new StdLibFunction { Name = "FileCopy", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String", "String" }, ReturnType = "Void" },
            ["FileMove"] = new StdLibFunction { Name = "FileMove", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String", "String" }, ReturnType = "Void" },
            ["FileLen"] = new StdLibFunction { Name = "FileLen", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "Long" },

            // File I/O - Handle-based operations
            ["FileOpen"] = new StdLibFunction { Name = "FileOpen", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String", "String" }, ReturnType = "Integer" },
            ["FileClose"] = new StdLibFunction { Name = "FileClose", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["FileReadLine"] = new StdLibFunction { Name = "FileReadLine", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "Integer" }, ReturnType = "String" },
            ["FileReadAll"] = new StdLibFunction { Name = "FileReadAll", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "Integer" }, ReturnType = "String" },
            ["FileWriteLine"] = new StdLibFunction { Name = "FileWriteLine", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "Integer", "String" }, ReturnType = "Void" },
            ["FileEof"] = new StdLibFunction { Name = "FileEof", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "Integer" }, ReturnType = "Boolean" },
            ["FileSeek"] = new StdLibFunction { Name = "FileSeek", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "Integer", "Long" }, ReturnType = "Void" },
            ["FileTell"] = new StdLibFunction { Name = "FileTell", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "Integer" }, ReturnType = "Long" },
            ["GetCurrentDir"] = new StdLibFunction { Name = "GetCurrentDir", Category = StdLibCategory.FileIO, ParameterTypes = Array.Empty<string>(), ReturnType = "String" },
            ["SetCurrentDir"] = new StdLibFunction { Name = "SetCurrentDir", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "Void" },
            ["DirExists"] = new StdLibFunction { Name = "DirExists", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "Boolean" },
            ["DirCreate"] = new StdLibFunction { Name = "DirCreate", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "Void" },
            ["DirDelete"] = new StdLibFunction { Name = "DirDelete", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "Void" },
            ["DirGetFiles"] = new StdLibFunction { Name = "DirGetFiles", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "String[]" },
            ["DirGetDirs"] = new StdLibFunction { Name = "DirGetDirs", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "String[]" },
            ["PathCombine"] = new StdLibFunction { Name = "PathCombine", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String", "String" }, ReturnType = "String" },
            ["PathGetFileName"] = new StdLibFunction { Name = "PathGetFileName", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["PathGetDirectory"] = new StdLibFunction { Name = "PathGetDirectory", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["PathGetExtension"] = new StdLibFunction { Name = "PathGetExtension", Category = StdLibCategory.FileIO, ParameterTypes = new[] { "String" }, ReturnType = "String" },

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
            ["Split"] = new StdLibFunction { Name = "Split", Category = StdLibCategory.String, ParameterTypes = new[] { "String", "String" }, ReturnType = "String[]" },
            ["Join"] = new StdLibFunction { Name = "Join", Category = StdLibCategory.String, ParameterTypes = new[] { "String[]", "String" }, ReturnType = "String" },
            ["Chr"] = new StdLibFunction { Name = "Chr", Category = StdLibCategory.String, ParameterTypes = new[] { "Integer" }, ReturnType = "String" },
            ["Asc"] = new StdLibFunction { Name = "Asc", Category = StdLibCategory.String, ParameterTypes = new[] { "String" }, ReturnType = "Integer" },

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

            // DateTime
            ["Now"] = new StdLibFunction { Name = "Now", Category = StdLibCategory.DateTime, ParameterTypes = Array.Empty<string>(), ReturnType = "DateTime" },
            ["Today"] = new StdLibFunction { Name = "Today", Category = StdLibCategory.DateTime, ParameterTypes = Array.Empty<string>(), ReturnType = "DateTime" },
            ["Year"] = new StdLibFunction { Name = "Year", Category = StdLibCategory.DateTime, ParameterTypes = new[] { "DateTime" }, ReturnType = "Integer" },
            ["Month"] = new StdLibFunction { Name = "Month", Category = StdLibCategory.DateTime, ParameterTypes = new[] { "DateTime" }, ReturnType = "Integer" },
            ["Day"] = new StdLibFunction { Name = "Day", Category = StdLibCategory.DateTime, ParameterTypes = new[] { "DateTime" }, ReturnType = "Integer" },
            ["Hour"] = new StdLibFunction { Name = "Hour", Category = StdLibCategory.DateTime, ParameterTypes = new[] { "DateTime" }, ReturnType = "Integer" },
            ["Minute"] = new StdLibFunction { Name = "Minute", Category = StdLibCategory.DateTime, ParameterTypes = new[] { "DateTime" }, ReturnType = "Integer" },
            ["Second"] = new StdLibFunction { Name = "Second", Category = StdLibCategory.DateTime, ParameterTypes = new[] { "DateTime" }, ReturnType = "Integer" },
            ["DateAdd"] = new StdLibFunction { Name = "DateAdd", Category = StdLibCategory.DateTime, ParameterTypes = new[] { "DateTime", "String", "Integer" }, ReturnType = "DateTime" },
            ["DateDiff"] = new StdLibFunction { Name = "DateDiff", Category = StdLibCategory.DateTime, ParameterTypes = new[] { "DateTime", "DateTime", "String" }, ReturnType = "Integer" },
            ["FormatDate"] = new StdLibFunction { Name = "FormatDate", Category = StdLibCategory.DateTime, ParameterTypes = new[] { "DateTime", "String" }, ReturnType = "String" },

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
            ["ListToArray"] = new StdLibFunction { Name = "ListToArray", Category = StdLibCategory.Collections, ParameterTypes = new[] { "List" }, ReturnType = "Array" },

            // Collections - Dictionary operations
            ["CreateDictionary"] = new StdLibFunction { Name = "CreateDictionary", Category = StdLibCategory.Collections, ParameterTypes = Array.Empty<string>(), ReturnType = "Dictionary" },
            ["DictAdd"] = new StdLibFunction { Name = "DictAdd", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object", "Object" }, ReturnType = "Void" },
            ["DictGet"] = new StdLibFunction { Name = "DictGet", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object" }, ReturnType = "Object" },
            ["DictSet"] = new StdLibFunction { Name = "DictSet", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object", "Object" }, ReturnType = "Void" },
            ["DictRemove"] = new StdLibFunction { Name = "DictRemove", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object" }, ReturnType = "Boolean" },
            ["DictCount"] = new StdLibFunction { Name = "DictCount", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary" }, ReturnType = "Integer" },
            ["DictContainsKey"] = new StdLibFunction { Name = "DictContainsKey", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object" }, ReturnType = "Boolean" },
            ["DictContainsValue"] = new StdLibFunction { Name = "DictContainsValue", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary", "Object" }, ReturnType = "Boolean" },
            ["DictKeys"] = new StdLibFunction { Name = "DictKeys", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary" }, ReturnType = "Array" },
            ["DictValues"] = new StdLibFunction { Name = "DictValues", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary" }, ReturnType = "Array" },
            ["DictClear"] = new StdLibFunction { Name = "DictClear", Category = StdLibCategory.Collections, ParameterTypes = new[] { "Dictionary" }, ReturnType = "Void" },

            // Collections - HashSet operations
            ["CreateHashSet"] = new StdLibFunction { Name = "CreateHashSet", Category = StdLibCategory.Collections, ParameterTypes = Array.Empty<string>(), ReturnType = "HashSet" },
            ["SetAdd"] = new StdLibFunction { Name = "SetAdd", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "Object" }, ReturnType = "Boolean" },
            ["SetRemove"] = new StdLibFunction { Name = "SetRemove", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "Object" }, ReturnType = "Boolean" },
            ["SetContains"] = new StdLibFunction { Name = "SetContains", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "Object" }, ReturnType = "Boolean" },
            ["SetCount"] = new StdLibFunction { Name = "SetCount", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet" }, ReturnType = "Integer" },
            ["SetClear"] = new StdLibFunction { Name = "SetClear", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet" }, ReturnType = "Void" },
            ["SetUnion"] = new StdLibFunction { Name = "SetUnion", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "HashSet" }, ReturnType = "HashSet" },
            ["SetIntersect"] = new StdLibFunction { Name = "SetIntersect", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "HashSet" }, ReturnType = "HashSet" },
            ["SetExcept"] = new StdLibFunction { Name = "SetExcept", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet", "HashSet" }, ReturnType = "HashSet" },
            ["SetToArray"] = new StdLibFunction { Name = "SetToArray", Category = StdLibCategory.Collections, ParameterTypes = new[] { "HashSet" }, ReturnType = "Array" },

            // Networking - TCP Client
            ["TcpConnect"] = new StdLibFunction { Name = "TcpConnect", Category = StdLibCategory.Networking, ParameterTypes = new[] { "String", "Integer" }, ReturnType = "TcpClient" },
            ["TcpSend"] = new StdLibFunction { Name = "TcpSend", Category = StdLibCategory.Networking, ParameterTypes = new[] { "TcpClient", "String" }, ReturnType = "Void" },
            ["TcpReceive"] = new StdLibFunction { Name = "TcpReceive", Category = StdLibCategory.Networking, ParameterTypes = new[] { "TcpClient", "Integer" }, ReturnType = "String" },
            ["TcpReceiveLine"] = new StdLibFunction { Name = "TcpReceiveLine", Category = StdLibCategory.Networking, ParameterTypes = new[] { "TcpClient" }, ReturnType = "String" },
            ["TcpClose"] = new StdLibFunction { Name = "TcpClose", Category = StdLibCategory.Networking, ParameterTypes = new[] { "TcpClient" }, ReturnType = "Void" },
            ["TcpIsConnected"] = new StdLibFunction { Name = "TcpIsConnected", Category = StdLibCategory.Networking, ParameterTypes = new[] { "TcpClient" }, ReturnType = "Boolean" },

            // Networking - TCP Server
            ["TcpListen"] = new StdLibFunction { Name = "TcpListen", Category = StdLibCategory.Networking, ParameterTypes = new[] { "Integer" }, ReturnType = "TcpListener" },
            ["TcpAccept"] = new StdLibFunction { Name = "TcpAccept", Category = StdLibCategory.Networking, ParameterTypes = new[] { "TcpListener" }, ReturnType = "TcpClient" },
            ["TcpStopListener"] = new StdLibFunction { Name = "TcpStopListener", Category = StdLibCategory.Networking, ParameterTypes = new[] { "TcpListener" }, ReturnType = "Void" },

            // Networking - UDP
            ["UdpCreate"] = new StdLibFunction { Name = "UdpCreate", Category = StdLibCategory.Networking, ParameterTypes = Array.Empty<string>(), ReturnType = "UdpClient" },
            ["UdpBind"] = new StdLibFunction { Name = "UdpBind", Category = StdLibCategory.Networking, ParameterTypes = new[] { "UdpClient", "Integer" }, ReturnType = "Void" },
            ["UdpSend"] = new StdLibFunction { Name = "UdpSend", Category = StdLibCategory.Networking, ParameterTypes = new[] { "UdpClient", "String", "Integer", "String" }, ReturnType = "Void" },
            ["UdpReceive"] = new StdLibFunction { Name = "UdpReceive", Category = StdLibCategory.Networking, ParameterTypes = new[] { "UdpClient" }, ReturnType = "String" },
            ["UdpClose"] = new StdLibFunction { Name = "UdpClose", Category = StdLibCategory.Networking, ParameterTypes = new[] { "UdpClient" }, ReturnType = "Void" },

            // Networking - HTTP
            ["HttpGet"] = new StdLibFunction { Name = "HttpGet", Category = StdLibCategory.Networking, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["HttpPost"] = new StdLibFunction { Name = "HttpPost", Category = StdLibCategory.Networking, ParameterTypes = new[] { "String", "String", "String" }, ReturnType = "String" },
            ["HttpDownload"] = new StdLibFunction { Name = "HttpDownload", Category = StdLibCategory.Networking, ParameterTypes = new[] { "String", "String" }, ReturnType = "Boolean" },

            // JSON
            ["JsonParse"] = new StdLibFunction { Name = "JsonParse", Category = StdLibCategory.Json, ParameterTypes = new[] { "String" }, ReturnType = "Object" },
            ["JsonStringify"] = new StdLibFunction { Name = "JsonStringify", Category = StdLibCategory.Json, ParameterTypes = new[] { "Object" }, ReturnType = "String" },
            ["JsonGet"] = new StdLibFunction { Name = "JsonGet", Category = StdLibCategory.Json, ParameterTypes = new[] { "Object", "String" }, ReturnType = "Object" },
            ["JsonSet"] = new StdLibFunction { Name = "JsonSet", Category = StdLibCategory.Json, ParameterTypes = new[] { "Object", "String", "Object" }, ReturnType = "Object" },
            ["JsonIsValid"] = new StdLibFunction { Name = "JsonIsValid", Category = StdLibCategory.Json, ParameterTypes = new[] { "String" }, ReturnType = "Boolean" },

            // Regex
            ["RegexMatch"] = new StdLibFunction { Name = "RegexMatch", Category = StdLibCategory.Regex, ParameterTypes = new[] { "String", "String" }, ReturnType = "String" },
            ["RegexMatches"] = new StdLibFunction { Name = "RegexMatches", Category = StdLibCategory.Regex, ParameterTypes = new[] { "String", "String" }, ReturnType = "String[]" },
            ["RegexReplace"] = new StdLibFunction { Name = "RegexReplace", Category = StdLibCategory.Regex, ParameterTypes = new[] { "String", "String", "String" }, ReturnType = "String" },
            ["RegexSplit"] = new StdLibFunction { Name = "RegexSplit", Category = StdLibCategory.Regex, ParameterTypes = new[] { "String", "String" }, ReturnType = "String[]" },
            ["IsMatch"] = new StdLibFunction { Name = "IsMatch", Category = StdLibCategory.Regex, ParameterTypes = new[] { "String", "String" }, ReturnType = "Boolean" },

            // Environment
            ["GetEnv"] = new StdLibFunction { Name = "GetEnv", Category = StdLibCategory.Environment, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["SetEnv"] = new StdLibFunction { Name = "SetEnv", Category = StdLibCategory.Environment, ParameterTypes = new[] { "String", "String" }, ReturnType = "Void" },
            ["GetArgs"] = new StdLibFunction { Name = "GetArgs", Category = StdLibCategory.Environment, ParameterTypes = Array.Empty<string>(), ReturnType = "String[]" },
            ["GetExePath"] = new StdLibFunction { Name = "GetExePath", Category = StdLibCategory.Environment, ParameterTypes = Array.Empty<string>(), ReturnType = "String" },
            ["GetMachineName"] = new StdLibFunction { Name = "GetMachineName", Category = StdLibCategory.Environment, ParameterTypes = Array.Empty<string>(), ReturnType = "String" },
            ["GetUserName"] = new StdLibFunction { Name = "GetUserName", Category = StdLibCategory.Environment, ParameterTypes = Array.Empty<string>(), ReturnType = "String" },
            ["GetOSVersion"] = new StdLibFunction { Name = "GetOSVersion", Category = StdLibCategory.Environment, ParameterTypes = Array.Empty<string>(), ReturnType = "String" },
            ["GetTempPath"] = new StdLibFunction { Name = "GetTempPath", Category = StdLibCategory.Environment, ParameterTypes = Array.Empty<string>(), ReturnType = "String" },
            ["Exit"] = new StdLibFunction { Name = "Exit", Category = StdLibCategory.Environment, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },

            // Console
            ["Cls"] = new StdLibFunction { Name = "Cls", Category = StdLibCategory.Console, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["ClearScreen"] = new StdLibFunction { Name = "ClearScreen", Category = StdLibCategory.Console, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["SetForeColor"] = new StdLibFunction { Name = "SetForeColor", Category = StdLibCategory.Console, ParameterTypes = new[] { "String" }, ReturnType = "Void" },
            ["SetBackColor"] = new StdLibFunction { Name = "SetBackColor", Category = StdLibCategory.Console, ParameterTypes = new[] { "String" }, ReturnType = "Void" },
            ["ResetColor"] = new StdLibFunction { Name = "ResetColor", Category = StdLibCategory.Console, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["SetCursorPos"] = new StdLibFunction { Name = "SetCursorPos", Category = StdLibCategory.Console, ParameterTypes = new[] { "Integer", "Integer" }, ReturnType = "Void" },
            ["Beep"] = new StdLibFunction { Name = "Beep", Category = StdLibCategory.Console, ParameterTypes = Array.Empty<string>(), ReturnType = "Void" },
            ["ReadKey"] = new StdLibFunction { Name = "ReadKey", Category = StdLibCategory.Console, ParameterTypes = Array.Empty<string>(), ReturnType = "String" },
            ["KeyAvailable"] = new StdLibFunction { Name = "KeyAvailable", Category = StdLibCategory.Console, ParameterTypes = Array.Empty<string>(), ReturnType = "Boolean" },
            ["SetTitle"] = new StdLibFunction { Name = "SetTitle", Category = StdLibCategory.Console, ParameterTypes = new[] { "String" }, ReturnType = "Void" },

            // Process
            ["Shell"] = new StdLibFunction { Name = "Shell", Category = StdLibCategory.Process, ParameterTypes = new[] { "String" }, ReturnType = "Integer" },
            ["Run"] = new StdLibFunction { Name = "Run", Category = StdLibCategory.Process, ParameterTypes = new[] { "String", "String" }, ReturnType = "Integer" },
            ["RunHidden"] = new StdLibFunction { Name = "RunHidden", Category = StdLibCategory.Process, ParameterTypes = new[] { "String", "String" }, ReturnType = "String" },
            ["KillProcess"] = new StdLibFunction { Name = "KillProcess", Category = StdLibCategory.Process, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },
            ["GetProcessId"] = new StdLibFunction { Name = "GetProcessId", Category = StdLibCategory.Process, ParameterTypes = Array.Empty<string>(), ReturnType = "Integer" },
            ["Sleep"] = new StdLibFunction { Name = "Sleep", Category = StdLibCategory.Process, ParameterTypes = new[] { "Integer" }, ReturnType = "Void" },

            // Crypto
            ["MD5"] = new StdLibFunction { Name = "MD5", Category = StdLibCategory.Crypto, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["SHA1"] = new StdLibFunction { Name = "SHA1", Category = StdLibCategory.Crypto, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["SHA256"] = new StdLibFunction { Name = "SHA256", Category = StdLibCategory.Crypto, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["Base64Encode"] = new StdLibFunction { Name = "Base64Encode", Category = StdLibCategory.Crypto, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["Base64Decode"] = new StdLibFunction { Name = "Base64Decode", Category = StdLibCategory.Crypto, ParameterTypes = new[] { "String" }, ReturnType = "String" },
            ["NewGuid"] = new StdLibFunction { Name = "NewGuid", Category = StdLibCategory.Crypto, ParameterTypes = Array.Empty<string>(), ReturnType = "String" },
        };

        public bool CanHandle(string functionName) => _functions.ContainsKey(functionName);

        public string EmitCall(string functionName, string[] arguments)
        {
            if (!_functions.TryGetValue(functionName, out var func))
                return null;

            return func.Category switch
            {
                StdLibCategory.IO => EmitIOCall(functionName, arguments),
                StdLibCategory.FileIO => EmitFileIOCall(functionName, arguments),
                StdLibCategory.String => EmitStringCall(functionName, arguments),
                StdLibCategory.Math => EmitMathCall(functionName, arguments),
                StdLibCategory.Array => EmitArrayCall(functionName, arguments),
                StdLibCategory.Conversion => EmitConversionCall(functionName, arguments),
                StdLibCategory.DateTime => EmitDateTimeCall(functionName, arguments),
                StdLibCategory.Collections => EmitCollectionsCall(functionName, arguments),
                StdLibCategory.Networking => EmitNetworkingCall(functionName, arguments),
                StdLibCategory.Json => EmitJsonCall(functionName, arguments),
                StdLibCategory.Regex => EmitRegexCall(functionName, arguments),
                StdLibCategory.Environment => EmitEnvironmentCall(functionName, arguments),
                StdLibCategory.Console => EmitConsoleCall(functionName, arguments),
                StdLibCategory.Process => EmitProcessCall(functionName, arguments),
                StdLibCategory.Crypto => EmitCryptoCall(functionName, arguments),
                _ => null
            };
        }

        public IEnumerable<string> GetRequiredImports(string functionName)
        {
            if (!_functions.TryGetValue(functionName, out var func))
                yield break;

            yield return "System";

            if (func.Category == StdLibCategory.Math)
                yield return "System.Math";

            if (func.Category == StdLibCategory.FileIO)
                yield return "System.IO";

            if (func.Category == StdLibCategory.Collections)
            {
                yield return "System.Collections.Generic";
                yield return "System.Linq";
            }

            if (func.Category == StdLibCategory.Networking)
            {
                yield return "System.Net";
                yield return "System.Net.Sockets";
                yield return "System.Net.Http";
            }

            if (func.Category == StdLibCategory.Json)
            {
                yield return "System.Text.Json";
                yield return "System.Text.Json.Nodes";
            }

            if (func.Category == StdLibCategory.Regex)
            {
                yield return "System.Text.RegularExpressions";
            }

            if (func.Category == StdLibCategory.Crypto)
            {
                yield return "System.Security.Cryptography";
            }

            if (func.Category == StdLibCategory.Process)
            {
                yield return "System.Diagnostics";
            }
        }

        public string GetInlineImplementation(string functionName)
        {
            // C# doesn't need inline implementations - just uses BCL
            return null;
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

        public string EmitPrint(string value) => $"Console.Write({value})";
        public string EmitPrintLine(string value) => $"Console.WriteLine({value})";
        public string EmitInput(string prompt) => $"new Func<string>(() => {{ Console.Write({prompt}); return Console.ReadLine(); }})()";
        public string EmitReadLine() => "Console.ReadLine()";

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
                "split" => EmitSplit(args[0], args[1]),
                "join" => EmitJoin(args[0], args[1]),
                "chr" => EmitChr(args[0]),
                "asc" => EmitAsc(args[0]),
                _ => null
            };
        }

        public string EmitLen(string str) => $"{str}.Length";
        public string EmitMid(string str, string start, string length) => $"{str}.Substring({start} - 1, {length})";
        public string EmitLeft(string str, string length) => $"{str}.Substring(0, {length})";
        public string EmitRight(string str, string length) => $"{str}.Substring({str}.Length - {length})";
        public string EmitUCase(string str) => $"{str}.ToUpper()";
        public string EmitLCase(string str) => $"{str}.ToLower()";
        public string EmitTrim(string str) => $"{str}.Trim()";
        public string EmitInStr(string str, string search) => $"({str}.IndexOf({search}) + 1)";
        public string EmitReplace(string str, string find, string replaceWith) => $"{str}.Replace({find}, {replaceWith})";
        public string EmitSplit(string str, string delimiter) => $"{str}.Split({delimiter})";
        public string EmitJoin(string array, string delimiter) => $"string.Join({delimiter}, {array})";
        public string EmitChr(string code) => $"((char){code}).ToString()";
        public string EmitAsc(string str) => $"(int){str}[0]";

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

        public string EmitAbs(string value) => $"Math.Abs({value})";
        public string EmitSqrt(string value) => $"Math.Sqrt({value})";
        public string EmitPow(string baseVal, string exponent) => $"Math.Pow({baseVal}, {exponent})";
        public string EmitSin(string value) => $"Math.Sin({value})";
        public string EmitCos(string value) => $"Math.Cos({value})";
        public string EmitTan(string value) => $"Math.Tan({value})";
        public string EmitLog(string value) => $"Math.Log({value})";
        public string EmitExp(string value) => $"Math.Exp({value})";
        public string EmitFloor(string value) => $"Math.Floor({value})";
        public string EmitCeiling(string value) => $"Math.Ceiling({value})";
        public string EmitRound(string value) => $"Math.Round({value})";
        public string EmitMin(string a, string b) => $"Math.Min({a}, {b})";
        public string EmitMax(string a, string b) => $"Math.Max({a}, {b})";
        public string EmitRnd() => "Random.Shared.NextDouble()";
        public string EmitRandomize() => "/* Randomize - no-op in .NET */";

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

        // For 1D arrays
        public string EmitUBound(string array) => $"({array}.Length - 1)";
        public string EmitLBound(string array) => "0";

        // For multi-dimensional arrays
        public string EmitUBoundDim(string array, string dimension) => $"({array}.GetUpperBound({dimension}))";
        public string EmitLBoundDim(string array, string dimension) => $"({array}.GetLowerBound({dimension}))";

        public string EmitLength(string array) => $"{array}.Length";
        public string EmitReDim(string array, string newSize) => $"Array.Resize(ref {array}, {newSize})";

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

        public string EmitCInt(string value) => $"Convert.ToInt32({value})";
        public string EmitCLng(string value) => $"Convert.ToInt64({value})";
        public string EmitCDbl(string value) => $"Convert.ToDouble({value})";
        public string EmitCSng(string value) => $"Convert.ToSingle({value})";
        public string EmitCStr(string value) => $"Convert.ToString({value})";
        public string EmitCBool(string value) => $"Convert.ToBoolean({value})";
        public string EmitCChar(string value) => $"Convert.ToChar({value})";

        #endregion

        #region File I/O Emissions

        private string EmitFileIOCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                // Simple file operations
                "fileread" => EmitFileRead(args[0]),
                "filewrite" => EmitFileWrite(args[0], args[1]),
                "fileappend" => EmitFileAppend(args[0], args[1]),
                "fileexists" => EmitFileExists(args[0]),
                "filedelete" => EmitFileDelete(args[0]),
                "filecopy" => EmitFileCopy(args[0], args[1]),
                "filemove" => EmitFileMove(args[0], args[1]),
                "filelen" => EmitFileLen(args[0]),

                // Handle-based file operations
                "fileopen" => EmitFileOpen(args[0], args[1]),
                "fileclose" => EmitFileClose(args[0]),
                "filereadline" => EmitFileReadLine(args[0]),
                "filereadall" => EmitFileReadAllHandle(args[0]),
                "filewriteline" => EmitFileWriteLine(args[0], args[1]),
                "fileeof" => EmitFileEof(args[0]),
                "fileseek" => EmitFileSeek(args[0], args[1]),
                "filetell" => EmitFileTell(args[0]),
                "getcurrentdir" => EmitGetCurrentDir(),
                "setcurrentdir" => EmitSetCurrentDir(args[0]),

                // Directory operations
                "direxists" => EmitDirExists(args[0]),
                "dircreate" => EmitDirCreate(args[0]),
                "dirdelete" => EmitDirDelete(args[0]),
                "dirgetfiles" => EmitDirGetFiles(args[0]),
                "dirgetdirs" => EmitDirGetDirs(args[0]),

                // Path operations
                "pathcombine" => EmitPathCombine(args[0], args[1]),
                "pathgetfilename" => EmitPathGetFileName(args[0]),
                "pathgetdirectory" => EmitPathGetDirectory(args[0]),
                "pathgetextension" => EmitPathGetExtension(args[0]),
                _ => null
            };
        }

        // Simple file operations
        public string EmitFileRead(string path) => $"File.ReadAllText({path})";
        public string EmitFileWrite(string path, string content) => $"File.WriteAllText({path}, {content})";
        public string EmitFileAppend(string path, string content) => $"File.AppendAllText({path}, {content})";
        public string EmitFileExists(string path) => $"File.Exists({path})";
        public string EmitFileDelete(string path) => $"File.Delete({path})";
        public string EmitFileCopy(string source, string dest) => $"File.Copy({source}, {dest}, true)";
        public string EmitFileMove(string source, string dest) => $"File.Move({source}, {dest}, true)";
        public string EmitFileLen(string path) => $"new FileInfo({path}).Length";

        // Handle-based file operations (use runtime file handle manager)
        public string EmitFileOpen(string path, string mode) => $"BasicLangFileIO.Open({path}, {mode})";
        public string EmitFileClose(string handle) => $"BasicLangFileIO.Close({handle})";
        public string EmitFileReadLine(string handle) => $"BasicLangFileIO.ReadLine({handle})";
        public string EmitFileReadAllHandle(string handle) => $"BasicLangFileIO.ReadAll({handle})";
        public string EmitFileWriteLine(string handle, string data) => $"BasicLangFileIO.WriteLine({handle}, {data})";
        public string EmitFileEof(string handle) => $"BasicLangFileIO.IsEof({handle})";
        public string EmitFileSeek(string handle, string position) => $"BasicLangFileIO.Seek({handle}, {position})";
        public string EmitFileTell(string handle) => $"BasicLangFileIO.Tell({handle})";
        public string EmitGetCurrentDir() => "Directory.GetCurrentDirectory()";
        public string EmitSetCurrentDir(string path) => $"Directory.SetCurrentDirectory({path})";

        // Directory operations
        public string EmitDirExists(string path) => $"Directory.Exists({path})";
        public string EmitDirCreate(string path) => $"Directory.CreateDirectory({path})";
        public string EmitDirDelete(string path) => $"Directory.Delete({path}, true)";
        public string EmitDirGetFiles(string path) => $"Directory.GetFiles({path})";
        public string EmitDirGetDirs(string path) => $"Directory.GetDirectories({path})";

        public string EmitPathCombine(string path1, string path2) => $"Path.Combine({path1}, {path2})";
        public string EmitPathGetFileName(string path) => $"Path.GetFileName({path})";
        public string EmitPathGetDirectory(string path) => $"Path.GetDirectoryName({path})";
        public string EmitPathGetExtension(string path) => $"Path.GetExtension({path})";

        #endregion

        #region DateTime Emissions

        private string EmitDateTimeCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "now" => EmitNow(),
                "today" => EmitToday(),
                "year" => EmitYear(args[0]),
                "month" => EmitMonth(args[0]),
                "day" => EmitDay(args[0]),
                "hour" => EmitHour(args[0]),
                "minute" => EmitMinute(args[0]),
                "second" => EmitSecond(args[0]),
                "dateadd" => EmitDateAdd(args[0], args[1], args[2]),
                "datediff" => EmitDateDiff(args[0], args[1], args[2]),
                "formatdate" => EmitFormatDate(args[0], args[1]),
                _ => null
            };
        }

        public string EmitNow() => "DateTime.Now";
        public string EmitToday() => "DateTime.Today";
        public string EmitYear(string date) => $"{date}.Year";
        public string EmitMonth(string date) => $"{date}.Month";
        public string EmitDay(string date) => $"{date}.Day";
        public string EmitHour(string date) => $"{date}.Hour";
        public string EmitMinute(string date) => $"{date}.Minute";
        public string EmitSecond(string date) => $"{date}.Second";
        public string EmitDateAdd(string date, string interval, string number) =>
            $"({interval}.ToLower() switch {{ \"d\" => {date}.AddDays({number}), \"m\" => {date}.AddMonths({number}), \"y\" => {date}.AddYears({number}), \"h\" => {date}.AddHours({number}), \"n\" => {date}.AddMinutes({number}), \"s\" => {date}.AddSeconds({number}), _ => {date} }})";
        public string EmitDateDiff(string date1, string date2, string interval) =>
            $"({interval}.ToLower() switch {{ \"d\" => (int)({date2} - {date1}).TotalDays, \"m\" => (({date2}.Year - {date1}.Year) * 12 + {date2}.Month - {date1}.Month), \"y\" => {date2}.Year - {date1}.Year, \"h\" => (int)({date2} - {date1}).TotalHours, \"n\" => (int)({date2} - {date1}).TotalMinutes, \"s\" => (int)({date2} - {date1}).TotalSeconds, _ => 0 }})";
        public string EmitFormatDate(string date, string format) => $"{date}.ToString({format})";

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
                "listtoarray" => EmitListToArray(args[0]),

                // Dictionary operations
                "createdictionary" => EmitCreateDictionary(),
                "dictadd" => EmitDictAdd(args[0], args[1], args[2]),
                "dictget" => EmitDictGet(args[0], args[1]),
                "dictset" => EmitDictSet(args[0], args[1], args[2]),
                "dictremove" => EmitDictRemove(args[0], args[1]),
                "dictcount" => EmitDictCount(args[0]),
                "dictcontainskey" => EmitDictContainsKey(args[0], args[1]),
                "dictcontainsvalue" => EmitDictContainsValue(args[0], args[1]),
                "dictkeys" => EmitDictKeys(args[0]),
                "dictvalues" => EmitDictValues(args[0]),
                "dictclear" => EmitDictClear(args[0]),

                // HashSet operations
                "createhashset" => EmitCreateHashSet(),
                "setadd" => EmitSetAdd(args[0], args[1]),
                "setremove" => EmitSetRemove(args[0], args[1]),
                "setcontains" => EmitSetContains(args[0], args[1]),
                "setcount" => EmitSetCount(args[0]),
                "setclear" => EmitSetClear(args[0]),
                "setunion" => EmitSetUnion(args[0], args[1]),
                "setintersect" => EmitSetIntersect(args[0], args[1]),
                "setexcept" => EmitSetExcept(args[0], args[1]),
                "settoarray" => EmitSetToArray(args[0]),

                _ => null
            };
        }

        // List operations
        public string EmitCreateList() => "new List<object>()";
        public string EmitListAdd(string list, string item) => $"{list}.Add({item})";
        public string EmitListGet(string list, string index) => $"{list}[{index}]";
        public string EmitListSet(string list, string index, string value) => $"{list}[{index}] = {value}";
        public string EmitListRemove(string list, string item) => $"{list}.Remove({item})";
        public string EmitListRemoveAt(string list, string index) => $"{list}.RemoveAt({index})";
        public string EmitListCount(string list) => $"{list}.Count";
        public string EmitListContains(string list, string item) => $"{list}.Contains({item})";
        public string EmitListIndexOf(string list, string item) => $"{list}.IndexOf({item})";
        public string EmitListClear(string list) => $"{list}.Clear()";
        public string EmitListInsert(string list, string index, string item) => $"{list}.Insert({index}, {item})";
        public string EmitListToArray(string list) => $"{list}.ToArray()";

        // Dictionary operations
        public string EmitCreateDictionary() => "new Dictionary<object, object>()";
        public string EmitDictAdd(string dict, string key, string value) => $"{dict}.Add({key}, {value})";
        public string EmitDictGet(string dict, string key) => $"{dict}[{key}]";
        public string EmitDictSet(string dict, string key, string value) => $"{dict}[{key}] = {value}";
        public string EmitDictRemove(string dict, string key) => $"{dict}.Remove({key})";
        public string EmitDictCount(string dict) => $"{dict}.Count";
        public string EmitDictContainsKey(string dict, string key) => $"{dict}.ContainsKey({key})";
        public string EmitDictContainsValue(string dict, string value) => $"{dict}.ContainsValue({value})";
        public string EmitDictKeys(string dict) => $"{dict}.Keys.ToArray()";
        public string EmitDictValues(string dict) => $"{dict}.Values.ToArray()";
        public string EmitDictClear(string dict) => $"{dict}.Clear()";

        // HashSet operations
        public string EmitCreateHashSet() => "new HashSet<object>()";
        public string EmitSetAdd(string set, string item) => $"{set}.Add({item})";
        public string EmitSetRemove(string set, string item) => $"{set}.Remove({item})";
        public string EmitSetContains(string set, string item) => $"{set}.Contains({item})";
        public string EmitSetCount(string set) => $"{set}.Count";
        public string EmitSetClear(string set) => $"{set}.Clear()";
        public string EmitSetUnion(string set1, string set2) => $"new HashSet<object>({set1}.Union({set2}))";
        public string EmitSetIntersect(string set1, string set2) => $"new HashSet<object>({set1}.Intersect({set2}))";
        public string EmitSetExcept(string set1, string set2) => $"new HashSet<object>({set1}.Except({set2}))";
        public string EmitSetToArray(string set) => $"{set}.ToArray()";

        #endregion

        #region Networking Emissions

        private string EmitNetworkingCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                // TCP Client
                "tcpconnect" => EmitTcpConnect(args[0], args[1]),
                "tcpsend" => EmitTcpSend(args[0], args[1]),
                "tcpreceive" => EmitTcpReceive(args[0], args[1]),
                "tcpreceiveline" => EmitTcpReceiveLine(args[0]),
                "tcpclose" => EmitTcpClose(args[0]),
                "tcpisconnected" => EmitTcpIsConnected(args[0]),

                // TCP Server
                "tcplisten" => EmitTcpListen(args[0]),
                "tcpaccept" => EmitTcpAccept(args[0]),
                "tcpstoplistener" => EmitTcpStopListener(args[0]),

                // UDP
                "udpcreate" => EmitUdpCreate(),
                "udpbind" => EmitUdpBind(args[0], args[1]),
                "udpsend" => EmitUdpSend(args[0], args[1], args[2], args[3]),
                "udpreceive" => EmitUdpReceive(args[0], args[1]),
                "udpclose" => EmitUdpClose(args[0]),

                // HTTP
                "httpget" => EmitHttpGet(args[0]),
                "httppost" => EmitHttpPost(args[0], args[1], args[2]),
                "httpdownload" => EmitHttpDownload(args[0], args[1]),

                _ => null
            };
        }

        // TCP Client
        public string EmitTcpConnect(string host, string port) => $"new TcpClient({host}, {port})";
        public string EmitTcpSend(string socket, string data) =>
            $"new Func<bool>(() => {{ var sw = new StreamWriter({socket}.GetStream()); sw.Write({data}); sw.Flush(); return true; }})()";
        public string EmitTcpReceive(string socket, string bufferSize) =>
            $"new Func<string>(() => {{ var buffer = new byte[{bufferSize}]; var n = {socket}.GetStream().Read(buffer, 0, buffer.Length); return System.Text.Encoding.UTF8.GetString(buffer, 0, n); }})()";
        public string EmitTcpReceiveLine(string socket) =>
            $"new StreamReader({socket}.GetStream()).ReadLine()";
        public string EmitTcpClose(string socket) => $"{socket}.Close()";
        public string EmitTcpIsConnected(string socket) => $"{socket}.Connected";

        // TCP Server
        public string EmitTcpListen(string port) => $"new Func<TcpListener>(() => {{ var l = new TcpListener(IPAddress.Any, {port}); l.Start(); return l; }})()";
        public string EmitTcpAccept(string listener) => $"{listener}.AcceptTcpClient()";
        public string EmitTcpStopListener(string listener) => $"{listener}.Stop()";

        // UDP
        public string EmitUdpCreate() => "new UdpClient()";
        public string EmitUdpBind(string socket, string port) => $"{socket}.Client.Bind(new IPEndPoint(IPAddress.Any, {port}))";
        public string EmitUdpSend(string socket, string host, string port, string data) =>
            $"new Func<int>(() => {{ var bytes = System.Text.Encoding.UTF8.GetBytes({data}); return {socket}.Send(bytes, bytes.Length, {host}, {port}); }})()";
        public string EmitUdpReceive(string socket, string bufferSize) =>
            $"new Func<string>(() => {{ IPEndPoint ep = null; var bytes = {socket}.Receive(ref ep); return System.Text.Encoding.UTF8.GetString(bytes); }})()";
        public string EmitUdpClose(string socket) => $"{socket}.Close()";

        // HTTP
        public string EmitHttpGet(string url) =>
            $"new HttpClient().GetStringAsync({url}).Result";
        public string EmitHttpPost(string url, string data, string contentType) =>
            $"new HttpClient().PostAsync({url}, new StringContent({data}, System.Text.Encoding.UTF8, {contentType})).Result.Content.ReadAsStringAsync().Result";
        public string EmitHttpDownload(string url, string filePath) =>
            $"new Func<bool>(() => {{ File.WriteAllBytes({filePath}, new HttpClient().GetByteArrayAsync({url}).Result); return true; }})()";

        #endregion

        #region JSON Emissions

        private string EmitJsonCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "jsonparse" => EmitJsonParse(args[0]),
                "jsonstringify" => EmitJsonStringify(args[0]),
                "jsonget" => EmitJsonGet(args[0], args[1]),
                "jsonset" => EmitJsonSet(args[0], args[1], args[2]),
                "jsonisvalid" => EmitJsonIsValid(args[0]),
                _ => null
            };
        }

        public string EmitJsonParse(string jsonString) => $"JsonNode.Parse({jsonString})";
        public string EmitJsonStringify(string obj) => $"JsonSerializer.Serialize({obj})";
        public string EmitJsonGet(string json, string path) => $"{json}[{path}]";
        public string EmitJsonSet(string json, string path, string value) =>
            $"new Func<JsonNode>(() => {{ {json}[{path}] = JsonValue.Create({value}); return {json}; }})()";
        public string EmitJsonIsValid(string jsonString) =>
            $"new Func<bool>(() => {{ try {{ JsonNode.Parse({jsonString}); return true; }} catch {{ return false; }} }})()";

        #endregion

        #region Regex Emissions

        private string EmitRegexCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "regexmatch" => EmitRegexMatch(args[0], args[1]),
                "regexmatches" => EmitRegexMatches(args[0], args[1]),
                "regexreplace" => EmitRegexReplace(args[0], args[1], args[2]),
                "regexsplit" => EmitRegexSplit(args[0], args[1]),
                "ismatch" => EmitIsMatch(args[0], args[1]),
                _ => null
            };
        }

        public string EmitRegexMatch(string input, string pattern) =>
            $"Regex.Match({input}, {pattern}).Value";
        public string EmitRegexMatches(string input, string pattern) =>
            $"Regex.Matches({input}, {pattern}).Cast<Match>().Select(m => m.Value).ToArray()";
        public string EmitRegexReplace(string input, string pattern, string replacement) =>
            $"Regex.Replace({input}, {pattern}, {replacement})";
        public string EmitRegexSplit(string input, string pattern) =>
            $"Regex.Split({input}, {pattern})";
        public string EmitIsMatch(string input, string pattern) =>
            $"Regex.IsMatch({input}, {pattern})";

        #endregion

        #region Environment Emissions

        private string EmitEnvironmentCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "getenv" => EmitGetEnv(args[0]),
                "setenv" => EmitSetEnv(args[0], args[1]),
                "getargs" => EmitGetArgs(),
                "getexepath" => EmitGetExePath(),
                "getmachinename" => EmitGetMachineName(),
                "getusername" => EmitGetUserName(),
                "getosversion" => EmitGetOSVersion(),
                "gettemppath" => EmitGetTempPath(),
                "exit" => EmitExit(args[0]),
                _ => null
            };
        }

        public string EmitGetEnv(string name) => $"Environment.GetEnvironmentVariable({name})";
        public string EmitSetEnv(string name, string value) => $"Environment.SetEnvironmentVariable({name}, {value})";
        public string EmitGetArgs() => "Environment.GetCommandLineArgs()";
        public string EmitGetExePath() => "Environment.ProcessPath";
        public string EmitGetMachineName() => "Environment.MachineName";
        public string EmitGetUserName() => "Environment.UserName";
        public string EmitGetOSVersion() => "Environment.OSVersion.ToString()";
        public string EmitGetTempPath() => "Path.GetTempPath()";
        public string EmitExit(string code) => $"Environment.Exit({code})";

        #endregion

        #region Console Emissions

        private string EmitConsoleCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "cls" => EmitClear(),
                "clearscreen" => EmitClear(),
                "setforecolor" => EmitSetForeColor(args[0]),
                "setbackcolor" => EmitSetBackColor(args[0]),
                "resetcolor" => EmitResetColor(),
                "setcursorpos" => EmitSetCursorPos(args[0], args[1]),
                "getcursorx" => EmitGetCursorX(),
                "getcursory" => EmitGetCursorY(),
                "beep" => EmitBeep(),
                "readkey" => EmitReadKey(),
                "keyavailable" => EmitKeyAvailable(),
                "settitle" => EmitSetTitle(args[0]),
                "getwindowwidth" => EmitGetWindowWidth(),
                "getwindowheight" => EmitGetWindowHeight(),
                _ => null
            };
        }

        public string EmitClear() => "Console.Clear()";
        public string EmitSetForeColor(string color) => $"Console.ForegroundColor = (ConsoleColor)Enum.Parse(typeof(ConsoleColor), {color}, true)";
        public string EmitSetBackColor(string color) => $"Console.BackgroundColor = (ConsoleColor)Enum.Parse(typeof(ConsoleColor), {color}, true)";
        public string EmitResetColor() => "Console.ResetColor()";
        public string EmitSetCursorPos(string x, string y) => $"Console.SetCursorPosition({x}, {y})";
        public string EmitGetCursorX() => "Console.CursorLeft";
        public string EmitGetCursorY() => "Console.CursorTop";
        public string EmitBeep() => "Console.Beep()";
        public string EmitReadKey() => "Console.ReadKey(true).KeyChar.ToString()";
        public string EmitKeyAvailable() => "Console.KeyAvailable";
        public string EmitSetTitle(string title) => $"Console.Title = {title}";
        public string EmitGetWindowWidth() => "Console.WindowWidth";
        public string EmitGetWindowHeight() => "Console.WindowHeight";

        #endregion

        #region Process Emissions

        private string EmitProcessCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "shell" => EmitShell(args[0]),
                "shellasync" => EmitShellAsync(args[0]),
                "run" => EmitRun(args[0], args[1]),
                "runhidden" => EmitRunHidden(args[0], args[1]),
                "killprocess" => EmitKillProcess(args[0]),
                "getprocessid" => EmitGetProcessId(),
                "getprocesses" => EmitGetProcesses(),
                "sleep" => EmitSleep(args[0]),
                _ => null
            };
        }

        public string EmitShell(string command) =>
            $"new Func<int>(() => {{ var p = Process.Start(new ProcessStartInfo {{ FileName = \"cmd\", Arguments = \"/c \" + {command}, UseShellExecute = true }}); p.WaitForExit(); return p.ExitCode; }})()";
        public string EmitShellAsync(string command) =>
            $"Process.Start(new ProcessStartInfo {{ FileName = \"cmd\", Arguments = \"/c \" + {command}, UseShellExecute = true }}).Id";
        public string EmitRun(string exePath, string arguments) =>
            $"new Func<int>(() => {{ var p = Process.Start({exePath}, {arguments}); p.WaitForExit(); return p.ExitCode; }})()";
        public string EmitRunHidden(string exePath, string arguments) =>
            $"new Func<string>(() => {{ var p = new Process {{ StartInfo = new ProcessStartInfo {{ FileName = {exePath}, Arguments = {arguments}, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }} }}; p.Start(); return p.StandardOutput.ReadToEnd(); }})()";
        public string EmitKillProcess(string processId) => $"Process.GetProcessById({processId}).Kill()";
        public string EmitGetProcessId() => "Environment.ProcessId";
        public string EmitGetProcesses() => "Process.GetProcesses().Select(p => p.ProcessName).ToArray()";
        public string EmitSleep(string milliseconds) => $"Thread.Sleep({milliseconds})";

        #endregion

        #region Crypto Emissions

        private string EmitCryptoCall(string functionName, string[] args)
        {
            return functionName.ToLower() switch
            {
                "md5" => EmitMD5(args[0]),
                "sha1" => EmitSHA1(args[0]),
                "sha256" => EmitSHA256(args[0]),
                "base64encode" => EmitBase64Encode(args[0]),
                "base64decode" => EmitBase64Decode(args[0]),
                "randombytes" => EmitRandomBytes(args[0]),
                "newguid" => EmitGuid(),
                _ => null
            };
        }

        public string EmitMD5(string data) =>
            $"BitConverter.ToString(MD5.HashData(System.Text.Encoding.UTF8.GetBytes({data}))).Replace(\"-\", \"\").ToLowerInvariant()";
        public string EmitSHA1(string data) =>
            $"BitConverter.ToString(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes({data}))).Replace(\"-\", \"\").ToLowerInvariant()";
        public string EmitSHA256(string data) =>
            $"BitConverter.ToString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes({data}))).Replace(\"-\", \"\").ToLowerInvariant()";
        public string EmitBase64Encode(string data) =>
            $"Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes({data}))";
        public string EmitBase64Decode(string data) =>
            $"System.Text.Encoding.UTF8.GetString(Convert.FromBase64String({data}))";
        public string EmitRandomBytes(string count) =>
            $"new Func<byte[]>(() => {{ var bytes = new byte[{count}]; RandomNumberGenerator.Fill(bytes); return bytes; }})()";
        public string EmitGuid() => "Guid.NewGuid().ToString()";

        #endregion
    }
}
