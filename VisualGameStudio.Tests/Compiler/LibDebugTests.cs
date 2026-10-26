using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class LibDebugTests
{
    [Test]
    public void Dump()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "libdbg-" + Path.GetRandomFileName())).FullName;
        var main = Path.Combine(dir, "Main.bas");
        File.WriteAllText(main, "Using System.Windows.Forms\nModule Program\n Sub Main()\n  Dim b As Button\n  Console.WriteLine(\"ok\")\n End Sub\nEnd Module\n");
        var result = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" }).CompileProjectFiles(new[] { main });
        var lines = new List<string>();
        lines.Add($"SUCCESS {result.Success}");
        foreach (var e in result.AllErrors) lines.Add($"ALLERR {e.Message}");
        foreach (var u in result.Units)
        {
            lines.Add($"FILE {Path.GetFileName(u.FilePath)} status={u.Status} exports={u.ExportedSymbols.Count}");
            foreach (var s in u.ExportedSymbols)
                lines.Add($"  EXP {s.Name} kind={s.Kind} type={s.Type?.Name} access={s.Access}");
            foreach (var e in u.Errors)
                lines.Add($"  ERR {e.Message}");
        }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "lib-debug.txt"), lines);
    }
}
