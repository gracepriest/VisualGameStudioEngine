using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The analyzer's <c>::</c> relaxation (plan 2 Task 7) is NOT backend-gated, so two assignment
/// shapes that the C++ backend had never seen — because the analyzer used to refuse them —
/// now reach it. Both were found by the branch review, both were SILENT on C++:
///
/// <list type="bullet">
/// <item><description><c>v = ::next_id()</c> — IRBuilder's assignment path RENAMED the foreign
/// call to the target (the declaration path was explicitly guarded against exactly this at
/// IRBuilder's <c>call.Type?.Kind != TypeKind.Foreign</c>), and CppCodeGenerator emits a
/// renamed foreign call as a bare statement: the local kept its old value.</description></item>
/// <item><description><c>::counter = 5</c> — the target became an IRVariable named
/// <c>::counter</c> typed by the VALUE (Integer), so the C++ backend's verbatim-foreign
/// rendering (which keys on <c>TypeKind.Foreign</c>) was skipped and <c>SanitizeName</c>
/// stripped the <c>::</c>: <c>counter = 5;</c>, which C++ resolves to a same-named LOCAL when
/// one exists — the very case the leading <c>::</c> exists to disambiguate.</description></item>
/// </list>
///
/// <para>Text assertions on the generated C++ (no toolchain needed): the shapes are about
/// what is emitted, and an execution test would need a header on disk.</para>
/// </summary>
[TestFixture]
public class CppForeignAssignmentTests
{
    private static string Cpp(string source) => BclE2E.CompileToCppOptimized(source);

    /// <summary>The call's value must reach the local — `v = std::abs(-7)`, not a bare `std::abs(-7);`.</summary>
    [Test]
    public void AssigningAForeignCall_ToAnExistingLocal_KeepsTheAssignment()
    {
        var cpp = Cpp("Sub Main()\nDim v As Integer = 0\nv = ::std::abs(-7)\nConsole.WriteLine(v)\nEnd Sub");

        // The leading `::` is kept verbatim (it is the user's spelling of a global-namespace call).
        Assert.That(cpp, Does.Contain("v = ::std::abs(-7)"),
            "the foreign call was emitted as a bare statement and the assignment dropped");
    }

    /// <summary>The declaration form was already guarded; it must keep working the same way.</summary>
    [Test]
    public void DeclaringFromAForeignCall_StillAssigns()
        => Assert.That(Cpp("Sub Main()\nDim v As Integer = ::std::abs(-7)\nConsole.WriteLine(v)\nEnd Sub"),
            Does.Contain("std::abs(-7)").And.Not.Contain("\n    std::abs(-7);"));

    /// <summary>A write to a `::` global must keep its `::` — it must not bind to a same-named local.</summary>
    [Test]
    public void AssigningAForeignGlobal_KeepsTheQualifier()
    {
        var cpp = Cpp("Sub Main()\nDim counter As Integer = 1\n::counter = 5\nConsole.WriteLine(::counter)\nConsole.WriteLine(counter)\nEnd Sub");

        Assert.That(cpp, Does.Contain("::counter = 5;"), "the qualifier was stripped and the LOCAL was written");
    }

    [Test]
    public void AssigningANamespacedForeignGlobal_KeepsTheQualifier()
        => Assert.That(Cpp("Sub Main()\nmathlib::counter = 5\nEnd Sub"),
            Does.Contain("mathlib::counter = 5;").And.Not.Contain("mathlibcounter"));
}
