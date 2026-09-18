using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A class declared AFTER the code that uses it — the same program, in the other order.
///
/// <para>⛔ Its members' TYPES did not resolve. The class TYPE did (an earlier change gives every
/// class its <c>TypeInfo</c> in pass 1), but its <c>Members</c> stayed empty until pass 2 reached
/// the declaration, so a use site above it read every member as Object. Measured on a plain
/// LITERAL field, so this was never about initializers: C++ emitted <c>void* t1; t1 = c-&gt;N;</c>
/// and failed to compile with "incompatible integer to pointer conversion assigning to 'void *'"
/// plus "no matching function for call to 'to_string'", and MSIL threw
/// <c>MissingFieldException: Field not found: 'Box.N'</c>. The identical file with the class FIRST
/// emitted <c>int32_t t1</c> and ran.</para>
///
/// <para>⛔ FIELD, METHOD and PROPERTY all three, identically — it was never one member kind's
/// problem. A <c>Private</c> member read from the class's own method broke too, and so did a
/// class-typed member whose type is declared later again, which failed EARLIER with
/// <c>BL6017: .NET type 'System.Object' has no accessible member named 'V'</c> — the front end had
/// already decided the member was Object and went looking for it on <c>System.Object</c>.</para>
///
/// <para>⚠ Constructors were NOT the gap and are not fixed here: their signatures have been
/// pre-registered in pass 1 since an earlier change, so <c>New Box(5)</c> resolved its arity in
/// either order. What still broke was reading <c>c.N</c> afterwards, which is the same member-type
/// gap — measured, both constructor cases below emitted 2 C++ errors before and 0 after, with the
/// constructor itself never at fault.</para>
///
/// <para>⚠ The fix registers class MEMBERS in pass 1, in their own sweep between the class-TYPE
/// sweep and the signature sweep, through the same helper the cross-file sibling path uses
/// (<c>PopulateClassMemberSignatures</c>) so the two cannot drift. Pass 2 overwrites every entry
/// with the fully resolved symbol, so pass 1 is a forward-reference stand-in, not a second source
/// of truth.</para>
///
/// <para>⚠ A class NESTED IN A MODULE is fixed too, by the same sweep's recursion — see
/// <see cref="AClassNestedInAModule_ResolvesItsMembers_WhicheverOrder"/>. This note previously
/// claimed the nested shape was STILL BROKEN. It was not: that measurement was taken against a
/// compiler binary still carrying the no-recursion mutation, because the mutation harness restores
/// the SOURCE without rebuilding. What was really missing was a TEST — which is why removing the
/// recursion passed the suite. Re-measured on a clean build, nested works in both orders, and
/// removing the recursion now fails.</para>
/// </summary>
[TestFixture]
public class ClassDeclarationOrderTests
{
    /// <summary>The class LAST — the order that was broken.</summary>
    private static string ClassAfter(string member, string main) => $"""
        Module M
         Sub Main()
          Dim c As New Box()
          {main}
         End Sub
        End Module

        Class Box
         {member}
        End Class
        """;

    /// <summary>The class nested INSIDE the module, declared after the code that uses it.</summary>
    private static string NestedAfter(string member, string main) => $"""
        Module M
         Sub Main()
          Dim c As New Box()
          {main}
         End Sub

         Class Box
          {member}
         End Class
        End Module
        """;

    /// <summary>The same nested class, declared before its use.</summary>
    private static string NestedBefore(string member, string main) => $"""
        Module M
         Class Box
          {member}
         End Class

         Sub Main()
          Dim c As New Box()
          {main}
         End Sub
        End Module
        """;

    /// <summary>The same program with the class FIRST — the order that already worked.</summary>
    private static string ClassBefore(string member, string main) => $"""
        Class Box
         {member}
        End Class

        Module M
         Sub Main()
          Dim c As New Box()
          {main}
         End Sub
        End Module
        """;

    /// <summary>
    /// ⛔ The headline, across all three member kinds and BOTH orders. The two orders asserted
    /// together is the point: the property under test is that declaration order does not change
    /// the answer, so a regression that breaks one order is caught even if the other still passes.
    ///
    /// <para>⚠ RUN, not merely compiled — on C++ a wrongly-typed temp is a BUILD failure, but on a
    /// backend that tolerates Object it would be a wrong value instead, and only running catches
    /// that.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Public N As Integer = 5", "PrintLine(CStr(c.N))", TestName = "Field")]
    [TestCase("Public Function Get5() As Integer\n  Return 5\n End Function", "PrintLine(CStr(c.Get5()))", TestName = "Method")]
    public void AMemberResolves_WhicheverOrderTheClassIsDeclaredIn(string member, string main)
    {
        Assert.Multiple(() =>
        {
            foreach (var (order, program) in new[]
                     {
                         ("class first", ClassBefore(member, main)),
                         ("class last", ClassAfter(member, main)),
                     })
            {
                Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("5\n"),
                    $"MSIL, {order}");
                Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("5"),
                    $"JavaScript, {order}");
                Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("5\n"),
                    $"C++, {order}");
            }
        });
    }

    /// <summary>
    /// ⚠ A PROPERTY, separately, because it needs a write before the read and so cannot share the
    /// table above. Same gap, same fix — its type read as Object in the class-last order too.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void APropertyResolves_WhicheverOrderTheClassIsDeclaredIn()
    {
        const string member = "Public Property P As Integer";
        const string main = "c.P = 5\n  PrintLine(CStr(c.P))";

        Assert.Multiple(() =>
        {
            foreach (var (order, program) in new[]
                     {
                         ("class first", ClassBefore(member, main)),
                         ("class last", ClassAfter(member, main)),
                     })
            {
                Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("5"), $"JavaScript, {order}");
                Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("5\n"),
                    $"C++, {order}");
            }
        });
    }

    /// <summary>
    /// ⛔ The class NESTED IN A MODULE, declared after its use. This is what holds the sweep's
    /// recursion into <c>ModuleNode</c> / <c>NamespaceNode</c> — without it the sweep only ever
    /// sees TOP-LEVEL classes, and a nested one falls back to the member-less type again:
    /// measured, removing that recursion emits <c>void* t1</c> and 2 C++ errors here while every
    /// top-level case stays green.
    ///
    /// <para>⚠ This fixture originally recorded the nested shape as STILL BROKEN after the fix.
    /// That was wrong, and the error is worth naming: the measurement was taken against a compiler
    /// binary that still carried the no-recursion mutation, because the mutation harness restores
    /// the SOURCE without rebuilding. Re-measured on a clean build, nested works in both orders —
    /// what was actually missing was this test.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Public N As Integer = 5", "PrintLine(CStr(c.N))", TestName = "Nested_Field")]
    [TestCase("Public Function Get5() As Integer\n   Return 5\n  End Function", "PrintLine(CStr(c.Get5()))", TestName = "Nested_Method")]
    public void AClassNestedInAModule_ResolvesItsMembers_WhicheverOrder(string member, string main)
    {
        Assert.Multiple(() =>
        {
            foreach (var (order, program) in new[]
                     {
                         ("nested, class last", NestedAfter(member, main)),
                         ("nested, class first", NestedBefore(member, main)),
                     })
            {
                Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("5"), $"JavaScript, {order}");
                Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("5\n"),
                    $"C++, {order}");
            }
        });
    }

    /// <summary>
    /// ⚠ The NAMESPACE arm of the same recursion. <c>RegisterClassMemberSignatures</c> walks
    /// Module and Namespace alike; a test that only nests in a Module leaves half of it unheld.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AClassNestedInANamespacedModule_ResolvesItsMembers()
    {
        const string program = """
            Namespace App
             Module M
              Sub Main()
               Dim c As New Box()
               PrintLine(CStr(c.N))
              End Sub

              Class Box
               Public N As Integer = 5
              End Class
             End Module
            End Namespace
            """;

        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("5\n"));
    }

    /// <summary>
    /// ⛔ The emitted C++ TEXT, because the defect was a TYPE and a run only sees its consequence.
    /// The temp holding <c>c.N</c> was <c>void*</c>; it must be <c>int32_t</c>. A test that only
    /// ran the program would pass on any backend that happened to tolerate the Object.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void TheTempHoldingTheMember_IsTypedFromTheMember_NotObject()
    {
        var cpp = BclE2E.CompileToCppOptimized(
            ClassAfter("Public N As Integer = 5", "PrintLine(CStr(c.N))"));

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Not.Contain("void* t1"),
                "the member's type decayed to Object:\n" + cpp);
            Assert.That(cpp, Does.Contain("int32_t t1"), cpp);
        });
    }

    /// <summary>
    /// ⚠ A PRIVATE member, read from the class's own method. This is what forces pass 1 to register
    /// EVERY access level rather than the non-private set the cross-file sibling path exposes —
    /// pass 2 has no access filter either, and pass 1 registering a different set from the one that
    /// overwrites it is its own bug. Measured: 2 C++ errors before, 0 after.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void APrivateMemberResolves_WhenTheClassIsDeclaredLast()
    {
        var program = ClassAfter(
            "Private _n As Integer = 5\n Public Function Read() As Integer\n  Return _n\n End Function",
            "PrintLine(CStr(c.Read()))");

        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("5\n"));
    }

    /// <summary>
    /// ⛔ A member whose TYPE is another class declared later again. This one failed EARLIER than
    /// the rest — not with bad codegen but with a front-end diagnostic,
    /// <c>BL6017: .NET type 'System.Object' has no accessible member named 'V'</c>: having decided
    /// <c>Inner</c> was Object, the analyzer went looking for <c>V</c> on <c>System.Object</c>.
    ///
    /// <para>⚠ It is also what pins the SWEEP ORDER: members are registered after every class TYPE
    /// exists, so a member typed by a class below resolves to the real class. Emitting
    /// <c>std::shared_ptr&lt;Leaf&gt; Inner;</c> is the observable form of that.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AMemberTypedByALaterClass_ResolvesToThatClass()
    {
        const string program = """
            Module M
             Sub Main()
              Dim o As New Outer()
              o.Inner = New Leaf()
              o.Inner.V = 5
              PrintLine(CStr(o.Inner.V))
             End Sub
            End Module

            Class Outer
             Public Inner As Leaf
            End Class

            Class Leaf
             Public V As Integer
            End Class
            """;

        var cpp = BclE2E.CompileToCppOptimized(program);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("std::shared_ptr<Leaf> Inner;"),
                "the member kept the real class type:\n" + cpp);
            Assert.That(BclE2E.CompileRun(cpp), Is.EqualTo("5\n"));
        });
    }

    /// <summary>
    /// ⚠ Constructors were ALREADY pre-registered in pass 1 and must stay owned by
    /// <c>RegisterConstructorSignature</c> — the new sweep deliberately does not write
    /// <c>.ctorN</c>, because that method returns early on an existing key and would silently hand
    /// constructor resolution to a different, less capable parameter builder.
    ///
    /// <para>⛔ These still FAILED before this change, and not because of the constructor: the
    /// arity resolved fine in either order, then reading <c>c.N</c> afterwards hit the same
    /// member-type gap. 2 C++ errors before, 0 after, for both the plain and the Optional
    /// case.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("Public Sub New(v As Integer)\n  N = v\n End Sub", "New Box(5)", TestName = "Ctor_Plain")]
    [TestCase("Public Sub New(Optional v As Integer = 5)\n  N = v\n End Sub", "New Box()", TestName = "Ctor_Optional")]
    public void AConstructedObjectsMemberResolves_WhenTheClassIsDeclaredLast(string ctor, string construct)
    {
        var program = $"""
            Module M
             Sub Main()
              Dim c As {construct}
              PrintLine(CStr(c.N))
             End Sub
            End Module

            Class Box
             Public N As Integer
             {ctor}
            End Class
            """;

        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)), Is.EqualTo("5\n"));
    }

    // ⚠ That pass 1 now touches each class TWICE (type, then members) must not swallow the
    // duplicate-class diagnostic. That is NOT re-asserted here: OptionalConstructorTests already
    // pins it, and more tightly than a copy here would — it checks both the message and that it is
    // reported at the SECOND declaration's line.
}
