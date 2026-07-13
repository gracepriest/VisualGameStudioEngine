using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>
    /// Writes the clangd compilation database (obj/compile_commands.json) from
    /// the same per-TU command lines the toolchain compiles with, so the editor
    /// and the build can never disagree about flags.
    /// </summary>
    public static class CompileCommandsWriter
    {
        public static string Write(
            string projectDir, CppToolchainKind kind, string driver, CppCompileRequest request)
        {
            var objDir = Path.Combine(projectDir, "obj");
            Directory.CreateDirectory(objDir);

            var entries = new List<object>();
            foreach (var tu in request.SourceFiles)
            {
                entries.Add(new
                {
                    directory = projectDir,
                    file = tu,
                    arguments = CppToolchain.BuildCompileCommandArguments(kind, driver, request, tu),
                });
            }

            var path = Path.Combine(objDir, "compile_commands.json");
            File.WriteAllText(path, JsonSerializer.Serialize(entries,
                new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }
    }
}
