using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace BasicLang.Compiler.ILReader
{
    /// <summary>
    /// Reads a .NET assembly's metadata using System.Reflection.Metadata.
    /// Phase 0 spike: prints types and methods. No code generation yet.
    /// </summary>
    public class ILAssemblyReader
    {
        public string FilePath { get; }

        public ILAssemblyReader(string filePath)
        {
            FilePath = filePath;
        }

        /// <summary>
        /// Reads the assembly and returns a summary string of its types and methods.
        /// </summary>
        public string ReadSummary()
        {
            using var stream = File.OpenRead(FilePath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Assembly: {Path.GetFileName(FilePath)}");
            sb.AppendLine($"Types: {metadataReader.TypeDefinitions.Count}");
            sb.AppendLine();

            foreach (var typeDefHandle in metadataReader.TypeDefinitions)
            {
                var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                var typeName = metadataReader.GetString(typeDef.Name);
                var typeNs = metadataReader.GetString(typeDef.Namespace);
                var fullName = string.IsNullOrEmpty(typeNs) ? typeName : $"{typeNs}.{typeName}";

                sb.AppendLine($"Type: {fullName}");

                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var methodDef = metadataReader.GetMethodDefinition(methodHandle);
                    var methodName = metadataReader.GetString(methodDef.Name);
                    sb.AppendLine($"  Method: {methodName} (RVA: {methodDef.RelativeVirtualAddress})");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Reads the IL bytes of a method by its name (first match).
        /// </summary>
        public byte[] ReadMethodBody(string methodName)
        {
            using var stream = File.OpenRead(FilePath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();

            foreach (var typeDefHandle in metadataReader.TypeDefinitions)
            {
                var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var methodDef = metadataReader.GetMethodDefinition(methodHandle);
                    var name = metadataReader.GetString(methodDef.Name);
                    if (name == methodName && methodDef.RelativeVirtualAddress != 0)
                    {
                        var body = peReader.GetMethodBody(methodDef.RelativeVirtualAddress);
                        return body.GetILBytes();
                    }
                }
            }
            return null;
        }
    }
}