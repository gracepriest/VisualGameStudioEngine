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
    /// IL-to-C++ emitter. Phase 1: handles common opcodes (ldstr, call, ret, arithmetic,
    /// locals, branches, conditionals). Not yet a full compiler.
    /// </summary>
    public class ILCppEmitter
    {
        private readonly string _assemblyPath;
        private MetadataReader _mr;
        private PEReader _peReader;

        // Stack of C++ expressions (as strings) for the current method.
        private readonly Stack<string> _stack = new();
        // Local variable slots → C++ variable names.
        private readonly Dictionary<int, string> _locals = new();
        private int _localCounter;
        // Label counter for branches.
        private int _labelCounter;
        // Collected labels that need to be emitted.
        private readonly HashSet<int> _branchTargets = new();
        // The IL bytes of the current method.
        private byte[] _il;
        // Output builder.
        private readonly StringBuilder _sb = new();
        // Current indentation.
        private int _indent;
        // Whether we're inside Main (entry point).
        private bool _isMain;

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

            // Find Main method.
            byte[] ilBytes = null;
            foreach (var typeDefHandle in _mr.TypeDefinitions)
            {
                var typeDef = _mr.GetTypeDefinition(typeDefHandle);
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var methodDef = _mr.GetMethodDefinition(methodHandle);
                    var name = _mr.GetString(methodDef.Name);
                    if (name == "Main" && methodDef.RelativeVirtualAddress != 0)
                    {
                        var body = peReader.GetMethodBody(methodDef.RelativeVirtualAddress);
                        ilBytes = body.GetILBytes();
                        break;
                    }
                }
                if (ilBytes != null) break;
            }

            if (ilBytes == null)
                throw new InvalidOperationException("Main method not found or has no body.");

            _il = ilBytes;

            // First pass: identify branch targets.
            IdentifyBranchTargets();

            // Second pass: emit C++.
            _sb.Clear();
            _sb.AppendLine("#include <iostream>");
            _sb.AppendLine("#include <string>");
            _sb.AppendLine();
            _sb.AppendLine("int main()");
            _sb.AppendLine("{");
            _indent = 1;

            EmitMethodBody();

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
                    case 0x2B: // br.s (short)
                    case 0x2C: // brfalse.s
                    case 0x2D: // brtrue.s
                    case 0x2E: // beq.s
                    case 0x2F: // bge.s
                    case 0x30: // bgt.s
                    case 0x31: // ble.s
                    case 0x32: // blt.s
                    case 0x33: // bne.s
                        {
                            sbyte offset = (sbyte)_il[i];
                            int target = i + 1 + offset;
                            _branchTargets.Add(target);
                            i += 1;
                            break;
                        }
                    case 0x38: // br (long)
                        {
                            int target = BitConverter.ToInt32(_il, i);
                            // target is relative to the next instruction
                            int absTarget = i + 4 + target;
                            _branchTargets.Add(absTarget);
                            i += 4;
                            break;
                        }
                    case 0x39: // brfalse (long)
                        {
                            int target = BitConverter.ToInt32(_il, i);
                            int absTarget = i + 4 + target;
                            _branchTargets.Add(absTarget);
                            i += 4;
                            break;
                        }
                    case 0x3A: // brtrue (long)
                        {
                            int target = BitConverter.ToInt32(_il, i);
                            int absTarget = i + 4 + target;
                            _branchTargets.Add(absTarget);
                            i += 4;
                            break;
                        }
                    case 0x20: // ldc.i4 (long)
                        i += 4;
                        break;
                    case 0x1F: // ldc.i4.s (short)
                        i += 1;
                        break;
                    case 0x72: // ldstr
                        i += 4;
                        break;
                    case 0x28: // call
                        i += 4;
                        break;
                    case 0x11: // ldloc
                    case 0x12: // ldloc.s? actually 0x12 is stloc
                        break;
                    default:
                        // Most other opcodes are 1 byte; some need special handling.
                        break;
                }
            }
        }

        private void EmitMethodBody()
        {
            int i = 0;
            while (i < _il.Length)
            {
                // Emit label if this offset is a branch target.
                if (_branchTargets.Contains(i))
                {
                    Indent();
                    _sb.AppendLine($"label_{i}:;");
                }

                byte op = _il[i++];
                switch (op)
                {
                    case 0x00: // nop
                        break;

                    case 0x02: // ldarg.0
                        _stack.Push("arg0");
                        break;
                    case 0x03: // ldarg.1
                        _stack.Push("arg1");
                        break;
                    case 0x04: // ldarg.2
                        _stack.Push("arg2");
                        break;
                    case 0x05: // ldarg.3
                        _stack.Push("arg3");
                        break;
                    case 0x0E: // ldarg.s
                        {
                            int idx = _il[i++];
                            _stack.Push($"arg{idx}");
                            break;
                        }
                    case 0x0F: // ldarga.s
                        i += 1;
                        break;

                    case 0x06: // ldloc.0
                        _stack.Push(GetLocal(0));
                        break;
                    case 0x07: // ldloc.1
                        _stack.Push(GetLocal(1));
                        break;
                    case 0x08: // ldloc.2
                        _stack.Push(GetLocal(2));
                        break;
                    case 0x09: // ldloc.3
                        _stack.Push(GetLocal(3));
                        break;
                    case 0x11: // ldloc
                        {
                            // ldloc takes a 2-byte index (little-endian)
                            int idx = _il[i] | (_il[i + 1] << 8);
                            i += 2;
                            _stack.Push(GetLocal(idx));
                            break;
                        }
                    case 0x12: // stloc? Actually 0x12 is stloc (2-byte)
                        // Wait, stloc.0-3 are 0x0A-0x0D, stloc.s is 0x13, stloc is 0x15
                        break;

                    case 0x0A: // stloc.0
                        {
                            string val = _stack.Pop();
                            string varName = GetLocal(0);
                            Indent();
                            _sb.AppendLine($"{varName} = {val};");
                            break;
                        }
                    case 0x0B: // stloc.1
                        {
                            string val = _stack.Pop();
                            string varName = GetLocal(1);
                            Indent();
                            _sb.AppendLine($"{varName} = {val};");
                            break;
                        }
                    case 0x0C: // stloc.2
                        {
                            string val = _stack.Pop();
                            string varName = GetLocal(2);
                            Indent();
                            _sb.AppendLine($"{varName} = {val};");
                            break;
                        }
                    case 0x0D: // stloc.3
                        {
                            string val = _stack.Pop();
                            string varName = GetLocal(3);
                            Indent();
                            _sb.AppendLine($"{varName} = {val};");
                            break;
                        }
                    case 0x13: // stloc.s
                        {
                            int idx = _il[i++];
                            string val = _stack.Pop();
                            string varName = GetLocal(idx);
                            Indent();
                            _sb.AppendLine($"{varName} = {val};");
                            break;
                        }

                    case 0x14: // ldloc.s
                        {
                            int idx = _il[i++];
                            _stack.Push(GetLocal(idx));
                            break;
                        }

                    case 0x16: // ldc.i4.0
                        _stack.Push("0");
                        break;
                    case 0x17: // ldc.i4.1
                        _stack.Push("1");
                        break;
                    case 0x18: // ldc.i4.2
                        _stack.Push("2");
                        break;
                    case 0x19: // ldc.i4.3
                        _stack.Push("3");
                        break;
                    case 0x1A: // ldc.i4.4
                        _stack.Push("4");
                        break;
                    case 0x1B: // ldc.i4.5
                        _stack.Push("5");
                        break;
                    case 0x1C: // ldc.i4.6
                        _stack.Push("6");
                        break;
                    case 0x1D: // ldc.i4.7
                        _stack.Push("7");
                        break;
                    case 0x1E: // ldc.i4.8
                        _stack.Push("8");
                        break;
                    case 0x1F: // ldc.i4.s
                        {
                            sbyte val = (sbyte)_il[i++];
                            _stack.Push(val.ToString());
                            break;
                        }
                    case 0x20: // ldc.i4
                        {
                            int val = BitConverter.ToInt32(_il, i);
                            i += 4;
                            _stack.Push(val.ToString());
                            break;
                        }
                    case 0x21: // ldc.i8
                        {
                            long val = BitConverter.ToInt64(_il, i);
                            i += 8;
                            _stack.Push(val.ToString() + "LL");
                            break;
                        }
                    case 0x22: // ldc.r4
                        {
                            float val = BitConverter.ToSingle(_il, i);
                            i += 4;
                            _stack.Push(val.ToString("G9") + "f");
                            break;
                        }
                    case 0x23: // ldc.r8
                        {
                            double val = BitConverter.ToDouble(_il, i);
                            i += 8;
                            _stack.Push(val.ToString("G17"));
                            break;
                        }

                    case 0x72: // ldstr
                        {
                            uint token = BitConverter.ToUInt32(_il, i);
                            i += 4;
                            var usHandle = MetadataTokens.UserStringHandle((int)token);
                            var str = _mr.GetUserString(usHandle);
                            _stack.Push($"\"{Escape(str)}\"");
                            break;
                        }

                    case 0x28: // call
                        {
                            uint token = BitConverter.ToUInt32(_il, i);
                            i += 4;
                            string methodName = ResolveMethodName(token);
                            string arg = _stack.Count > 0 ? _stack.Pop() : "";

                            // Known BCL calls → C++ equivalents
                            if (methodName.EndsWith("Console.WriteLine", StringComparison.Ordinal)
                                || methodName.EndsWith("Console.Write", StringComparison.Ordinal))
                            {
                                Indent();
                                _sb.AppendLine($"std::cout << {arg} << std::endl;");
                            }
                            else if (methodName == "Console.ReadLine")
                            {
                                _stack.Push("std::cin"); // placeholder
                            }
                            else
                            {
                                // Generic method call: emit as a C++ function call
                                var cppName = methodName.Replace(".", "::");
                                if (string.IsNullOrEmpty(arg))
                                {
                                    Indent();
                                    _sb.AppendLine($"{cppName}();");
                                }
                                else
                                {
                                    _stack.Push($"{cppName}({arg})");
                                }
                            }
                            break;
                        }

                    case 0x25: // dup
                        {
                            if (_stack.Count > 0) _stack.Push(_stack.Peek());
                            break;
                        }
                    case 0x26: // pop
                        {
                            if (_stack.Count > 0) _stack.Pop();
                            break;
                        }

                    case 0x73: // newobj
                        {
                            i += 4; // skip method token
                            _stack.Push("new Object()");
                            break;
                        }

                    case 0x7B: // ldfld
                        {
                            i += 4; // skip field token
                            if (_stack.Count > 0)
                            {
                                var obj = _stack.Pop();
                                _stack.Push($"{obj}->field");
                            }
                            break;
                        }
                    case 0x7D: // stfld
                        {
                            i += 4; // skip field token
                            if (_stack.Count >= 2)
                            {
                                var val = _stack.Pop();
                                var obj = _stack.Pop();
                                Indent();
                                _sb.AppendLine($"{obj}->field = {val};");
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
                        i += 4; // skip type token (value stays on stack)
                        break;
                    case 0x79: // unbox
                        i += 4; // skip type token
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
                            i += 4; // skip type token
                            if (_stack.Count > 0)
                            {
                                var size = _stack.Pop();
                                _stack.Push($"new Array({size})");
                            }
                            break;
                        }

                    case 0x67: // conv.i1
                    case 0x68: // conv.i2
                    case 0x69: // conv.i4
                    case 0x6A: // conv.i8
                    case 0x6B: // conv.r4
                    case 0x6C: // conv.r8
                    case 0x6D: // conv.u4
                    case 0x6E: // conv.u8
                        // Leave value on stack (C++ handles conversion)
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
                        // End of method — don't emit anything extra
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
                    // Get declaring type
                    // MethodDef doesn't directly give type; need to find the type that owns it
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
                    // Resolve the parent type
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
                    return $"{parentName}.{name}";
                }
                else if (tokenType == 0x2B) // MethodSpec
                {
                    var handle = MetadataTokens.MethodSpecificationHandle(rid);
                    var methodSpec = _mr.GetMethodSpecification(handle);
                    // Resolve the underlying method
                    return ResolveMethodName((uint)MetadataTokens.GetToken(methodSpec.Method));
                }
            }
            catch
            {
                // Fall through to default
            }
            return "UnknownMethod";
        }

        private string GetLocal(int idx)
        {
            if (!_locals.TryGetValue(idx, out var name))
            {
                name = $"local_{idx}";
                _locals[idx] = name;
                // Declare the variable (int by default for spike)
                Indent();
                _sb.AppendLine($"int {name} = 0;");
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