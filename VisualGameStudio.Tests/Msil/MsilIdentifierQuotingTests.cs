using System.Linq;
using NUnit.Framework;
using static VisualGameStudio.Tests.Msil.MsilHarness;

namespace VisualGameStudio.Tests.Msil;

/// <summary>
/// A BasicLang name that happens to be an IL keyword, which ilasm otherwise refuses.
///
/// <para>⛔ <c>Dim neg As Integer = 1</c> compiled "successfully" and then would not assemble:
/// <c>syntax error at token 'neg' in: [0] int32 neg</c>. Nothing on the BasicLang side said
/// anything, because the backend writes IL text and never sees ilasm's verdict.</para>
///
/// <para>⛔ SCANNED, not guessed at: of 90 IL keyword candidates written as a plain
/// <c>Dim … As Integer</c>, 8 are not BasicLang identifiers at all and <b>64 of the remaining 82
/// made ilasm reject the program</b>. They are not exotic — <c>value</c>, <c>add</c>,
/// <c>call</c>, <c>method</c>, <c>field</c>, <c>filter</c>, <c>handler</c>, <c>custom</c>,
/// <c>break</c>, <c>switch</c>, <c>box</c>, <c>literal</c>, <c>native</c>, <c>sealed</c>. The 14
/// that passed (<c>file</c>, <c>hash</c>, <c>ldc</c>, <c>tail</c>, <c>volatile</c>, …) are what
/// makes a hand-written keyword list the wrong fix: the set is large, context-sensitive and not
/// readable as data, so a list drifts and being wrong by one word costs a program that does not
/// assemble. Every user-chosen name is quoted instead.</para>
///
/// <para>⚠ EVERY position was affected, measured with <c>value</c>: local, parameter, method name,
/// class name, field, module-level global and property.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class MsilIdentifierQuotingTests
{
    // ====================================================================================
    // The headline, and the spread behind it.
    // ====================================================================================

    /// <summary>
    /// ⛔ The defect as found: one local, named <c>neg</c>.
    /// </summary>
    [Test]
    public void ALocalNamedAfterAnIlKeyword_Assembles()
    {
        Assert.That(RunExpectingSuccess("""
            Module M
             Sub Main()
              Dim neg As Integer = 1
              PrintLine(CStr(neg))
             End Sub
            End Module
            """), Is.EqualTo("1\n"));
    }

    /// <summary>
    /// ⚠ A spread across the kinds of keyword that collide — an opcode (<c>neg</c>, <c>pop</c>),
    /// a type/modifier word (<c>native</c>, <c>sealed</c>, <c>literal</c>), a directive word
    /// (<c>custom</c>, <c>handler</c>) and the one the backend had already hit by hand
    /// (<c>value</c>). Each is an ordinary variable that did not assemble.
    /// </summary>
    [TestCase("value")]
    [TestCase("add")]
    [TestCase("call")]
    [TestCase("method")]
    [TestCase("field")]
    [TestCase("box")]
    [TestCase("switch")]
    [TestCase("break")]
    [TestCase("literal")]
    [TestCase("native")]
    [TestCase("sealed")]
    [TestCase("handler")]
    [TestCase("custom")]
    [TestCase("pop")]
    public void AnyIlKeyword_IsUsableAsALocal(string name)
    {
        Assert.That(RunExpectingSuccess($"""
            Module M
             Sub Main()
              Dim {name} As Integer = 7
              PrintLine(CStr({name} * 2))
             End Sub
            End Module
            """), Is.EqualTo("14\n"));
    }

    // ====================================================================================
    // Every position that prints a user-chosen name.
    // ====================================================================================

    /// <summary>
    /// ⚠ The other positions, each measured broken before: a parameter, a method name, a class
    /// name, an instance field and a module-level global.
    ///
    /// <para>⛔ A PROPERTY named <c>value</c> is deliberately absent. It is still refused, but for
    /// an unrelated and PRE-EXISTING reason: the backend writes a <c>.property</c> block whose
    /// <c>.get</c>/<c>.set</c> name accessor methods it never emits, so ilasm answers
    /// "Invalid Set method of property". Verified on the parent commit with an ordinary name
    /// (<c>Public Property Alpha As Integer</c>) — it fails there too. Asserting it here would
    /// pin someone else's defect to this fixture.</para>
    /// </summary>
    [Test]
    public void EveryOtherNamePosition_Assembles()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RunExpectingSuccess("""
                Module M
                 Sub Take(value As Integer)
                  PrintLine("arg=" & CStr(value))
                 End Sub
                 Sub Main()
                  Take(7)
                 End Sub
                End Module
                """), Is.EqualTo("arg=7\n"), "parameter");

            Assert.That(RunExpectingSuccess("""
                Module M
                 Function value() As Integer
                  Return 7
                 End Function
                 Sub Main()
                  PrintLine(CStr(value()))
                 End Sub
                End Module
                """), Is.EqualTo("7\n"), "method name");

            Assert.That(RunExpectingSuccess("""
                Class value
                 Public N As Integer
                End Class

                Module M
                 Sub Main()
                  Dim c As New value()
                  c.N = 7
                  PrintLine(CStr(c.N))
                 End Sub
                End Module
                """), Is.EqualTo("7\n"), "class name");

            Assert.That(RunExpectingSuccess("""
                Class Box
                 Public value As Integer
                End Class

                Module M
                 Sub Main()
                  Dim c As New Box()
                  c.value = 7
                  PrintLine(CStr(c.value))
                 End Sub
                End Module
                """), Is.EqualTo("7\n"), "instance field");

            Assert.That(RunExpectingSuccess("""
                Module M
                 Dim value As Integer = 7
                 Sub Main()
                  PrintLine(CStr(value))
                 End Sub
                End Module
                """), Is.EqualTo("7\n"), "module-level global");
        });
    }

    // ====================================================================================
    // The two tables the quoting must NOT reach.
    // ====================================================================================

    /// <summary>
    /// ⛔ <c>_localIndices</c> is keyed by the name the IR uses, not the name the IL prints, and
    /// two lookups miss it in opposite ways when that is confused:
    ///
    /// <list type="bullet">
    /// <item>A CATCH variable that is not found throws at generation time —
    /// "the catch variable 'ex' has no local slot" — so it is loud.</item>
    /// <item>An ARRAY local that is not found is <b>silent</b>: <c>EmitArrayLocalAllocations</c>
    /// simply <c>continue</c>s, the <c>newarr</c> is never emitted, the slot stays null and the
    /// program dies at run time with a NullReferenceException on first use.</item>
    /// </list>
    ///
    /// <para>Both are exercised with a KEYWORD name, so the test covers the quoting and the key
    /// at the same time.</para>
    /// </summary>
    [Test]
    public void TheLocalsTable_IsStillKeyedByTheIrName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RunExpectingSuccess("""
                Module M
                 Sub Main()
                  Try
                   Throw New Exception("boom")
                  Catch value As Exception
                   PrintLine("caught:" & value.Message)
                  End Try
                 End Sub
                End Module
                """), Is.EqualTo("caught:boom\n"), "catch variable");

            Assert.That(RunExpectingSuccess("""
                Module M
                 Sub Main()
                  Dim switch(3) As Integer
                  switch(0) = 7
                  switch(1) = 9
                  PrintLine(CStr(switch(0) + switch(1)))
                 End Sub
                End Module
                """), Is.EqualTo("16\n"), "array local — a missed key here is SILENT");
        });
    }

    /// <summary>
    /// ⛔ A TYPE is not a user identifier and must not be quoted. <c>MapTypeName</c> is reached
    /// with names that are ALREADY IL spellings — something maps a type and feeds the result back
    /// in — which was a harmless no-op only while its fallback was the identity. Once the fallback
    /// quoted, <c>int32</c> came back as <c>'int32'</c>, <c>IlTypeSpec</c> stopped recognizing it
    /// as a primitive, and <c>Dim n As Integer</c> declared <c>[0] class 'int32' 'n'</c> — a local
    /// whose type is a class that does not exist.
    /// </summary>
    [Test]
    public void APrimitiveType_IsNotQuoted()
    {
        var il = CompileToIl("""
            Module M
             Sub Main()
              Dim n As Integer = 1
              PrintLine(CStr(n))
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain("[0] int32 'n'"),
                "the NAME is quoted and the TYPE is not:\n" + il);
            Assert.That(il, Does.Not.Contain("'int32'"),
                "an IL primitive keyword must never be quoted — it would become a class "
                + "reference:\n" + il);
        });
    }

    /// <summary>
    /// ⛔ The entry point is decided from the name the IR uses. Asking the QUOTED name whether it
    /// is "Main" is always false, and ilasm then rejects the whole assembly with
    /// "No entry point declared for executable" — every program, not just one with a keyword in
    /// it, which is why this is asserted structurally rather than left to the run tests.
    /// </summary>
    [Test]
    public void TheEntryPoint_IsStillMarked()
    {
        var il = CompileToIl("""
            Module M
             Sub Main()
              PrintLine("hi")
             End Sub
            End Module
            """);

        Assert.That(il.Split('\n').Count(l => l.Trim() == ".entrypoint"), Is.EqualTo(1),
            "exactly one .entrypoint:\n" + il);
    }

    /// <summary>
    /// ⛔ A COMPOSED name is one identifier, so the quotes go around the whole thing or nowhere:
    /// <c>get_'Alpha'</c> is not an identifier at all. This is asserted STRUCTURALLY and not by
    /// running, because a property does not assemble on this backend for an unrelated,
    /// pre-existing reason — the <c>.property</c> block names accessor methods the backend never
    /// emits, so ilasm answers "Invalid Set method of property" for an ordinary name too
    /// (verified on the parent commit). The IL text is the only thing this can hold.
    ///
    /// <para>⚠ The accessor names themselves are deliberately NOT quoted: <c>get_Alpha</c> begins
    /// with <c>get_</c> and so can never be an IL keyword. The property's own name is.</para>
    /// </summary>
    [Test]
    public void AComposedAccessorName_IsOneIdentifier()
    {
        var il = CompileToIl("""
            Class Box
             Public Property Alpha As Integer
            End Class

            Module M
             Sub Main()
              PrintLine("hi")
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(il, Does.Contain(".property instance int32 'Alpha'()"),
                "the property's own name is a user name and is quoted:\n" + il);
            Assert.That(il, Does.Contain("::get_Alpha()"),
                "and the accessor built from it is one unquoted identifier:\n" + il);
            Assert.That(il, Does.Not.Contain("get_'Alpha'"),
                "quoting the tail of a composed name produces something that is not an "
                + "identifier:\n" + il);
        });
    }

    // ====================================================================================
    // Why quoting everything is safe.
    // ====================================================================================

    /// <summary>
    /// ⚠ Quoting is PURELY LEXICAL: <c>'X'</c> and <c>X</c> are the same identifier to ilasm, so a
    /// quoted REFERENCE resolves to an unquoted DECLARATION. That is the property that makes
    /// "quote everything" safe rather than a matching hazard — no reference has to be kept in step
    /// with its declaration, and a site the quoting misses still resolves.
    ///
    /// <para>This is a real instance of it rather than a contrived one: the emitted call is
    /// <c>call int32 [mscorlib]System.Math::'Abs'(int32)</c>, and the method it binds is declared
    /// in mscorlib, which knows nothing about quotes. If quoting were anything but lexical this
    /// would not resolve.</para>
    /// </summary>
    [Test]
    public void AQuotedReference_BindsAnUnquotedDeclaration()
    {
        const string program = """
            Module M
             Sub Main()
              PrintLine(CStr(Math.Abs(-7)))
             End Sub
            End Module
            """;

        Assert.Multiple(() =>
        {
            Assert.That(CompileToIl(program), Does.Contain("System.Math::'Abs'(int32)"),
                "the reference is quoted; the declaration in mscorlib is not");
            Assert.That(RunExpectingSuccess(program), Is.EqualTo("7\n"),
                "and it still binds");
        });
    }
}
