using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace BasicLang.Compiler.ILReader
{
    /// <summary>
    /// Phase 0 spike: reads a .NET assembly's Main method IL and emits a minimal C++ program.
    /// Recognizes ldstr/call/ret. Not a full compiler — just proof of concept.
    /// </summary>
    public class ILCppEmitter
    {
        private readonly string _assemblyPath;

        public ILCppEmitter(string assemblyPath)
        {
            _assemblyPath = assemblyPath;
        }

        public string Emit()
        {
            using var stream = File.OpenRead(_assemblyPath);
            using var peReader = new PEReader(stream);
            var mr = peReader.GetMetadataReader();

            // Find the Main method and its IL bytes.
            byte[] ilBytes = null;
            foreach (var typeDefHandle in mr.TypeDefinitions)
            {
                var typeDef = mr.GetTypeDefinition(typeDefHandle);
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var methodDef = mr.GetMethodDefinition(methodHandle);
                    var name = mr.GetString(methodDef.Name);
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

            // Parse IL: collect ldstr strings and detect Console.WriteLine calls.
            var strings = new System.Collections.Generic.List<string>();
            int i = 0;
            while (i < ilBytes.Length)
            {
                byte op = ilBytes[i++];
                if (op == 0x72) // ldstr
                {
                    // 4-byte metadata token (little-endian)
                    uint token = BitConverter.ToUInt32(ilBytes, i);
                    i += 4;
                    // Resolve string
                    var usHandle = MetadataTokens.UserStringHandle((int)token);
                    var str = mr.GetUserString(usHandle);
                    strings.Add(str);
                }
                else if (op == 0x28) // call
                {
                    i += 4; // skip method token
                }
                else if (op == 0x2A) // ret
                {
                    break;
                }
                else if (op == 0x00) // nop
                {
                    // skip
                }
                else
                {
                    // Unknown opcode — skip (spike limitation)
                }
            }

            // Emit C++.
            var sb = new StringBuilder();
            sb.AppendLine("#include <iostream>");
            sb.AppendLine();
            sb.AppendLine("int main()");
            sb.AppendLine("{");
            foreach (var s in strings)
            {
                sb.AppendLine($"    std::cout << \"{Escape(s)}\" << std::endl;");
            }
            sb.AppendLine("    return 0;");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}