using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ TASK #134 — the AGGRESSIVE C++ a Release <c>.blproj</c> now emits, COMPILED AND RUN, prints
/// vbc's answer.
///
/// <para>Until #134 a C++ <c>.blproj</c> never ran the aggressive pipeline, so no C++ project leg
/// could see an aggressive-only miscompile (the 2026-09-24 LICM defect printed 6 under CLI
/// <c>--optimize</c> and 12 from the same program's C++ project). Now that it does, this is the leg
/// that would see one: each program below is built as a Release project, its <c>obj/gen</c> is
/// compiled with a real C++ compiler (clang++/g++, the way <c>CppCompile</c> compiles every split
/// emission in this suite) and run under a timeout, and its stdout is compared with the answer vbc
/// gives the same source.</para>
///
/// <para>The programs are the ones whose C++ the aggressive pipeline changes (LICM hoists, algebraic
/// simplification), measured when #134 landed: all of them print vbc's answer before and after the
/// change, so this fixture is a standing guard on the aggressive output, not a fix probe. ⚠ The
/// Platformer sample is NOT here: it fails to compile on C++ identically in every pipeline (a
/// <c>vector&lt;bool&gt;</c> bound to a non-const reference, #255) and says nothing about this change.</para>
///
/// <para>A BasicLang native BUILD needs MSVC, which Linux does not have, so the project is taken to
/// its toolchain gate (<c>obj/gen</c> written, BL6015) and the generated files are compiled directly;
/// on a machine with MSVC the same files come from the same builder call.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CppReleaseProjectExecutionTests
{
    [TestCaseSource(typeof(CppPipelinePrograms), nameof(CppPipelinePrograms.Runnable))]
    public void AReleaseProject_BuiltAsTheAggressivePipeline_PrintsVbcsAnswer(CppPipelineProgram program)
    {
        var compiler = Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        using var project = new CppProjectProbe(program.Source);
        var release = project.Emit(CppProjectRoute.BuildApi, "Release");

        // The thing run must BE the aggressive pipeline's code — otherwise this is a standard-pipeline
        // run and says nothing about #134. (CppProjectOptimizerPipelineTests pins which pipeline
        // each route takes; this is the precondition for the run, stated where it is used.)
        var standard = CppPipelineLines.Standard(program.Source);
        var aggressive = CppPipelineLines.Aggressive(program.Source);
        var actual = CppPipelineLines.Of(release);
        Assert.Multiple(() =>
        {
            Assert.That(CppPipelineLines.Delta(aggressive, actual), Is.Empty, "the Release project's code is the aggressive pipeline's");
            Assert.That(CppPipelineLines.Delta(actual, standard), Is.Not.Empty, "...and is not the standard pipeline's (the probe discriminates)");
        });

        var translationUnits = release.Files.Keys.Where(k => k.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k).ToList();
        var stdout = Native.CppCompile.CompileAndRunFiles(release.Files, translationUnits, compiler.Value);

        Assert.That(stdout.Replace("\r\n", "\n").TrimEnd('\n'), Is.EqualTo(program.Expected.TrimEnd('\n')),
            $"{program.Name}: the aggressive C++ a Release project emitted printed something other than vbc's answer.");
    }
}
