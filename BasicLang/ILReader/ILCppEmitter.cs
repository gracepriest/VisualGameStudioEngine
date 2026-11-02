using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace BasicLang.Compiler.ILReader
{
    /// <summary>
    /// IL-to-C++ emitter. Reads a .NET assembly and emits C++.
    /// Handles types, methods, fields, opcodes, control flow, exceptions, generics.
    /// </summary>
    public class ILCppEmitter
    {
        private readonly string _assemblyPath;
        private MetadataReader _mr;
        private PEReader _peReader;
        private MethodBodyBlock _methodBody;

        private readonly Stack<string> _stack = new();
        private readonly Dictionary<int, string> _locals = new();
        private readonly Dictionary<int, string> _localTypes = new();
        private readonly HashSet<int> _branchTargets = new();
        private readonly HashSet<string> _genericInstantiations = new();
        private readonly StringBuilder _sb = new();
        private int _indent;
        private byte[] _il;
        private int _localCounter;
        private bool _isInstanceMethod;

        public ILCppEmitter(string assemblyPath)
        {
            _assemblyPath = assemblyPath;
        }

        public string Emit()
        {
            using var stream = File.OpenRead(_assemblyPath);
            using var peReader = new PEReader(stream);
            _peReader = peReader;
            _mr = peReader.GetMetadataReader();

            // Collect all user types (non-system, non-<Module>).
            var userTypes = new List<TypeDefinitionHandle>();
            foreach (var th in _mr.TypeDefinitions)
            {
                var td = _mr.GetTypeDefinition(th);
                var name = _mr.GetString(td.Name);
                if (name == "<Module>") continue;
                userTypes.Add(th);
            }

            _sb.Clear();
            _sb.AppendLine("#include <iostream>");
            _sb.AppendLine("#include <string>");
            _sb.AppendLine("#include <vector>");
            _sb.AppendLine("#include <algorithm>");
            _sb.AppendLine("#include <cmath>");
            _sb.AppendLine("#include <chrono>");
            _sb.AppendLine("#include <thread>");
            _sb.AppendLine("#include \"BclShim.h\"");
            _sb.AppendLine();
            _sb.AppendLine("using namespace BclShim;");
            _sb.AppendLine();

            // Emit struct definitions for user types.
            foreach (var th in userTypes)
            {
                var td = _mr.GetTypeDefinition(th);
                var name = _mr.GetString(td.Name);
                var ns = _mr.GetString(td.Namespace);
                var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                _sb.AppendLine($"struct {name};");
            }
            _sb.AppendLine();

            // Build a map of type name -> TypeDefinitionHandle for base class resolution.
            var typeByName = new Dictionary<string, TypeDefinitionHandle>();
            foreach (var th in userTypes)
            {
                var td = _mr.GetTypeDefinition(th);
                var name = _mr.GetString(td.Name);
                typeByName[name] = th;
            }

            foreach (var th in userTypes)
            {
                var td = _mr.GetTypeDefinition(th);
                var name = _mr.GetString(td.Name);
                if (name == "<Module>") continue;

                _sb.AppendLine($"struct {name}");
                _sb.AppendLine("{");

                // Collect all fields including inherited ones.
                var allFields = new List<(string type, string name)>();
                var visited = new HashSet<string>();
                var current = td;
                var currentName = name;
                while (!visited.Contains(currentName))
                {
                    visited.Add(currentName);
                    foreach (var fh in current.GetFields())
                    {
                        var fd = _mr.GetFieldDefinition(fh);
                        var fieldName = _mr.GetString(fd.Name);
                        var fieldType = ResolveFieldType(fd);
                        var cppType = ILTypeMapper.Map(fieldType);
                        allFields.Add((cppType, fieldName));
                    }
                    // Walk base class
                    if (current.BaseType.IsNil) break;
                    var baseName = ResolveTypeName(current.BaseType);
                    // Strip namespace for lookup
                    var baseSimpleName = baseName.Contains(".") ? baseName.Substring(baseName.LastIndexOf('.') + 1) : baseName;
                    if (typeByName.TryGetValue(baseSimpleName, out var baseHandle))
                    {
                        current = _mr.GetTypeDefinition(baseHandle);
                        currentName = baseSimpleName;
                    }
                    else
                    {
                        break;
                    }
                }

                foreach (var (cppType, fieldName) in allFields)
                {
                    _sb.AppendLine($"    {cppType} {fieldName} = {{}};");
                }
                // Default constructor
                _sb.AppendLine($"    {name}() = default;");
                _sb.AppendLine("};");
                _sb.AppendLine();
            }

            // Emit method implementations for all user types.
            try
            {
            foreach (var th in userTypes)
            {
                var td = _mr.GetTypeDefinition(th);
                var typeName = _mr.GetString(td.Name);
                if (typeName == "<Module>") continue;

                foreach (var mh in td.GetMethods())
                {
                    var md = _mr.GetMethodDefinition(mh);
                    var methodName = _mr.GetString(md.Name);
                    if (md.RelativeVirtualAddress == 0) continue;

                    // Handle constructors: emit as TypeName__ctor
                    string emitName = methodName;
                    if (methodName == ".ctor") emitName = "__ctor";

                    // Read method body
                    var body = peReader.GetMethodBody(md.RelativeVirtualAddress);
                    var methodIl = body.GetILBytes();
                    if (methodIl == null || methodIl.Length == 0) continue;

                    // Instance method if not static
                    _isInstanceMethod = !md.Attributes.HasFlag(System.Reflection.MethodAttributes.Static);

                    // Save state
                    var savedIl = _il;
                    var savedBody = _methodBody;
                    var savedInstance = _isInstanceMethod;
                    var savedStack = new Stack<string>(_stack);
                    var savedLocals = new Dictionary<int, string>(_locals);
                    var savedLocalTypes = new Dictionary<int, string>(_localTypes);
                    var savedBranchTargets = new HashSet<int>(_branchTargets);

                    _il = methodIl;
                    _methodBody = body;
                    _stack.Clear();
                    _locals.Clear();
                    _localTypes.Clear();
                    _branchTargets.Clear();
                    ParseLocalTypes(body);
                    IdentifyBranchTargets();

                    // Determine return type
                    var sig = md.DecodeSignature(new SimpleTypeProvider(_mr), default);
                    var returnType = ILTypeMapper.Map(sig.ReturnType);

                    // Build parameter list
                    var isStatic = md.Attributes.HasFlag(System.Reflection.MethodAttributes.Static);
                    var paramList = new System.Collections.Generic.List<string>();
                    if (!isStatic) paramList.Add($"{typeName}* this_");
                    int pIdx = 0;
                    foreach (var pType in sig.ParameterTypes)
                    {
                        paramList.Add($"{ILTypeMapper.Map(pType)} arg{pIdx + (isStatic ? 0 : 1)}");
                        pIdx++;
                    }
                    var paramsStr = string.Join(", ", paramList);

                    // Emit method as free function
                    var funcName = (methodName == ".ctor") ? "_ctor" : methodName;
                    _sb.AppendLine($"{returnType} {typeName}_{funcName}({paramsStr})");
                    _sb.AppendLine("{");
                    _indent = 1;

                    // Map 'this' to local_0 (ldarg.0)
                    // For instance methods, ldarg.0 is 'this'
                    try
                    {
                        EmitMethodBody();
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.AppendAllText("C:\\Users\\melvi\\AppData\\Local\\Temp\\opencode\\emitter-debug.txt", $"EXCEPTION in {typeName}.{methodName}: {ex.Message}\n");
                    }

                    // Add return if not void
                    if (returnType != "void")
                    {
                        Indent();
                        _sb.AppendLine($"return {{}};");
                    }
                    _sb.AppendLine("}");
                    _sb.AppendLine();

                    // Restore state
                    _il = savedIl;
                    _methodBody = savedBody;
                    _isInstanceMethod = savedInstance;
                    _stack.Clear();
                    foreach (var s in savedStack) _stack.Push(s);
                    _locals.Clear();
                    foreach (var kv in savedLocals) _locals[kv.Key] = kv.Value;
                    _localTypes.Clear();
                    foreach (var kv in savedLocalTypes) _localTypes[kv.Key] = kv.Value;
                    _branchTargets.Clear();
                    foreach (var t in savedBranchTargets) _branchTargets.Add(t);
                }
            }

            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("C:\\Users\\melvi\\AppData\\Local\\Temp\\opencode\\emit-error.txt", ex.ToString());
            }

            // Emit Main method.
            byte[] ilBytes = null;
            foreach (var th in userTypes)
            {
                var td = _mr.GetTypeDefinition(th);
                foreach (var mh in td.GetMethods())
                {
                    var md = _mr.GetMethodDefinition(mh);
                    var name = _mr.GetString(md.Name);
                    if (name == "Main" && md.RelativeVirtualAddress != 0)
                    {
                        _methodBody = peReader.GetMethodBody(md.RelativeVirtualAddress);
                        ilBytes = _methodBody.GetILBytes();
                        break;
                    }
                }
                if (ilBytes != null) break;
            }

            if (ilBytes == null)
                throw new InvalidOperationException("Main method not found or has no body.");

            _il = ilBytes;
            ParseLocalTypes(_methodBody);
            IdentifyBranchTargets();

            // Map method calls: Box.GetX → Box_GetX
            // (handled in ResolveMethodName via type name)

            _sb.AppendLine("int main()");
            _sb.AppendLine("{");
            _indent = 1;

            try
            {
                EmitMethodBody();
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("C:\\Users\\melvi\\AppData\\Local\\Temp\\opencode\\emit-error.txt", $"Main: {ex.Message}\n{ex.StackTrace}");
            }

            _sb.AppendLine("    return 0;");
            _sb.AppendLine("}");
            return _sb.ToString();
        }

        private void IdentifyBranchTargets()
        {
            int i = 0;
            while (i < _il.Length)
            {
                byte op = _il[i++];
                switch (op)
                {
                    case 0x2B: case 0x2C: case 0x2D: case 0x2E: case 0x2F:
                    case 0x30: case 0x31: case 0x32: case 0x33:
                        {
                            sbyte offset = (sbyte)_il[i];
                            int target = i + 1 + offset;
                            _branchTargets.Add(target);
                            i += 1;
                            break;
                        }
                    case 0x38: case 0x39: case 0x3A:
                        {
                            int target = BitConverter.ToInt32(_il, i);
                            int absTarget = i + 4 + target;
                            _branchTargets.Add(absTarget);
                            i += 4;
                            break;
                        }
                    case 0x1F: i += 1; break;
                    case 0x20: i += 4; break;
                    case 0x21: i += 8; break;
                    case 0x22: i += 4; break;
                    case 0x23: i += 8; break;
                    case 0x72: i += 4; break;
                    case 0x28: i += 4; break;
                    case 0x6F: i += 4; break;
                    case 0x73: i += 4; break;
                    case 0x7B: i += 4; break;
                    case 0x7D: i += 4; break;
                    case 0x7E: i += 4; break;
                    case 0x80: i += 4; break;
                    case 0x8C: i += 4; break;
                    case 0x79: i += 4; break;
                    case 0x8D: i += 4; break;
                    case 0x74: i += 4; break;
                    case 0x75: i += 4; break;
                    case 0x11: i += 2; break;
                    case 0x13: i += 1; break;
                    case 0x14: i += 1; break;
                    case 0x0E: i += 1; break;
                    case 0x0F: i += 1; break;
                    case 0x10: i += 1; break;
                    case 0xFE:
                        i += 1; // skip second byte
                        break;
                    default:
                        break;
                }
            }
        }

        private void EmitMethodBody()
        {
            var regions = _methodBody != null ? _methodBody.ExceptionRegions : default;
            bool hasExceptions = regions.Length > 0;

            var tryStarts = new Dictionary<int, int>();
            var tryEnds = new Dictionary<int, int>();
            var handlerStarts = new Dictionary<int, ExceptionRegion>();
            var handlerEnds = new Dictionary<int, ExceptionRegion>();

            if (hasExceptions)
            {
                for (int r = 0; r < regions.Length; r++)
                {
                    var reg = regions[r];
                    tryStarts[reg.TryOffset] = r;
                    tryEnds[reg.TryOffset + reg.TryLength] = r;
                    handlerStarts[reg.HandlerOffset] = reg;
                    handlerEnds[reg.HandlerOffset + reg.HandlerLength] = reg;
                }
            }

            int i = 0;
            while (i < _il.Length)
            {
                if (hasExceptions)
                {
                    if (tryStarts.ContainsKey(i))
                    {
                        Indent();
                        _sb.AppendLine("try {");
                        _indent++;
                    }
                    if (handlerStarts.TryGetValue(i, out var handlerReg))
                    {
                        _indent--;
                        Indent();
                        if (handlerReg.Kind == ExceptionRegionKind.Finally)
                        {
                            _sb.AppendLine("} finally {");
                        }
                        else
                        {
                            string catchType = "Exception";
                            if (!handlerReg.CatchType.IsNil)
                            {
                                try
                                {
                                    if (handlerReg.CatchType.Kind == HandleKind.TypeReference)
                                    {
                                        var tr = _mr.GetTypeReference((TypeReferenceHandle)handlerReg.CatchType);
                                        catchType = _mr.GetString(tr.Name);
                                    }
                                    else if (handlerReg.CatchType.Kind == HandleKind.TypeDefinition)
                                    {
                                        var td = _mr.GetTypeDefinition((TypeDefinitionHandle)handlerReg.CatchType);
                                        catchType = _mr.GetString(td.Name);
                                    }
                                }
                                catch { }
                            }
                            _sb.AppendLine($"}} catch ({catchType} ex) {{");
                        }
                        _indent++;
                    }
                    if (handlerEnds.ContainsKey(i))
                    {
                        _indent--;
                        Indent();
                        _sb.AppendLine("}");
                    }
                    if (tryEnds.ContainsKey(i) && !handlerStarts.ContainsKey(i))
                    {
                        _indent--;
                        Indent();
                        _sb.AppendLine("}");
                    }
                }

                if (_branchTargets.Contains(i))
                {
                    Indent();
                    _sb.AppendLine($"label_{i}:;");
                }

                byte op = _il[i++];
                switch (op)
                {
                    case 0x00: break;
                    case 0x02: _stack.Push(_isInstanceMethod ? "this_" : "arg0"); break;
                    case 0x03: _stack.Push("arg1"); break;
                    case 0x04: _stack.Push("arg2"); break;
                    case 0x05: _stack.Push("arg3"); break;
                    case 0x06: _stack.Push(GetLocal(0)); break;
                    case 0x07: _stack.Push(GetLocal(1)); break;
                    case 0x08: _stack.Push(GetLocal(2)); break;
                    case 0x09: _stack.Push(GetLocal(3)); break;
                    case 0x0A:
                        {
                            string val = _stack.Pop();
                            if (!_locals.ContainsKey(0)) { _locals[0] = "local_0"; Indent(); _sb.AppendLine($"auto local_0 = {val};"); }
                            else { Indent(); _sb.AppendLine($"local_0 = {val};"); }
                            break;
                        }
                    case 0x0B:
                        {
                            string val = _stack.Pop();
                            if (!_locals.ContainsKey(1)) { _locals[1] = "local_1"; Indent(); _sb.AppendLine($"auto local_1 = {val};"); }
                            else { Indent(); _sb.AppendLine($"local_1 = {val};"); }
                            break;
                        }
                    case 0x0C:
                        {
                            string val = _stack.Pop();
                            if (!_locals.ContainsKey(2)) { _locals[2] = "local_2"; Indent(); _sb.AppendLine($"auto local_2 = {val};"); }
                            else { Indent(); _sb.AppendLine($"local_2 = {val};"); }
                            break;
                        }
                    case 0x0D:
                        {
                            string val = _stack.Pop();
                            if (!_locals.ContainsKey(3)) { _locals[3] = "local_3"; Indent(); _sb.AppendLine($"auto local_3 = {val};"); }
                            else { Indent(); _sb.AppendLine($"local_3 = {val};"); }
                            break;
                        }
                    case 0x0E:
                        {
                            int idx = _il[i++];
                            _stack.Push($"arg{idx}");
                            break;
                        }
                    case 0x11:
                        {
                            int idx = _il[i] | (_il[i + 1] << 8);
                            i += 2;
                            _stack.Push(GetLocal(idx));
                            break;
                        }
                    case 0x13:
                        {
                            int idx = _il[i++];
                            string val = _stack.Pop();
                            if (!_locals.ContainsKey(idx)) { _locals[idx] = $"local_{idx}"; Indent(); _sb.AppendLine($"auto local_{idx} = {val};"); }
                            else { Indent(); _sb.AppendLine($"local_{idx} = {val};"); }
                            break;
                        }
                    case 0x14:
                        {
                            int idx = _il[i++];
                            _stack.Push(GetLocal(idx));
                            break;
                        }
                    case 0x16: _stack.Push("0"); break;
                    case 0x17: _stack.Push("1"); break;
                    case 0x18: _stack.Push("2"); break;
                    case 0x19: _stack.Push("3"); break;
                    case 0x1A: _stack.Push("4"); break;
                    case 0x1B: _stack.Push("5"); break;
                    case 0x1C: _stack.Push("6"); break;
                    case 0x1D: _stack.Push("7"); break;
                    case 0x1E: _stack.Push("8"); break;
                    case 0x1F:
                        {
                            sbyte val = (sbyte)_il[i++];
                            _stack.Push(val.ToString());
                            break;
                        }
                    case 0x20:
                        {
                            int val = BitConverter.ToInt32(_il, i);
                            i += 4;
                            _stack.Push(val.ToString());
                            break;
                        }
                    case 0x21:
                        {
                            long val = BitConverter.ToInt64(_il, i);
                            i += 8;
                            _stack.Push(val.ToString() + "LL");
                            break;
                        }
                    case 0x22:
                        {
                            float val = BitConverter.ToSingle(_il, i);
                            i += 4;
                            _stack.Push(val.ToString("G9") + "f");
                            break;
                        }
                    case 0x23:
                        {
                            double val = BitConverter.ToDouble(_il, i);
                            i += 8;
                            _stack.Push(val.ToString("G17"));
                            break;
                        }
                    case 0x25:
                        {
                            if (_stack.Count > 0) _stack.Push(_stack.Peek());
                            break;
                        }
                    case 0x26:
                        {
                            if (_stack.Count > 0) _stack.Pop();
                            break;
                        }
                    case 0x28: // call
                    case 0x6F: // callvirt
                        {
                            uint token = BitConverter.ToUInt32(_il, i);
                            i += 4;
                            string methodName = ResolveMethodName(token);
                            // Determine if this is an instance method or static method.
                            bool isStaticCall = methodName.StartsWith("Console.") || methodName.StartsWith("Math.")
                                || methodName.StartsWith("Convert.") || methodName.StartsWith("Environment.")
                                || methodName.StartsWith("Array.") || methodName.StartsWith("File.")
                                || methodName.StartsWith("String.") || methodName.StartsWith("BclShim.")
                                || methodName.StartsWith("System.Console.") || methodName.StartsWith("System.Math.")
                                || methodName.StartsWith("System.Convert.") || methodName.StartsWith("System.Environment.")
                                || methodName.StartsWith("System.Array.") || methodName.StartsWith("System.String.")
                                || methodName.Contains("Object") && methodName.EndsWith(".ctor", StringComparison.Ordinal);

                            // Instance/static call handling (heuristic: pop 1 value as arg)
                            string arg = "";
                            string receiver = "";
                            if (isStaticCall)
                            {
                                // Static call: pop args only
                                if (_stack.Count > 0) arg = _stack.Pop();
                            }
                            else
                            {
                                // Instance call: last value on stack is the receiver
                                // For 0-arg methods like GetX(), stack has [receiver]
                                // For 1-arg methods like Add(x), stack has [receiver, x]
                                // We don't know arg count, so pop 1 (receiver) and check if more
                                // For simplicity, assume 0 args for instance methods unless we detect otherwise
                                // Pop the receiver
                                if (_stack.Count > 0)
                                {
                                    // Check if there might be args: if stack has 2+ items, pop last as arg
                                    // This is a heuristic; proper handling needs method signature
                                    receiver = _stack.Pop();
                                    // If there's still something on stack that looks like an arg, pop it
                                    // (heuristic: if stack non-empty after popping receiver, treat top as arg)
                                }
                            }
                            if (methodName.EndsWith("Console.WriteLine", StringComparison.Ordinal) || methodName.EndsWith("Console.Write", StringComparison.Ordinal))
                            {
                                // Console.WriteLine takes 1 arg
                                if (string.IsNullOrEmpty(arg) && _stack.Count > 0)
                                    arg = _stack.Pop();
                                if (string.IsNullOrEmpty(arg) && _stack.Count > 0)
                                    arg = _stack.Pop();
                                Indent();
                                _sb.AppendLine($"std::cout << {arg} << std::endl;");
                            }
                            else if (methodName.Contains("Object") && methodName.EndsWith(".ctor", StringComparison.Ordinal))
                            {
                                // Base constructor call — skip (no-op in C++ for Object)
                                if (_stack.Count > 0) _stack.Pop(); // pop 'this'
                            }
                            else if (methodName.EndsWith("Math.Sqrt", StringComparison.Ordinal))
                            {
                                _stack.Push($"std::sqrt({arg})");
                            }
                            else if (methodName.EndsWith("Math.Abs", StringComparison.Ordinal))
                            {
                                _stack.Push($"std::abs({arg})");
                            }
                            else if (methodName.EndsWith("Math.Floor", StringComparison.Ordinal))
                            {
                                _stack.Push($"std::floor({arg})");
                            }
                            else if (methodName.EndsWith("Math.Ceiling", StringComparison.Ordinal))
                            {
                                _stack.Push($"std::ceil({arg})");
                            }
                            else if (methodName.EndsWith("Math.Pow", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0) { var a2 = _stack.Pop(); _stack.Push($"std::pow({a2}, {arg})"); }
                            }
                            else if (methodName.EndsWith("Math.Min", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0) { var a2 = _stack.Pop(); _stack.Push($"std::min({a2}, {arg})"); }
                            }
                            else if (methodName.EndsWith("Math.Max", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0) { var a2 = _stack.Pop(); _stack.Push($"std::max({a2}, {arg})"); }
                            }
                            else if (methodName.EndsWith("Math.Round", StringComparison.Ordinal))
                            {
                                _stack.Push($"std::round({arg})");
                            }
                            else if (methodName.Contains("String") && methodName.Contains("Length"))
                            {
                                _stack.Push($"{arg}.length()");
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("Concat", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var right = _stack.Pop();
                                    var left = _stack.Count > 0 ? _stack.Pop() : "\"\"";
                                    _stack.Push($"({left} + {right})");
                                }
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("Trim", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Trim({arg})");
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("Replace", StringComparison.Ordinal))
                            {
                                if (_stack.Count >= 2)
                                {
                                    var newStr = _stack.Pop();
                                    var oldStr = _stack.Pop();
                                    _stack.Push($"BclShim::Replace({arg}, {oldStr}, {newStr})");
                                }
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("StartsWith", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var prefix = _stack.Pop();
                                    _stack.Push($"BclShim::StartsWith({arg}, {prefix})");
                                }
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("EndsWith", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var suffix = _stack.Pop();
                                    _stack.Push($"BclShim::EndsWith({arg}, {suffix})");
                                }
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("IndexOf", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var search = _stack.Pop();
                                    _stack.Push($"BclShim::IndexOf({arg}, {search})");
                                }
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("PadLeft", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var width = _stack.Pop();
                                    _stack.Push($"BclShim::PadLeft({arg}, {width})");
                                }
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("PadRight", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var width = _stack.Pop();
                                    _stack.Push($"BclShim::PadRight({arg}, {width})");
                                }
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("Remove", StringComparison.Ordinal))
                            {
                                if (_stack.Count >= 2)
                                {
                                    var count = _stack.Pop();
                                    var start = _stack.Pop();
                                    _stack.Push($"BclShim::Remove({arg}, {start}, {count})");
                                }
                                else if (_stack.Count == 1)
                                {
                                    var start = _stack.Pop();
                                    _stack.Push($"BclShim::Remove({arg}, {start})");
                                }
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("Insert", StringComparison.Ordinal))
                            {
                                if (_stack.Count >= 2)
                                {
                                    var val = _stack.Pop();
                                    var start = _stack.Pop();
                                    _stack.Push($"BclShim::Insert({arg}, {start}, {val})");
                                }
                            }
                            // Math methods (expanded)
                            else if (methodName.Contains("Math") && methodName.EndsWith("Truncate", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Truncate({arg})");
                            }
                            else if (methodName.Contains("Math") && methodName.EndsWith("Sign", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Sign({arg})");
                            }
                            else if (methodName.Contains("Math") && methodName.EndsWith("Log", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Log({arg})");
                            }
                            else if (methodName.Contains("Math") && methodName.EndsWith("Log10", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Log10({arg})");
                            }
                            else if (methodName.Contains("Math") && methodName.EndsWith("Exp", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Exp({arg})");
                            }
                            else if (methodName.Contains("Math") && methodName.EndsWith("Sin", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Sin({arg})");
                            }
                            else if (methodName.Contains("Math") && methodName.EndsWith("Cos", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Cos({arg})");
                            }
                            else if (methodName.Contains("Math") && methodName.EndsWith("Tan", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Tan({arg})");
                            }
                            else if (methodName.Contains("Math") && methodName.EndsWith("Asin", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Asin({arg})");
                            }
                            else if (methodName.Contains("Math") && methodName.EndsWith("Acos", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Acos({arg})");
                            }
                            else if (methodName.Contains("Math") && methodName.EndsWith("Atan", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::Atan({arg})");
                            }
                            else if (methodName.Contains("Math") && methodName.EndsWith("Atan2", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var x = _stack.Pop();
                                    var y = _stack.Count > 0 ? _stack.Pop() : "0";
                                    _stack.Push($"BclShim::Atan2({y}, {x})");
                                }
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("Format", StringComparison.Ordinal))
                            {
                                // String.Format - placeholder (just return the format string)
                                _stack.Push($"/* String.Format */ {arg}");
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("Join", StringComparison.Ordinal))
                            {
                                _stack.Push($"/* String.Join */ {arg}");
                            }
                            else if (methodName.Contains("String") && methodName.EndsWith("Split", StringComparison.Ordinal))
                            {
                                _stack.Push($"/* String.Split */ {arg}");
                            }
                            // List methods (expanded)
                            else if (methodName.Contains("List") && methodName.EndsWith("Insert", StringComparison.Ordinal))
                            {
                                if (_stack.Count >= 2)
                                {
                                    var val = _stack.Pop();
                                    var idx = _stack.Pop();
                                    Indent();
                                    _sb.AppendLine($"{arg}.insert({arg}.begin() + {idx}, {val});");
                                }
                            }
                            else if (methodName.Contains("List") && methodName.EndsWith("RemoveAt", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var idx = _stack.Pop();
                                    Indent();
                                    _sb.AppendLine($"{arg}.erase({arg}.begin() + {idx});");
                                }
                            }
                            else if (methodName.Contains("List") && methodName.EndsWith("Sort", StringComparison.Ordinal))
                            {
                                Indent();
                                _sb.AppendLine($"std::sort({arg}.begin(), {arg}.end());");
                            }
                            else if (methodName.Contains("List") && methodName.EndsWith("IndexOf", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var val = _stack.Pop();
                                    _stack.Push($"(int)(std::find({arg}.begin(), {arg}.end(), {val}) - {arg}.begin())");
                                }
                            }
                            // Dictionary methods (expanded)
                            else if (methodName.Contains("Dictionary") && methodName.EndsWith("TryGetValue", StringComparison.Ordinal))
                            {
                                if (_stack.Count >= 2)
                                {
                                    var val = _stack.Pop();
                                    var key = _stack.Pop();
                                    _stack.Push($"/* TryGetValue */ {arg}[{key}]");
                                }
                            }
                            else if (methodName.Contains("Dictionary") && methodName.EndsWith("Keys", StringComparison.Ordinal))
                            {
                                _stack.Push($"/* {arg}.keys */");
                            }
                            else if (methodName.Contains("Dictionary") && methodName.EndsWith("Values", StringComparison.Ordinal))
                            {
                                _stack.Push($"/* {arg}.values */");
                            }
                            // List methods (expanded)
                            else if (methodName.Contains("List") && methodName.EndsWith("Remove", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var val = _stack.Pop();
                                    Indent();
                                    _sb.AppendLine($"/* {arg}.erase(remove({arg}.begin(), {arg}.end(), {val}), {arg}.end()) */;");
                                }
                            }
                            else if (methodName.Contains("List") && methodName.EndsWith("Contains", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var val = _stack.Pop();
                                    _stack.Push($"(std::find({arg}.begin(), {arg}.end(), {val}) != {arg}.end())");
                                }
                            }
                            else if (methodName.Contains("List") && methodName.EndsWith("Clear", StringComparison.Ordinal))
                            {
                                Indent();
                                _sb.AppendLine($"{arg}.clear();");
                            }
                            // Dictionary methods (expanded)
                            else if (methodName.Contains("Dictionary") && methodName.EndsWith("ContainsKey", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var key = _stack.Pop();
                                    _stack.Push($"({arg}.find({key}) != {arg}.end())");
                                }
                            }
                            else if (methodName.Contains("Dictionary") && methodName.EndsWith("Remove", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var key = _stack.Pop();
                                    Indent();
                                    _sb.AppendLine($"{arg}.erase({key});");
                                }
                            }
                            // DateTime methods
                            else if (methodName.Contains("DateTime") && methodName.EndsWith("AddDays", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var days = _stack.Pop();
                                    _stack.Push($"({arg} + static_cast<int64_t>({days} * 86400000))");
                                }
                            }
                            else if (methodName.Contains("DateTime") && methodName.EndsWith("ToString", StringComparison.Ordinal))
                            {
                                _stack.Push($"std::to_string({arg})");
                            }
                            // Phase 4: BCL shim support — TimeSpan
                            else if (methodName.Contains("TimeSpan") && methodName.EndsWith("FromDays", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::TimeSpanFromDays({arg})");
                            }
                            else if (methodName.Contains("TimeSpan") && methodName.EndsWith("FromHours", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::TimeSpanFromHours({arg})");
                            }
                            else if (methodName.Contains("TimeSpan") && methodName.EndsWith("FromMinutes", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::TimeSpanFromMinutes({arg})");
                            }
                            else if (methodName.Contains("TimeSpan") && methodName.EndsWith("FromSeconds", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::TimeSpanFromSeconds({arg})");
                            }
                            else if (methodName.Contains("TimeSpan") && methodName.EndsWith("FromMilliseconds", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::TimeSpanFromMilliseconds({arg})");
                            }
                            // Phase 4: BCL shim support — Guid
                            else if (methodName.Contains("Guid") && methodName.EndsWith("NewGuid", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::NewGuid()");
                            }
                            else if (methodName.Contains("DateTime") && methodName.EndsWith("Parse", StringComparison.Ordinal))
                            {
                                _stack.Push($"/* DateTime.Parse */ {arg}");
                            }
                            // Environment
                            else if (methodName.Contains("Environment") && methodName.EndsWith("GetEnvironmentVariable", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var name = _stack.Pop();
                                    _stack.Push($"std::getenv({name}.c_str())");
                                }
                            }
                            else if (methodName.Contains("Environment") && methodName.EndsWith("NewLine", StringComparison.Ordinal))
                            {
                                _stack.Push("\"\\\\n\"");
                            }
                            // Array
                            else if (methodName.Contains("Array") && methodName.EndsWith("Sort", StringComparison.Ordinal))
                            {
                                Indent();
                                _sb.AppendLine($"std::sort({arg}.begin(), {arg}.end());");
                            }
                            else if (methodName.Contains("Array") && methodName.EndsWith("Reverse", StringComparison.Ordinal))
                            {
                                Indent();
                                _sb.AppendLine($"std::reverse({arg}.begin(), {arg}.end());");
                            }
                            // StringBuilder
                            else if (methodName.Contains("StringBuilder") && methodName.EndsWith("Append", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var val = _stack.Pop();
                                    Indent();
                                    _sb.AppendLine($"{arg}.append({val});");
                                }
                            }
                            else if (methodName.Contains("StringBuilder") && methodName.EndsWith("ToString", StringComparison.Ordinal))
                            {
                                _stack.Push($"{arg}");
                            }
                            else if (methodName.Contains("StringBuilder") && methodName.EndsWith("Clear", StringComparison.Ordinal))
                            {
                                Indent();
                                _sb.AppendLine($"BclShim::StringBuilderClear({arg});");
                            }
                            else if (methodName.Contains("StringBuilder") && methodName.EndsWith("Length", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::StringBuilderLength({arg})");
                            }
                            // StreamReader/Writer
                            else if (methodName.Contains("StreamReader") && methodName.EndsWith("ReadToEnd", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::StreamReaderReadToEnd({arg})");
                            }
                            else if (methodName.Contains("StreamReader") && methodName.EndsWith("ReadLine", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::StreamReaderReadLine({arg})");
                            }
                            else if (methodName.Contains("StreamWriter") && methodName.EndsWith("Write", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var content = _stack.Pop();
                                    Indent();
                                    _sb.AppendLine($"BclShim::StreamWriterWrite({arg}, {content});");
                                }
                            }
                            else if (methodName.Contains("StreamWriter") && methodName.EndsWith("WriteLine", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0)
                                {
                                    var content = _stack.Pop();
                                    Indent();
                                    _sb.AppendLine($"BclShim::StreamWriterWriteLine({arg}, {content});");
                                }
                            }
                            else if (methodName.Contains("File") && methodName.EndsWith("Exists", StringComparison.Ordinal))
                            {
                                _stack.Push($"BclShim::FileExists({arg})");
                            }
                            // File I/O
                            else if (methodName.Contains("File") && methodName.EndsWith("ReadAllText", StringComparison.Ordinal))
                            {
                                _stack.Push($"/* File.ReadAllText */ {arg}");
                            }
                            else if (methodName.Contains("File") && methodName.EndsWith("WriteAllText", StringComparison.Ordinal))
                            {
                                if (_stack.Count >= 2)
                                {
                                    var content = _stack.Pop();
                                    var path = _stack.Pop();
                                    Indent();
                                    _sb.AppendLine($"/* File.WriteAllText({path}, {content}) */");
                                }
                            }
                            else if (methodName.Contains("File") && methodName.EndsWith("Exists", StringComparison.Ordinal))
                            {
                                _stack.Push($"/* File.Exists */ {arg}");
                            }
                            else if (methodName.Contains("Convert") && methodName.Contains("ToInt32"))
                            {
                                _stack.Push($"static_cast<int32_t>({arg})");
                            }
                            else if (methodName.Contains("Convert") && methodName.Contains("ToDouble"))
                            {
                                _stack.Push($"static_cast<double>({arg})");
                            }
                            else if (methodName.Contains("Convert") && methodName.Contains("ToInt64"))
                            {
                                _stack.Push($"static_cast<int64_t>({arg})");
                            }
                            else if (methodName.Contains("Convert") && methodName.Contains("ToSingle"))
                            {
                                _stack.Push($"static_cast<float>({arg})");
                            }
                            else if (methodName.Contains("Convert") && methodName.Contains("ToBoolean"))
                            {
                                _stack.Push($"static_cast<bool>({arg})");
                            }
                            else if (methodName.Contains("List") && methodName.EndsWith("Add", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0) { var val = _stack.Pop(); Indent(); _sb.AppendLine($"{arg}.push_back({val});"); }
                            }
                            else if (methodName.Contains("List") && methodName.EndsWith("get_Count", StringComparison.Ordinal))
                            {
                                _stack.Push($"{arg}.size()");
                            }
                            else if (methodName.Contains("Dictionary") && methodName.EndsWith("Add", StringComparison.Ordinal))
                            {
                                if (_stack.Count >= 2) { var val = _stack.Pop(); var key = _stack.Pop(); Indent(); _sb.AppendLine($"{arg}[{key}] = {val};"); }
                            }
                            else if (methodName.Contains("Dictionary") && methodName.EndsWith("get_Item", StringComparison.Ordinal))
                            {
                                if (_stack.Count > 0) { var key = _stack.Pop(); _stack.Push($"{arg}[{key}]"); }
                            }
                            else if (methodName.Contains("Thread") && methodName.EndsWith("Sleep", StringComparison.Ordinal))
                            {
                                Indent();
                                _sb.AppendLine($"std::this_thread::sleep_for(std::chrono::milliseconds({arg}));");
                            }
                            else if (methodName.Contains("Environment") && methodName.EndsWith("NewLine", StringComparison.Ordinal))
                            {
                                _stack.Push("\"\\\\n\"");
                            }
                            else if (methodName.Contains("Array") && methodName.EndsWith("Sort", StringComparison.Ordinal))
                            {
                                Indent();
                                _sb.AppendLine($"std::sort({arg}.begin(), {arg}.end());");
                            }
                            else if (methodName.Contains("Array") && methodName.EndsWith("Reverse", StringComparison.Ordinal))
                            {
                                Indent();
                                _sb.AppendLine($"std::reverse({arg}.begin(), {arg}.end());");
                            }
                            else if (methodName.EndsWith(".ctor", StringComparison.Ordinal))
                            {
                                // Constructor call: TypeName__ctor(receiver, args)
                                // methodName is like "Animal..ctor" or "System.Object..ctor"
                                var ctorType = methodName.Substring(0, methodName.LastIndexOf(".ctor")).TrimEnd('.');
                                if (ctorType.Contains(".")) ctorType = ctorType.Substring(ctorType.LastIndexOf('.') + 1);
                                if (string.IsNullOrEmpty(arg))
                                {
                                    Indent();
                                    _sb.AppendLine($"{ctorType}__ctor(this_);");
                                }
                                else
                                {
                                    Indent();
                                    _sb.AppendLine($"{ctorType}__ctor(this_, {arg});");
                                }
                            }
                            else
                            {
                                // Generic method call: emit as a C++ function call
                                var cppName = methodName.Replace(".", "::");
                                // Use receiver if arg is empty (instance method with no extra args)
                                var callArg = !string.IsNullOrEmpty(arg) ? arg : receiver;
                                if (cppName.Contains("::") && !cppName.Contains("BclShim"))
                                {
                                    var parts = cppName.Split(new[] { "::" }, StringSplitOptions.None);
                                    if (parts.Length == 2)
                                    {
                                        _stack.Push($"{parts[0]}_{parts[1]}({callArg})");
                                    }
                                    else
                                    {
                                        _stack.Push($"{cppName}({callArg})");
                                    }
                                }
                                else
                                {
                                    _stack.Push($"{cppName}({callArg})");
                                }
                            }
                            break;
                        }

                    case 0x72: // ldstr
                        {
                            uint token = BitConverter.ToUInt32(_il, i);
                            i += 4;
                            var usHandle = MetadataTokens.UserStringHandle((int)token);
                            var str = _mr.GetUserString(usHandle);
                            _stack.Push($"std::string(\"{Escape(str)}\")");
                            break;
                        }

                    case 0x73: // newobj
                        {
                            uint token = BitConverter.ToUInt32(_il, i);
                            i += 4;
                            string typeName = ResolveConstructorType(token);
                            string cppType = ILTypeMapper.Map(typeName);
                            // Resolve constructor arg count
                            int argCount = 0;
                            try
                            {
                                var sig = ResolveMethodSignature(token);
                                argCount = sig.Count;
                            }
                            catch { }
                            // Pop args
                            var args = new System.Collections.Generic.List<string>();
                            for (int a = 0; a < argCount; a++)
                            {
                                if (_stack.Count > 0) args.Insert(0, _stack.Pop());
                            }
                            var argStr = string.Join(", ", args);
                            if (argCount > 0)
                            {
                                // Emit: auto __tmp = new Type(); Type__ctor(__tmp, args); __tmp
                                // For spike, emit as new Type() and store ctor call separately
                                var tmpVar = $"__new_{_localCounter++}";
                                Indent();
                                _sb.AppendLine($"auto {tmpVar} = new {cppType}();");
                                Indent();
                                _sb.AppendLine($"{typeName}__ctor({tmpVar}, {argStr});");
                                _stack.Push(tmpVar);
                            }
                            else
                            {
                                _stack.Push($"new {cppType}()");
                            }
                            break;
                        }

                    case 0x7B: // ldfld
                        {
                            uint token = BitConverter.ToUInt32(_il, i);
                            i += 4;
                            string fieldName = ResolveFieldName(token);
                            if (_stack.Count > 0)
                            {
                                var obj = _stack.Pop();
                                _stack.Push($"{obj}->{fieldName}");
                            }
                            break;
                        }
                    case 0x7D: // stfld
                        {
                            uint token = BitConverter.ToUInt32(_il, i);
                            i += 4;
                            string fieldName = ResolveFieldName(token);
                            if (_stack.Count >= 2)
                            {
                                var val = _stack.Pop();
                                var obj = _stack.Pop();
                                Indent();
                                _sb.AppendLine($"{obj}->{fieldName} = {val};");
                            }
                            break;
                        }

                    case 0x7E: // ldsfld
                        {
                            i += 4;
                            _stack.Push("static_field");
                            break;
                        }
                    case 0x80: // stsfld
                        {
                            i += 4;
                            if (_stack.Count > 0)
                            {
                                var val = _stack.Pop();
                                Indent();
                                _sb.AppendLine($"static_field = {val};");
                            }
                            break;
                        }

                    case 0x8C: // box
                        i += 4;
                        break;
                    case 0x79: // unbox
                        i += 4;
                        break;

                    case 0x8E: // ldlen
                        {
                            if (_stack.Count > 0)
                            {
                                var arr = _stack.Pop();
                                _stack.Push($"{arr}->Length()");
                            }
                            break;
                        }

                    case 0x8D: // newarr
                        {
                            i += 4;
                            if (_stack.Count > 0)
                            {
                                var size = _stack.Pop();
                                _stack.Push($"new Array({size})");
                            }
                            break;
                        }

                    case 0x90: case 0x91: case 0x92: case 0x93: case 0x94:
                    case 0x95: case 0x96: case 0x98: case 0x99: case 0x9A:
                    case 0xA3: // ldelem
                        {
                            if (_stack.Count >= 2)
                            {
                                var idx = _stack.Pop();
                                var arr = _stack.Pop();
                                _stack.Push($"{arr}[{idx}]");
                            }
                            break;
                        }

                    case 0x9C: case 0x9D: case 0x9E: case 0x9F:
                    case 0xA0: case 0xA1: case 0xA2: case 0xA4: // stelem
                        {
                            if (_stack.Count >= 3)
                            {
                                var val = _stack.Pop();
                                var idx = _stack.Pop();
                                var arr = _stack.Pop();
                                Indent();
                                _sb.AppendLine($"{arr}[{idx}] = {val};");
                            }
                            break;
                        }

                    case 0x67: case 0x68: case 0x69: case 0x6A:
                    case 0x6B: case 0x6C: case 0x6D: case 0x6E:
                        break;

                    case 0x74: // castclass
                        i += 4;
                        break;
                    case 0x75: // isinst
                        i += 4;
                        break;

                    case 0x7A: // throw
                        {
                            if (_stack.Count > 0)
                            {
                                var ex = _stack.Pop();
                                Indent();
                                _sb.AppendLine($"throw {ex};");
                            }
                            break;
                        }

                    case 0x2A: // ret
                        if (_stack.Count > 0)
                        {
                            var retVal = _stack.Pop();
                            Indent();
                            _sb.AppendLine($"return {retVal};");
                        }
                        break;

                    case 0x2B: // br.s
                        {
                            sbyte offset = (sbyte)_il[i];
                            int target = i + 1 + offset;
                            i += 1;
                            Indent();
                            _sb.AppendLine($"goto label_{target};");
                            break;
                        }
                    case 0x38: // br (long)
                        {
                            int offset = BitConverter.ToInt32(_il, i);
                            int target = i + 4 + offset;
                            i += 4;
                            Indent();
                            _sb.AppendLine($"goto label_{target};");
                            break;
                        }
                    case 0x2C: // brfalse.s
                        {
                            sbyte offset = (sbyte)_il[i];
                            int target = i + 1 + offset;
                            i += 1;
                            string val = _stack.Pop();
                            Indent();
                            _sb.AppendLine($"if (!{val}) goto label_{target};");
                            break;
                        }
                    case 0x39: // brfalse (long)
                        {
                            int offset = BitConverter.ToInt32(_il, i);
                            int target = i + 4 + offset;
                            i += 4;
                            string valf = _stack.Pop();
                            Indent();
                            _sb.AppendLine($"if (!{valf}) goto label_{target};");
                            break;
                        }
                    case 0x2D: // brtrue.s
                        {
                            sbyte offset = (sbyte)_il[i];
                            int target = i + 1 + offset;
                            i += 1;
                            string valt = _stack.Pop();
                            Indent();
                            _sb.AppendLine($"if ({valt}) goto label_{target};");
                            break;
                        }
                    case 0x3A: // brtrue (long)
                        {
                            int offset = BitConverter.ToInt32(_il, i);
                            int target = i + 4 + offset;
                            i += 4;
                            string valt2 = _stack.Pop();
                            Indent();
                            _sb.AppendLine($"if ({valt2}) goto label_{target};");
                            break;
                        }

                    case 0x58: // add
                        {
                            string right = _stack.Pop();
                            string left = _stack.Pop();
                            _stack.Push($"({left} + {right})");
                            break;
                        }
                    case 0x59: // sub
                        {
                            string right = _stack.Pop();
                            string left = _stack.Pop();
                            _stack.Push($"({left} - {right})");
                            break;
                        }
                    case 0x5A: // mul
                        {
                            string right = _stack.Pop();
                            string left = _stack.Pop();
                            _stack.Push($"({left} * {right})");
                            break;
                        }
                    case 0x5B: // div
                        {
                            string right = _stack.Pop();
                            string left = _stack.Pop();
                            _stack.Push($"({left} / {right})");
                            break;
                        }
                    case 0x60: // neg
                        {
                            string val = _stack.Pop();
                            _stack.Push($"(-{val})");
                            break;
                        }
                    case 0xFE: // two-byte opcode
                        {
                            byte op2 = _il[i++];
                            switch (op2)
                            {
                                case 0x01: // ceq
                                    {
                                        string right = _stack.Pop();
                                        string left = _stack.Pop();
                                        _stack.Push($"({left} == {right})");
                                        break;
                                    }
                                case 0x02: // cgt
                                    {
                                        string right = _stack.Pop();
                                        string left = _stack.Pop();
                                        _stack.Push($"({left} > {right})");
                                        break;
                                    }
                                case 0x04: // clt
                                    {
                                        string right = _stack.Pop();
                                        string left = _stack.Pop();
                                        _stack.Push($"({left} < {right})");
                                        break;
                                    }
                                case 0x1A: // rethrow
                                    {
                                        Indent();
                                        _sb.AppendLine("throw;");
                                        break;
                                    }
                                default:
                                    break;
                            }
                            break;
                        }

                    default:
                        // Unknown opcode — skip (spike limitation)
                        break;
                }
            }

            // Close try block if exception regions were present.
            if (_methodBody != null && _methodBody.ExceptionRegions.Length > 0)
            {
                _indent--;
                Indent();
                _sb.AppendLine("} catch (...) {");
                Indent();
                _sb.AppendLine("    // Exception handling (Phase 1 basic)");
                _indent--;
                Indent();
                _sb.AppendLine("}");
            }
        }

        private string ResolveMethodName(uint token)
        {
            try
            {
                int tokenType = (int)(token >> 24);
                int rid = (int)(token & 0x00FFFFFF);

                if (tokenType == 0x06) // MethodDef
                {
                    var handle = MetadataTokens.MethodDefinitionHandle(rid);
                    var methodDef = _mr.GetMethodDefinition(handle);
                    var name = _mr.GetString(methodDef.Name);
                    foreach (var typeHandle in _mr.TypeDefinitions)
                    {
                        var typeDef = _mr.GetTypeDefinition(typeHandle);
                        foreach (var mh in typeDef.GetMethods())
                        {
                            if (mh == handle)
                            {
                                var typeName = _mr.GetString(typeDef.Name);
                                var ns = _mr.GetString(typeDef.Namespace);
                                var fullType = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}";
                                return $"{fullType}.{name}";
                            }
                        }
                    }
                    return name;
                }
                else if (tokenType == 0x0A) // MemberRef
                {
                    var handle = MetadataTokens.MemberReferenceHandle(rid);
                    var memberRef = _mr.GetMemberReference(handle);
                    var name = _mr.GetString(memberRef.Name);
                    string parentName = "Unknown";
                    if (memberRef.Parent.Kind == HandleKind.TypeReference)
                    {
                        var typeRef = _mr.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                        var tn = _mr.GetString(typeRef.Name);
                        var tns = _mr.GetString(typeRef.Namespace);
                        parentName = string.IsNullOrEmpty(tns) ? tn : $"{tns}.{tn}";
                    }
                    else if (memberRef.Parent.Kind == HandleKind.TypeDefinition)
                    {
                        var typeDef = _mr.GetTypeDefinition((TypeDefinitionHandle)memberRef.Parent);
                        var tn = _mr.GetString(typeDef.Name);
                        var tns = _mr.GetString(typeDef.Namespace);
                        parentName = string.IsNullOrEmpty(tns) ? tn : $"{tns}.{tn}";
                    }
                    else if (memberRef.Parent.Kind == HandleKind.TypeSpecification)
                    {
                        var typeSpec = _mr.GetTypeSpecification((TypeSpecificationHandle)memberRef.Parent);
                        var sig = typeSpec.DecodeSignature(new SimpleTypeProvider(_mr), null);
                        if (sig.Contains("<"))
                            _genericInstantiations.Add(sig);
                        parentName = sig;
                    }
                    return $"{parentName}.{name}";
                }
                else if (tokenType == 0x2B) // MethodSpec
                {
                    var handle = MetadataTokens.MethodSpecificationHandle(rid);
                    var methodSpec = _mr.GetMethodSpecification(handle);
                    return ResolveMethodName((uint)MetadataTokens.GetToken(methodSpec.Method));
                }
            }
            catch
            {
            }
            return "UnknownMethod";
        }

        private string ResolveConstructorType(uint token)
        {
            try
            {
                int tokenType = (int)(token >> 24);
                int rid = (int)(token & 0x00FFFFFF);

                if (tokenType == 0x06) // MethodDef
                {
                    var handle = MetadataTokens.MethodDefinitionHandle(rid);
                    var methodDef = _mr.GetMethodDefinition(handle);
                    foreach (var typeHandle in _mr.TypeDefinitions)
                    {
                        var typeDef = _mr.GetTypeDefinition(typeHandle);
                        foreach (var mh in typeDef.GetMethods())
                        {
                            if (mh == handle)
                            {
                                var name = _mr.GetString(typeDef.Name);
                                var ns = _mr.GetString(typeDef.Namespace);
                                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                            }
                        }
                    }
                }
                else if (tokenType == 0x0A) // MemberRef
                {
                    var handle = MetadataTokens.MemberReferenceHandle(rid);
                    var memberRef = _mr.GetMemberReference(handle);
                    if (memberRef.Parent.Kind == HandleKind.TypeReference)
                    {
                        var typeRef = _mr.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                        var name = _mr.GetString(typeRef.Name);
                        var ns = _mr.GetString(typeRef.Namespace);
                        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                    }
                    else if (memberRef.Parent.Kind == HandleKind.TypeDefinition)
                    {
                        var typeDef = _mr.GetTypeDefinition((TypeDefinitionHandle)memberRef.Parent);
                        var name = _mr.GetString(typeDef.Name);
                        var ns = _mr.GetString(typeDef.Namespace);
                        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                    }
                    else if (memberRef.Parent.Kind == HandleKind.TypeSpecification)
                    {
                        var typeSpec = _mr.GetTypeSpecification((TypeSpecificationHandle)memberRef.Parent);
                        var sig = typeSpec.DecodeSignature(new SimpleTypeProvider(_mr), null);
                        if (sig.Contains("<"))
                            _genericInstantiations.Add(sig);
                        return sig;
                    }
                }
            }
            catch
            {
            }
            return "System.Object";
        }

        private string ResolveFieldType(FieldDefinition fieldDef)
        {
            try
            {
                var sigBlob = fieldDef.Signature;
                if (sigBlob.IsNil) return "object";
                var reader = _mr.GetBlobReader(sigBlob);
                if (reader.Length == 0) return "object";
                byte first = reader.ReadByte();
                if (first != 0x06) return "object";
                // Read type code
                if (reader.Length == 0) return "object";
                byte typeCode = reader.ReadByte();
                switch (typeCode)
                {
                    case 0x08: return "int";
                    case 0x09: return "uint";
                    case 0x0A: return "long";
                    case 0x0C: return "float";
                    case 0x0D: return "double";
                    case 0x0E: return "string";
                    case 0x12: // CLASS
                        {
                            uint token = ReadCompressedUInt(ref reader);
                            int table = (int)(token & 0x03);
                            int rid = (int)(token >> 2);
                            EntityHandle handle = table == 0 ? MetadataTokens.TypeDefinitionHandle(rid)
                                : table == 1 ? MetadataTokens.TypeReferenceHandle(rid)
                                : MetadataTokens.TypeSpecificationHandle(rid);
                            return ResolveTypeName(handle);
                        }
                    case 0x11: // VALUETYPE
                        {
                            uint token = ReadCompressedUInt(ref reader);
                            int table = (int)(token & 0x03);
                            int rid = (int)(token >> 2);
                            EntityHandle handle = table == 0 ? MetadataTokens.TypeDefinitionHandle(rid)
                                : table == 1 ? MetadataTokens.TypeReferenceHandle(rid)
                                : MetadataTokens.TypeSpecificationHandle(rid);
                            return ResolveTypeName(handle);
                        }
                    case 0x14: // SZARRAY
                        {
                            var elemType = ReadTypeFromBlob(ref reader);
                            return elemType + "[]";
                        }
                    case 0x15: // GENERICINST
                        {
                            byte gType = reader.ReadByte();
                            var genToken = ReadCompressedUInt(ref reader);
                            int gTable = (int)(genToken & 0x03);
                            int gRid = (int)(genToken >> 2);
                            EntityHandle genHandle = gTable == 0 ? MetadataTokens.TypeDefinitionHandle(gRid)
                                : gTable == 1 ? MetadataTokens.TypeReferenceHandle(gRid)
                                : MetadataTokens.TypeSpecificationHandle(gRid);
                            var genTypeName = ResolveTypeName(genHandle);
                            int argCount = (int)ReadCompressedUInt(ref reader);
                            var args = new System.Collections.Generic.List<string>();
                            for (int i = 0; i < argCount; i++)
                                args.Add(ReadTypeFromBlob(ref reader));
                            return $"{genTypeName}<{string.Join(",", args)}>";
                        }
                    default:
                        return "object";
                }
            }
            catch
            {
                return "object";
            }
        }

        private string ReadTypeFromBlob(ref BlobReader reader)
        {
            if (reader.Length == 0) return "object";
            byte typeCode = reader.ReadByte();
            switch (typeCode)
            {
                case 0x02: return "bool";
                case 0x03: return "char";
                case 0x04: return "sbyte";
                case 0x05: return "byte";
                case 0x06: return "short";
                case 0x07: return "ushort";
                case 0x08: return "int";
                case 0x09: return "uint";
                case 0x0A: return "long";
                case 0x0B: return "ulong";
                case 0x0C: return "float";
                case 0x0D: return "double";
                case 0x0E: return "string";
                case 0x11: // VALUETYPE
                    {
                        uint token = ReadCompressedUInt(ref reader);
                        int table = (int)(token & 0x03);
                        int rid = (int)(token >> 2);
                        EntityHandle handle = table == 0 ? MetadataTokens.TypeDefinitionHandle(rid)
                            : table == 1 ? MetadataTokens.TypeReferenceHandle(rid)
                            : MetadataTokens.TypeSpecificationHandle(rid);
                        return ResolveTypeName(handle);
                    }
                case 0x12: // CLASS
                    {
                        uint token = ReadCompressedUInt(ref reader);
                        int table = (int)(token & 0x03);
                        int rid = (int)(token >> 2);
                        EntityHandle handle = table == 0 ? MetadataTokens.TypeDefinitionHandle(rid)
                            : table == 1 ? MetadataTokens.TypeReferenceHandle(rid)
                            : MetadataTokens.TypeSpecificationHandle(rid);
                        return ResolveTypeName(handle);
                    }
                case 0x14: // SZARRAY
                    {
                        var elemType = ReadTypeFromBlob(ref reader);
                        return elemType + "[]";
                    }
                case 0x15: // GENERICINST
                    {
                        byte gType = reader.ReadByte();
                        uint genToken = ReadCompressedUInt(ref reader);
                        int gTable = (int)(genToken & 0x03);
                        int gRid = (int)(genToken >> 2);
                        EntityHandle genHandle = gTable == 0 ? MetadataTokens.TypeDefinitionHandle(gRid)
                            : gTable == 1 ? MetadataTokens.TypeReferenceHandle(gRid)
                            : MetadataTokens.TypeSpecificationHandle(gRid);
                        var genTypeName = ResolveTypeName(genHandle);
                        int argCount = (int)ReadCompressedUInt(ref reader);
                        var args = new System.Collections.Generic.List<string>();
                        for (int i = 0; i < argCount; i++)
                            args.Add(ReadTypeFromBlob(ref reader));
                        return $"{genTypeName}<{string.Join(",", args)}>";
                    }
                default:
                    return "object";
            }
        }

        private uint ReadCompressedUInt(ref BlobReader reader)
        {
            byte first = reader.ReadByte();
            if ((first & 0x80) == 0) return first;
            if ((first & 0xC0) == 0x80)
            {
                byte second = reader.ReadByte();
                return (uint)(((first & 0x3F) << 8) | second);
            }
            byte b2 = reader.ReadByte();
            byte b3 = reader.ReadByte();
            byte b4 = reader.ReadByte();
            return (uint)(((first & 0x1F) << 24) | (b2 << 16) | (b3 << 8) | b4);
        }

        private string ResolveTypeName(EntityHandle handle)
        {
            if (handle.IsNil) return "void";
            if (handle.Kind == HandleKind.TypeDefinition)
            {
                var td = _mr.GetTypeDefinition((TypeDefinitionHandle)handle);
                var n = _mr.GetString(td.Name);
                var ns = _mr.GetString(td.Namespace);
                return string.IsNullOrEmpty(ns) ? n : $"{ns}.{n}";
            }
            if (handle.Kind == HandleKind.TypeReference)
            {
                var tr = _mr.GetTypeReference((TypeReferenceHandle)handle);
                var n = _mr.GetString(tr.Name);
                var ns = _mr.GetString(tr.Namespace);
                return string.IsNullOrEmpty(ns) ? n : $"{ns}.{n}";
            }
            if (handle.Kind == HandleKind.TypeSpecification)
            {
                var ts = _mr.GetTypeSpecification((TypeSpecificationHandle)handle);
                var sig = ts.DecodeSignature(new SimpleTypeProvider(_mr), null);
                if (sig.Contains("<"))
                    _genericInstantiations.Add(sig);
                return sig;
            }
            return handle.Kind.ToString();
        }

        private List<string> ResolveMethodSignature(uint token)
        {
            var result = new List<string>();
            try
            {
                int tokenType = (int)(token >> 24);
                int rid = (int)(token & 0x00FFFFFF);
                MethodDefinition? methodDef = null;
                if (tokenType == 0x06)
                {
                    var handle = MetadataTokens.MethodDefinitionHandle(rid);
                    methodDef = _mr.GetMethodDefinition(handle);
                }
                else if (tokenType == 0x0A)
                {
                    var handle = MetadataTokens.MemberReferenceHandle(rid);
                    var memberRef = _mr.GetMemberReference(handle);
                    // MemberRef doesn't have direct signature access easily; skip
                    return result;
                }
                if (methodDef.HasValue)
                {
                    var sig = methodDef.Value.DecodeSignature(new SimpleTypeProvider(_mr), default);
                    foreach (var p in sig.ParameterTypes)
                        result.Add(p);
                }
            }
            catch { }
            return result;
        }

        private string ResolveFieldName(uint token)
        {
            try
            {
                int tokenType = (int)(token >> 24);
                int rid = (int)(token & 0x00FFFFFF);

                if (tokenType == 0x04) // FieldDef
                {
                    var handle = MetadataTokens.FieldDefinitionHandle(rid);
                    var fieldDef = _mr.GetFieldDefinition(handle);
                    return _mr.GetString(fieldDef.Name);
                }
                else if (tokenType == 0x0A) // MemberRef (field)
                {
                    var handle = MetadataTokens.MemberReferenceHandle(rid);
                    var memberRef = _mr.GetMemberReference(handle);
                    return _mr.GetString(memberRef.Name);
                }
            }
            catch
            {
            }
            return "field";
        }

        private void ParseLocalTypes(MethodBodyBlock body)
        {
            if (body == null || body.LocalSignature.IsNil) return;
            try
            {
                var sigReader = _mr.GetStandaloneSignature(body.LocalSignature);
                var provider = new SimpleTypeProvider(_mr);
                var locals = sigReader.DecodeLocalSignature(provider, null);
                int idx = 0;
                foreach (var localType in locals)
                {
                    _localTypes[idx] = localType;
                    if (localType.Contains("<"))
                        _genericInstantiations.Add(localType);
                    idx++;
                }
            }
            catch
            {
                // Fall back to int for all locals
            }
        }

        private string GetLocal(int idx)
        {
            if (!_locals.TryGetValue(idx, out var name))
            {
                name = $"local_{idx}";
                _locals[idx] = name;
                var cppType = _localTypes.TryGetValue(idx, out var t) ? ILTypeMapper.Map(t) : "int";
                if (cppType == "void*") cppType = "int";
                Indent();
                _sb.AppendLine($"{cppType} {name} = {{}};");
            }
            return name;
        }

        private void Indent()
        {
            _sb.Append(new string(' ', _indent * 4));
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}