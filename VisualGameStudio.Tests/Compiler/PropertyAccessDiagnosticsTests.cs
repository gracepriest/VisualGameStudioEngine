using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.LSP;
using BasicLang.Compiler.SemanticAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #178 — the front end's own two diagnostics: BC30526 ("Property 'P' is 'ReadOnly'.") at
/// every WRITE of a ReadOnly property, and BC30524 ("Property 'W' is 'WriteOnly'.") at every READ
/// of a WriteOnly one. Fast subset — <see cref="Analyze"/> parses and semantically analyzes only,
/// no IR, no backend, no process. See <c>PropertyAccessExecutionTests</c> (Integration) for the
/// same probes taken to a running program on all four backends, and
/// <c>MsilInterfacePropertyTests.MsilInterfacePropertyCompileTests</c> for the two pins this fix
/// moved (the MSIL <c>ForeignFeatureException</c> backstop).
///
/// <para>Fixed shapes throughout: the assignment/read line is written to be the exact line
/// asserted, so a diagnostic landing anywhere else (a receiver, a different statement) is caught
/// by the line check, not only by the code/message check.</para>
/// </summary>
[TestFixture]
public class PropertyAccessDiagnosticsTests
{
    // ====================================================================================
    // Shared helpers.
    // ====================================================================================

    private static List<SemanticError> Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.ToList();
    }

    /// <summary>Asserts exactly the diagnostic BC30526/BC30524 promise: the CODE, the PROPERTY
    /// NAME in the message, and the LINE it lands on.</summary>
    private static void AssertRefused(string source, string code, string propertyName, int line)
    {
        var errors = Analyze(source);
        var match = errors.FirstOrDefault(e => e.ErrorCode == code);
        Assert.That(match, Is.Not.Null,
            $"expected {code} for '{propertyName}'; got: "
            + string.Join(" | ", errors.Select(e => $"{e.ErrorCode}:{e.Message}")));
        Assert.That(match!.Message, Does.Contain($"'{propertyName}'"), "property name in message: " + match.Message);
        Assert.That(match.Line, Is.EqualTo(line), "diagnostic line: " + match.Message);
    }

    /// <summary>Asserts NEITHER BC30526 nor BC30524 fires anywhere in the program — the
    /// mutant-killing "legal" half of the contract.</summary>
    private static void AssertNoPropertyAccessDiagnostic(string source)
    {
        var errors = Analyze(source);
        var stray = errors.Where(e => e.ErrorCode is "BC30526" or "BC30524").ToList();
        Assert.That(stray, Is.Empty,
            "expected no BC30526/BC30524; got: " + string.Join(" | ", stray.Select(e => $"{e.ErrorCode}:{e.Message}")));
    }

    // ====================================================================================
    // BC30526 — a write to a ReadOnly property.
    // ====================================================================================

    [Test]
    public void Write_ObjDotP_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
        End Class

        Module M
            Sub Main()
                Dim o As New C()
                o.P = 99
            End Sub
        End Module
        """, "BC30526", "P", 12);

    [Test]
    public void Write_MeDotP_InsideAnOrdinaryMethod_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
            Public Sub Touch()
                Me.P = 5
            End Sub
        End Class
        """, "BC30526", "P", 8);

    [Test]
    public void Write_BareP_InsideAnOrdinaryMethod_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
            Public Sub Touch()
                P = 5
            End Sub
        End Class
        """, "BC30526", "P", 8);

    [Test]
    public void Write_ClassDotSharedS_IsRefused() => AssertRefused("""
        Class C
            Public Shared ReadOnly Property S As Integer
                Get
                    Return 1
                End Get
            End Property
        End Class

        Module M
            Sub Main()
                C.S = 9
            End Sub
        End Module
        """, "BC30526", "S", 11);

    [Test]
    public void Write_BareP_InheritedFromBase_IsRefused() => AssertRefused("""
        Class Base
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
        End Class

        Class Derived
            Inherits Base
            Public Sub Touch()
                P = 2
            End Sub
        End Class
        """, "BC30526", "P", 12);

    [Test]
    public void Write_MyBaseDotP_IsRefused() => AssertRefused("""
        Class Base
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
        End Class

        Class Derived
            Inherits Base
            Public Sub Touch()
                MyBase.P = 2
            End Sub
        End Class
        """, "BC30526", "P", 12);

    /// <summary>A6 — through an interface receiver, ReadOnly on both the interface and the
    /// implementing class.</summary>
    [Test]
    public void Write_ThroughInterfaceReceiver_ReadOnlyOnBothSides_IsRefused() => AssertRefused("""
        Interface IShape
            ReadOnly Property P As Integer
        End Interface

        Class C
            Implements IShape
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
        End Class

        Module M
            Sub Main()
                Dim o As IShape = New C()
                o.P = 99
            End Sub
        End Module
        """, "BC30526", "P", 17);

    /// <summary>A6b — ReadOnly on the INTERFACE but ReadWrite on the implementing CLASS: still
    /// refused through the interface receiver, because the receiver's static type decides (VB's
    /// own rule). Contrast <see cref="Write_ReadWritePropertyThroughClassReceiver_WhoseInterfaceIsReadOnly_IsLegal"/>
    /// (L6) below — the same class, through the CLASS receiver instead.</summary>
    [Test]
    public void Write_ThroughInterfaceReceiver_ReadOnlyOnInterface_ReadWriteOnClass_IsRefused() => AssertRefused("""
        Interface IShape
            ReadOnly Property P As Integer
        End Interface

        Class C
            Implements IShape
            Public Property P As Integer
        End Class

        Module M
            Sub Main()
                Dim o As IShape = New C()
                o.P = 99
            End Sub
        End Module
        """, "BC30526", "P", 13);

    [Test]
    public void Write_CompoundPlusEquals_OnReadOnly_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
            Public Sub Touch()
                P += 5
            End Sub
        End Class
        """, "BC30526", "P", 8);

    [Test]
    public void Write_PlusPlus_OnReadOnly_IsRefused() => AssertRefused("""
        Class C
            Private _v As Integer = 42
            Public ReadOnly Property P As Integer
                Get
                    Return _v
                End Get
            End Property
        End Class

        Module M
            Sub Main()
                Dim o As New C()
                o.P++
            End Sub
        End Module
        """, "BC30526", "P", 13);

    /// <summary>A8 — a ReadOnly AUTO-property written from OUTSIDE the class entirely.</summary>
    [Test]
    public void Write_ReadOnlyAutoProperty_FromOutsideTheClass_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property P As Integer
        End Class

        Module M
            Sub Main()
                Dim o As New C()
                o.P = 99
            End Sub
        End Module
        """, "BC30526", "P", 8);

    /// <summary>A10 — a ReadOnly auto-property written in a NON-constructor method of its own
    /// class.</summary>
    [Test]
    public void Write_ReadOnlyAutoProperty_InAnOrdinaryMethod_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property P As Integer
            Public Sub Touch()
                P = 5
            End Sub
        End Class
        """, "BC30526", "P", 4);

    /// <summary>A9 — a ReadOnly auto-property written in a DERIVED class's own constructor. The
    /// carve-out is the DECLARING class's constructor only.</summary>
    [Test]
    public void Write_ReadOnlyAutoProperty_InADerivedClassConstructor_IsRefused() => AssertRefused("""
        Class Base
            Public ReadOnly Property P As Integer
        End Class

        Class Derived
            Inherits Base
            Public Sub New()
                P = 2
            End Sub
        End Class
        """, "BC30526", "P", 8);

    /// <summary>A lambda written INSIDE the constructor is its own function scope, not the
    /// constructor's — C#'s own rule too (CS0200).</summary>
    [Test]
    public void Write_ReadOnlyAutoProperty_InALambdaInsideSubNew_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property P As Integer
            Public Sub New()
                Dim f As Action = Sub()
                    P = 1
                End Sub
                f()
            End Sub
        End Class
        """, "BC30526", "P", 5);

    /// <summary>Another instance of the SAME class, inside the constructor: the carve-out is
    /// bare/<c>Me.</c> only, never <c>obj.</c>.</summary>
    [Test]
    public void Write_AnotherInstance_InsideSubNew_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property P As Integer
            Public Sub New(other As C)
                other.P = 1
            End Sub
        End Class
        """, "BC30526", "P", 4);

    /// <summary>E25 — a ReadOnly property WITH A GET BODY (not an auto-property) assigned in
    /// its own constructor: the carve-out is for an auto-property only, so this is still
    /// refused.</summary>
    [Test]
    public void Write_GetBodiedReadOnlyProperty_InSubNew_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
            Public Sub New()
                P = 2
            End Sub
        End Class
        """, "BC30526", "P", 8);

    [Test]
    public void Write_WithDotP_OnReadOnly_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
        End Class

        Module M
            Sub Main()
                Dim o As New C()
                With o
                    .P = 1
                End With
            End Sub
        End Module
        """, "BC30526", "P", 13);

    [Test]
    public void Write_ReDim_OnAReadOnlyArrayProperty_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property Arr As Integer()
            Public Sub Grow()
                ReDim Arr(5)
            End Sub
        End Class
        """, "BC30526", "Arr", 4);

    [Test]
    public void Write_BareForLoop_OverAReadOnlyProperty_IsRefused() => AssertRefused("""
        Class C
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
            Public Sub Loop3()
                For P = 1 To 3
                Next
            End Sub
        End Class
        """, "BC30526", "P", 8);

    // ====================================================================================
    // BC30524 — a read of a WriteOnly property.
    // ====================================================================================

    [Test]
    public void Read_InAnExpression_IsRefused() => AssertRefused("""
        Class C
            Public WriteOnly Property W As Integer
                Set(value As Integer)
                End Set
            End Property
        End Class

        Module M
            Sub Main()
                Dim o As New C()
                Dim x As Integer = o.W
            End Sub
        End Module
        """, "BC30524", "W", 11);

    [Test]
    public void Read_AsAWriteLineArgument_IsRefused() => AssertRefused("""
        Class C
            Public WriteOnly Property W As Integer
                Set(value As Integer)
                End Set
            End Property
        End Class

        Module M
            Sub Main()
                Dim o As New C()
                Console.WriteLine(o.W)
            End Sub
        End Module
        """, "BC30524", "W", 11);

    [Test]
    public void Read_CompoundPlusEquals_ReadHalf_IsRefused() => AssertRefused("""
        Class C
            Public WriteOnly Property W As Integer
                Set(value As Integer)
                End Set
            End Property
        End Class

        Module M
            Sub Main()
                Dim o As New C()
                o.W += 1
            End Sub
        End Module
        """, "BC30524", "W", 11);

    [Test]
    public void Read_BareW_InsideTheClass_IsRefused() => AssertRefused("""
        Class C
            Public WriteOnly Property W As Integer
                Set(value As Integer)
                End Set
            End Property
            Public Sub Touch()
                Dim x As Integer = W
            End Sub
        End Class
        """, "BC30524", "W", 7);

    [Test]
    public void Read_ThroughInterfaceReceiver_IsRefused() => AssertRefused("""
        Interface IShape
            WriteOnly Property W As Integer
        End Interface

        Class C
            Implements IShape
            Public WriteOnly Property W As Integer
                Set(value As Integer)
                End Set
            End Property
        End Class

        Module M
            Sub Main()
                Dim o As IShape = New C()
                Console.WriteLine(o.W)
            End Sub
        End Module
        """, "BC30524", "W", 16);

    [Test]
    public void Read_InAnIfCondition_IsRefused() => AssertRefused("""
        Class C
            Public WriteOnly Property W As Integer
                Set(value As Integer)
                End Set
            End Property
            Public Sub Touch()
                If W > 0 Then
                End If
            End Sub
        End Class
        """, "BC30524", "W", 7);

    [Test]
    public void Read_AsAConcatOperand_IsRefused() => AssertRefused("""
        Class C
            Public WriteOnly Property W As Integer
                Set(value As Integer)
                End Set
            End Property
            Public Sub Touch()
                Dim s As String = "x" & W
            End Sub
        End Class
        """, "BC30524", "W", 7);

    [Test]
    public void Read_AsAnArgument_IsRefused() => AssertRefused("""
        Class C
            Public WriteOnly Property W As Integer
                Set(value As Integer)
                End Set
            End Property
            Public Sub Touch()
                Console.WriteLine(W)
            End Sub
        End Class
        """, "BC30524", "W", 7);

    /// <summary>E17 — <c>o.W.X</c>: the WRITE lands on <c>.X</c> (an ordinary field, legal), but
    /// <c>o.W</c> is still a READ of the WriteOnly property nested inside that target — only the
    /// OUTERMOST target node of a plain <c>=</c> is exempted from the read check.</summary>
    [Test]
    public void Read_OfAChainedMemberAccess_OWDotX_IsRefused() => AssertRefused("""
        Class Box
            Public X As Integer
        End Class

        Class C
            Public WriteOnly Property W As Box
                Set(value As Box)
                End Set
            End Property
        End Class

        Module M
            Sub Main()
                Dim o As New C()
                o.W.X = 1
            End Sub
        End Module
        """, "BC30524", "W", 15);

    // ====================================================================================
    // Legal — NO diagnostic. Each one kills a widening mutant (see mut-results.txt).
    // ====================================================================================

    [Test]
    public void Legal_ReadOnlyAutoProperty_AssignedBare_InItsOwnSubNew() => AssertNoPropertyAccessDiagnostic("""
        Class C
            Public ReadOnly Property P As Integer
            Public Sub New()
                P = 42
            End Sub
        End Class
        """);

    [Test]
    public void Legal_ReadOnlyAutoProperty_AssignedViaMe_InItsOwnSubNew() => AssertNoPropertyAccessDiagnostic("""
        Class C
            Public ReadOnly Property P As Integer
            Public Sub New()
                Me.P = 42
            End Sub
        End Class
        """);

    /// <summary>Legal, and since #208 it RUNS on every backend: the Shared Sub New is the class's type initializer, so
    /// `S` is 11 at its first read (`SharedConstructorExecutionTests` P10). It used to be emitted as an instance
    /// constructor that never ran on a Shared access. The front end's job here is only whether the WRITE is refused,
    /// and VB accepts it.</summary>
    [Test]
    public void Legal_SharedReadOnlyAutoProperty_AssignedInSharedSubNew() => AssertNoPropertyAccessDiagnostic("""
        Class C
            Public Shared ReadOnly Property S As Integer
            Shared Sub New()
                S = 11
            End Sub
        End Class
        """);

    /// <summary>L6 — the same class as A6b's, but through the CLASS receiver: the receiver's
    /// static type decides, and a class-typed receiver sees the class's own ReadWrite
    /// declaration.</summary>
    [Test]
    public void Write_ReadWritePropertyThroughClassReceiver_WhoseInterfaceIsReadOnly_IsLegal() => AssertNoPropertyAccessDiagnostic("""
        Interface IShape
            ReadOnly Property P As Integer
        End Interface

        Class C
            Implements IShape
            Public Property P As Integer
        End Class

        Module M
            Sub Main()
                Dim o As New C()
                o.P = 99
            End Sub
        End Module
        """);

    /// <summary>L2 — a ReadOnly property passed ByRef is not a WRITE (VB passes a copy and skips
    /// the write-back; task #209 made every backend do that, see <c>PropertyByRefCopyOutExecutionTests</c> — this test is only about the diagnostic).</summary>
    [Test]
    public void Legal_ReadOnlyPropertyPassedByRef() => AssertNoPropertyAccessDiagnostic("""
        Class C
            Public ReadOnly Property P As Integer
                Get
                    Return 42
                End Get
            End Property
        End Class

        Module M
            Sub Bump(ByRef x As Integer)
                x = x + 1
            End Sub
            Sub Main()
                Dim o As New C()
                Bump(o.P)
            End Sub
        End Module
        """);

    /// <summary>Control (L3-shaped): a WriteOnly write, a ReadOnly read, a ReadWrite compound,
    /// and a field — none of these may ever be refused.</summary>
    [Test]
    public void Legal_WriteOnlyWrite_ReadOnlyRead_ReadWriteCompound_AndAField() => AssertNoPropertyAccessDiagnostic("""
        Class C
            Public WriteOnly Property W As Integer
                Set(value As Integer)
                End Set
            End Property
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
            Public Property RW As Integer
            Public F As Integer
        End Class

        Module M
            Sub Main()
                Dim o As New C()
                o.W = 1
                Dim x As Integer = o.P
                o.RW += 1
                o.F += 1
            End Sub
        End Module
        """);

    /// <summary>E09 — inside a property's own Get, VB binds a bare `P` to the accessor's implicit
    /// RETURN VARIABLE, not to the property — so `P = v` is never BC30526. Task #178 exempted it
    /// with a carve-out (<c>IsGetterReturnVariable</c>); task #219 replaced the carve-out by the
    /// binding itself (<c>SemanticAnalyzer.AsReturnVariable</c>): the reference is a Local, not a
    /// property, so the check never sees it — in the Get's own body and in a lambda written there,
    /// as vbc accepts. The program also RUNS now: <c>PropertyAccessExecutionTests.E09_…</c> and
    /// <c>ImplicitReturnVariableExecutionTests</c>.</summary>
    [Test]
    public void Legal_AssignmentToPInsideItsOwnGet_IsTheGetterReturnVariable() => AssertNoPropertyAccessDiagnostic("""
        Class Ctx
            Private _v As Integer = 42
            Public ReadOnly Property P As Integer
                Get
                    P = _v + 1
                End Get
            End Property
        End Class
        """);

    /// <summary>E10 — a plain <c>ReDim</c> (no <c>Preserve</c>) REPLACES the array, so it does not
    /// READ it, and a WriteOnly array property may be ReDim'd.</summary>
    [Test]
    public void Legal_PlainReDim_OfAWriteOnlyArrayProperty_WithoutPreserve() => AssertNoPropertyAccessDiagnostic("""
        Class Ctx
            Private _a() As Integer
            Public WriteOnly Property W As Integer()
                Set(value As Integer())
                    _a = value
                End Set
            End Property
            Public Sub Grow()
                ReDim W(5)
            End Sub
        End Class
        """);

    // ====================================================================================
    // LSP — the diagnostic surfaces through the LSP's own diagnostics path, with a REAL span
    // on the use line (not (0,0), and not only present in the CLI's console text).
    // ====================================================================================

    [Test]
    public void Lsp_WriteToReadOnlyProperty_SurfacesWithARealSpanOnTheUseLine()
    {
        var documentManager = new DocumentManager();
        var uri = DocumentUri.From("untitled:PropAccessWrite.bas");
        var state = documentManager.UpdateDocument(uri, """
            Class C
                Public ReadOnly Property P As Integer
                    Get
                        Return 1
                    End Get
                End Property
                Public Sub Touch()
                    P = 5
                End Sub
            End Class
            """);

        var match = state.Diagnostics.FirstOrDefault(d => d.Message.Contains("BC30526"));
        Assert.That(match, Is.Not.Null,
            "expected a BC30526 LSP diagnostic; got: " + string.Join(" | ", state.Diagnostics.Select(d => d.Message)));
        Assert.That(match!.Message, Does.Contain("'P'"));
        Assert.That(match.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(match.Line, Is.EqualTo(8), "the use line, not (0,0) or the declaration");
        Assert.That(match.Column, Is.GreaterThan(0), "a real column, not a placeholder");
    }

    [Test]
    public void Lsp_ReadOfWriteOnlyProperty_SurfacesWithARealSpanOnTheUseLine()
    {
        var documentManager = new DocumentManager();
        var uri = DocumentUri.From("untitled:PropAccessRead.bas");
        var state = documentManager.UpdateDocument(uri, """
            Class C
                Public WriteOnly Property W As Integer
                    Set(value As Integer)
                    End Set
                End Property
                Public Sub Touch()
                    Console.WriteLine(W)
                End Sub
            End Class
            """);

        var match = state.Diagnostics.FirstOrDefault(d => d.Message.Contains("BC30524"));
        Assert.That(match, Is.Not.Null,
            "expected a BC30524 LSP diagnostic; got: " + string.Join(" | ", state.Diagnostics.Select(d => d.Message)));
        Assert.That(match!.Message, Does.Contain("'W'"));
        Assert.That(match.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(match.Line, Is.EqualTo(7), "the use line");
        Assert.That(match.Column, Is.GreaterThan(0), "a real column, not a placeholder");
    }
}
