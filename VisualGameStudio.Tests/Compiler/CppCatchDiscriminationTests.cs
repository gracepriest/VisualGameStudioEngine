using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A typed <c>Catch</c> selects by TYPE, not by position — chip task_d7f0a91c.
///
/// <para>It did not. <c>Throw New XException(msg)</c> lowered to a bare
/// <c>throw std::runtime_error(msg)</c>, so the declared type existed only in the source.
/// Every typed clause then emitted a BYTE-IDENTICAL <c>catch (const std::runtime_error&amp;)</c>
/// handler and C++ dispatched to the first one — measured: a thrown
/// <c>ArgumentException</c> ran an <c>InvalidOperationException</c> body, and swapping the
/// clause order swapped the answer.</para>
///
/// <para>The fix carries the ';'-delimited .NET inheritance chain on the throw, routing it
/// into the §11.1 <c>NetException</c> ladder the generator ALREADY emitted — correct all
/// along, just never entered because nothing ever threw a <c>NetException</c>.</para>
///
/// <para>⛔ <b>ORDER-INDEPENDENCE AND OVER-CATCH ARE THE TESTS THAT MATTER.</b> A positive
/// subclass match (<c>ArgumentNullException</c> caught by <c>Catch e As ArgumentException</c>)
/// passed even against the BROKEN compiler, because clause 1 caught everything and clause 1
/// happened to be right. The P2a-2 plan's Task 13 specified exactly that shape, which is why
/// it could never have failed. A test here must include a thrown type that must SKIP a clause.</para>
///
/// <para>⚠ SCOPE. This covers exceptions BasicLang itself throws. The native BCL still throws
/// ~64 bare <c>std::runtime_error</c>s (Guid/DateTime/Decimal parse and overflow failures),
/// which carry no chain and are therefore matched only by <c>Catch e As Exception</c>. That is
/// a LOUD divergence — the exception propagates rather than running a handler written for a
/// different type — and converting those sites is tracked separately.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class CppCatchDiscriminationTests
{
    private static string Run(string source) =>
        BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source)).Replace("\r\n", "\n").Trim();

    [OneTimeSetUp]
    public void RequireCppCompiler()
    {
        if (BasicLang.Compiler.ProjectSystem.CppToolchain.Find() == null)
            Assert.Ignore("no C++ toolchain found — this fixture compiles and runs native code.");
    }

    /// <summary>
    /// THE core case. FormatException/InvalidOperationException/ArgumentException are SIBLINGS
    /// under SystemException, so a clause naming one must not catch another.
    /// </summary>
    [Test]
    public void AClauseWhoseTypeDoesNotMatch_IsSkipped()
    {
        Assert.That(Run(@"
Function F() As String
    Try
        Throw New ArgumentException(""boom"")
    Catch e As InvalidOperationException
        Return ""IOE""
    Catch b As Exception
        Return ""EX""
    End Try
End Function

Sub Main()
    Console.WriteLine(F())
End Sub
"), Is.EqualTo("EX"),
            "IOE means the first clause caught an exception its declared type excludes.");
    }

    /// <summary>
    /// Both clause orderings must give the SAME answer — that is the property, and asserting
    /// it directly is the only way to make this test able to fail.
    ///
    /// <para>⛔ An earlier version ran ONLY the Exception-first ordering and asserted "EX". It
    /// PASSED against the broken compiler: with "first clause wins", the Exception clause was
    /// first, so the right answer came out for the wrong reason. Mutation testing caught it.
    /// The same trap one level up from the one in this fixture's header — a test can be about
    /// the correct property and still be satisfied by the bug.</para>
    /// </summary>
    [Test]
    public void TheAnswerDoesNotDependOnClauseOrder()
    {
        const string body = @"
Function Ordered() As String
    Try
        Throw New ArgumentException(""boom"")
    Catch e As InvalidOperationException
        Return ""IOE""
    Catch b As Exception
        Return ""EX""
    End Try
End Function

Function Swapped() As String
    Try
        Throw New ArgumentException(""boom"")
    Catch b As Exception
        Return ""EX""
    Catch e As InvalidOperationException
        Return ""IOE""
    End Try
End Function

Sub Main()
    Console.WriteLine(Ordered())
    Console.WriteLine(Swapped())
End Sub
";
        Assert.That(Run(body), Is.EqualTo("EX\nEX"),
            "the two orderings must agree, and both must pick the clause whose type actually "
            + "matches. 'IOE\\nEX' is the signature of position deciding instead of type.");
    }

    /// <summary>
    /// OVER-CATCH — the worse half. A non-matching inner clause must not STEAL the exception
    /// from a correct outer handler.
    /// </summary>
    [Test]
    public void ANonMatchingClause_DoesNotStealFromAnOuterHandler()
    {
        Assert.That(Run(@"
Function F() As String
    Try
        Try
            Throw New ArgumentException(""boom"")
        Catch e As FormatException
            Return ""INNER-WRONG""
        End Try
    Catch b As Exception
        Return ""OUTER""
    End Try
    Return ""fell-through""
End Function

Sub Main()
    Console.WriteLine(F())
End Sub
"), Is.EqualTo("OUTER"),
            "INNER-WRONG means the inner clause swallowed an exception its type excludes.");
    }

    /// <summary>
    /// The positive direction must still work: a real base-class clause DOES catch a derived
    /// throw. This is the case that passed even when broken, so it is a guard against the fix
    /// over-correcting into refusing everything, not evidence the fix works.
    /// </summary>
    [Test]
    public void ABaseClassClause_StillCatchesADerivedThrow()
    {
        Assert.That(Run(@"
Function F() As String
    Try
        Throw New ArgumentNullException(""boom"")
    Catch e As ArgumentException
        Return ""SUBCLASS""
    Catch b As Exception
        Return ""EX""
    End Try
End Function

Sub Main()
    Console.WriteLine(F())
End Sub
"), Is.EqualTo("SUBCLASS"));
    }
}
