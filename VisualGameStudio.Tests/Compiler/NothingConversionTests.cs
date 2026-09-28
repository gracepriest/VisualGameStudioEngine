using BasicLang.Compiler;                  // Lexer, Parser
using BasicLang.Compiler.AST;              // the node types
using BasicLang.Compiler.SemanticAnalysis; // SemanticAnalyzer, TypeInfo, ErrorSeverity
using BasicLang.Compiler.IR;               // IRBuilder etc.
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #173 (fix commit c0b457d9) — <c>Nothing</c> converts to any REFERENCE type at every
/// conversion site, following VB. Before this, the <c>Nothing</c> literal typed <c>Object</c>
/// and every site ran its own <c>IsAssignableFrom</c>, refusing it into anything but
/// <c>Object</c> — <c>Dim f As Action = Nothing</c> ("cannot convert 'Object' to 'Action'") was
/// the filed case, but the same refusal hit every other site.
///
/// <para><c>JudgeNothingConversion</c> is now the ONE answer, asked at all NINE call sites:
/// a <c>Dim</c> initializer (local / field / module-level, one code path —
/// SemanticAnalyzer.cs ~6200), an assignment (variable / field / property set / array element,
/// one code path — ~8959), a user-call argument (~10223), a delegate-invocation argument
/// (~10104), a <c>New</c> argument (~10652), a <c>MyBase.New</c> argument (~6778), <c>Return</c>
/// (~8823), an <c>Optional</c> default (~6101), and the typed array literal's own element rule
/// (<c>CheckTypedLiteralElement</c>, ~7360 — pinned separately in
/// <see cref="TypedArrayLiteralTests.TypedLiteral_Nothing"/> and its sibling rows; the last test
/// below re-pins the ONE-answer invariant between that site and this fixture's).</para>
///
/// <para>It is built on <c>NothingAdviceFor</c>: null for a reference (or unresolvable .NET)
/// target, which admits <c>Nothing</c>; a value-type-specific message otherwise. #173 added four
/// new arms — the P1 native structs (DateTime/TimeSpan/Guid, NativeOwned reference-typed structs
/// StringBuilder excepted), a type parameter, a tuple, and <c>Union</c> — each measured wrong
/// once admitted (CS0037 on C#, a clang error on C++, a silent <c>null</c> on JavaScript for the
/// P1 structs; CS0403 for a type parameter).</para>
/// </summary>
[TestFixture]
public class NothingConversionTests
{
    private static (bool ok, List<string> errors) Analyze(string body, string prelude = "")
    {
        var source = (prelude.Length > 0 ? prelude + "\n" : "") + body;
        var parser = new Parser(new Lexer(source).Tokenize());
        var program = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(program);
        // ⛔ ONE list, two severities (TypedArrayLiteralTests' own note): the analyzer has no
        // `Warnings` member, so `Errors` must be filtered by severity to mean anything.
        var errors = analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).Select(e => e.Message).ToList();
        return (ok, errors);
    }

    // ============================================================================================
    // 1. Every site admits Nothing into a reference type (data-driven, per site).
    // ============================================================================================

    private const string Prelude =
        "Class C\nEnd Class\nInterface I\nEnd Interface\nDelegate Sub UserDel(x As Integer)\n";

    // ---- Dim: local, class field, module-level — one code path (SemanticAnalyzer ~6200) --------

    [TestCase("Sub Main()\nDim o As C = Nothing\nEnd Sub", "class")]
    [TestCase("Sub Main()\nDim o As I = Nothing\nEnd Sub", "interface")]
    [TestCase("Sub Main()\nDim f As Action = Nothing\nEnd Sub", "Action")]
    [TestCase("Sub Main()\nDim g As Func(Of Integer) = Nothing\nEnd Sub", "Func")]
    [TestCase("Sub Main()\nDim d As UserDel = Nothing\nEnd Sub", "user Delegate")]
    [TestCase("Sub Main()\nDim s As String = Nothing\nEnd Sub", "String")]
    [TestCase("Sub Main()\nDim a() As Integer = Nothing\nEnd Sub", "array")]
    [TestCase("Sub Main()\nDim l As List(Of Integer) = Nothing\nEnd Sub", "List")]
    public void Dim_Local_AdmitsNothing_IntoAReferenceType(string body, string target)
    {
        var (ok, errors) = Analyze(body, Prelude);
        Assert.That(ok, Is.True, $"[{target}] " + string.Join("; ", errors));
    }

    [TestCase("Class H\nPublic X As C = Nothing\nEnd Class\nSub Main()\nEnd Sub", "class field")]
    [TestCase("Class H\nPublic Cb As Action = Nothing\nEnd Class\nSub Main()\nEnd Sub", "Action field")]
    [TestCase("Class H\nPublic S As String = Nothing\nEnd Class\nSub Main()\nEnd Sub", "String field")]
    [TestCase("Class H\nPublic A() As Integer = Nothing\nEnd Class\nSub Main()\nEnd Sub", "array field")]
    public void Dim_ClassField_AdmitsNothing_IntoAReferenceType(string body, string target)
    {
        var (ok, errors) = Analyze(body, Prelude);
        Assert.That(ok, Is.True, $"[{target}] " + string.Join("; ", errors));
    }

    [TestCase("Dim G As C = Nothing\nSub Main()\nEnd Sub", "class")]
    [TestCase("Dim S As String = Nothing\nSub Main()\nEnd Sub", "String")]
    public void Dim_ModuleLevel_AdmitsNothing_IntoAReferenceType(string body, string target)
    {
        var (ok, errors) = Analyze(body, Prelude);
        Assert.That(ok, Is.True, $"[{target}] " + string.Join("; ", errors));
    }

    // ---- Assignment: variable, field, property set, array element — one code path (~8959) ------

    [Test]
    public void Assignment_Variable_AdmitsNothing()
    {
        var (ok, errors) = Analyze("Sub Main()\nDim o As C\no = Nothing\nEnd Sub", Prelude);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void Assignment_Field_AdmitsNothing()
    {
        var (ok, errors) = Analyze(
            "Class H\nPublic X As C\nEnd Class\nSub Main()\nDim h As New H()\nh.X = Nothing\nEnd Sub", Prelude);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void Assignment_PropertySet_AdmitsNothing()
    {
        var (ok, errors) = Analyze(
            """
            Class H
                Private _n As C
                Public Property Nxt As C
                    Get
                        Return _n
                    End Get
                    Set(value As C)
                        _n = value
                    End Set
                End Property
            End Class
            Sub Main()
                Dim h As New H()
                h.Nxt = Nothing
            End Sub
            """, Prelude);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    [Test]
    public void Assignment_ArrayElement_AdmitsNothing()
    {
        var (ok, errors) = Analyze("Sub Main()\nDim a(2) As C\na(0) = Nothing\nEnd Sub", Prelude);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    // ---- User call argument (~10223: Take(Nothing), obj.M(Nothing)) ----------------------------

    [TestCase("Sub Take(c As C)\nEnd Sub\nSub Main()\nTake(Nothing)\nEnd Sub", "class, free function")]
    [TestCase("Sub Take(i As I)\nEnd Sub\nSub Main()\nTake(Nothing)\nEnd Sub", "interface, free function")]
    [TestCase("Sub Take(s As String)\nEnd Sub\nSub Main()\nTake(Nothing)\nEnd Sub", "String, free function")]
    [TestCase("Sub Take(a() As Integer)\nEnd Sub\nSub Main()\nTake(Nothing)\nEnd Sub", "array, free function")]
    [TestCase("Sub Take(l As List(Of Integer))\nEnd Sub\nSub Main()\nTake(Nothing)\nEnd Sub", "List, free function")]
    [TestCase("Class K\nPublic Sub Take(c As C)\nEnd Sub\nEnd Class\nSub Main()\nDim k As New K()\nk.Take(Nothing)\nEnd Sub", "class, method")]
    public void UserCallArgument_AdmitsNothing_IntoAReferenceType(string body, string target)
    {
        var (ok, errors) = Analyze(body, Prelude);
        Assert.That(ok, Is.True, $"[{target}] " + string.Join("; ", errors));
    }

    // ---- Delegate-invocation argument (~10104: f(Nothing) where f is itself a delegate value) --

    [TestCase(
        "Sub Main()\nDim f As Action(Of C) = Sub(x As C)\nConsole.WriteLine(\"x\")\nEnd Sub\nf(Nothing)\nEnd Sub",
        "Action(Of C)")]
    [TestCase(
        "Sub Main()\nDim f As Action(Of Integer()) = Sub(v As Integer())\nConsole.WriteLine(\"v\")\nEnd Sub\nf(Nothing)\nEnd Sub",
        "Action(Of array)")]
    public void DelegateInvocationArgument_AdmitsNothing_IntoAReferenceType(string body, string target)
    {
        var (ok, errors) = Analyze(body, Prelude);
        Assert.That(ok, Is.True, $"[{target}] " + string.Join("; ", errors));
    }

    // ---- New argument (~10652) ------------------------------------------------------------------

    [TestCase("Class H\nPublic Sub New(c As C)\nEnd Sub\nEnd Class\nSub Main()\nDim h As New H(Nothing)\nEnd Sub", "class")]
    [TestCase("Class H\nPublic Sub New(s As String)\nEnd Sub\nEnd Class\nSub Main()\nDim h As New H(Nothing)\nEnd Sub", "String")]
    public void NewArgument_AdmitsNothing_IntoAReferenceType(string body, string target)
    {
        var (ok, errors) = Analyze(body, Prelude);
        Assert.That(ok, Is.True, $"[{target}] " + string.Join("; ", errors));
    }

    // ---- MyBase.New argument (~6778) ------------------------------------------------------------

    [Test]
    public void MyBaseNewArgument_AdmitsNothing()
    {
        var (ok, errors) = Analyze(
            """
            Class B
                Public Sub New(c As C)
                End Sub
            End Class
            Class D
                Inherits B
                Public Sub New()
                    MyBase.New(Nothing)
                End Sub
            End Class
            Sub Main()
            End Sub
            """, Prelude);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    // ---- Return (~8823) --------------------------------------------------------------------------

    [TestCase("Function F() As C\nReturn Nothing\nEnd Function\nSub Main()\nEnd Sub", "class")]
    [TestCase("Function F() As I\nReturn Nothing\nEnd Function\nSub Main()\nEnd Sub", "interface")]
    [TestCase("Function F() As Action\nReturn Nothing\nEnd Function\nSub Main()\nEnd Sub", "Action")]
    [TestCase("Function F() As String\nReturn Nothing\nEnd Function\nSub Main()\nEnd Sub", "String")]
    [TestCase("Function F() As Integer()\nReturn Nothing\nEnd Function\nSub Main()\nEnd Sub", "array")]
    [TestCase("Function F() As List(Of Integer)\nReturn Nothing\nEnd Function\nSub Main()\nEnd Sub", "List")]
    public void Return_AdmitsNothing_IntoAReferenceType(string body, string target)
    {
        var (ok, errors) = Analyze(body, Prelude);
        Assert.That(ok, Is.True, $"[{target}] " + string.Join("; ", errors));
    }

    // ---- Optional default (~6101) -----------------------------------------------------------------

    [TestCase("Sub Take(Optional c As C = Nothing)\nEnd Sub\nSub Main()\nTake()\nEnd Sub", "class")]
    [TestCase("Sub Take(Optional s As String = Nothing)\nEnd Sub\nSub Main()\nTake()\nEnd Sub", "String")]
    public void OptionalDefault_AdmitsNothing_IntoAReferenceType(string body, string target)
    {
        var (ok, errors) = Analyze(body, Prelude);
        Assert.That(ok, Is.True, $"[{target}] " + string.Join("; ", errors));
    }

    // ============================================================================================
    // 2. Value types stay refused, with the exact advice message (NothingAdviceFor's arms).
    // ============================================================================================

    [TestCase("Sub Main()\nDim n As Integer = Nothing\nEnd Sub", "Nothing has no value of type 'Integer'; write 0")]
    [TestCase("Sub Main()\nDim n As Double = Nothing\nEnd Sub", "Nothing has no value of type 'Double'; write 0")]
    [TestCase("Sub Main()\nDim n As Decimal = Nothing\nEnd Sub", "Nothing has no value of type 'Decimal'; write 0")]
    [TestCase("Sub Main()\nDim b As Boolean = Nothing\nEnd Sub", "Nothing has no value of type 'Boolean'; write False")]
    [TestCase("Sub Main()\nDim c As Char = Nothing\nEnd Sub", "Nothing has no value of type 'Char'; write a character literal")]
    [TestCase("Enum E\nA\nEnd Enum\nSub Main()\nDim e1 As E = Nothing\nEnd Sub",
        "Nothing has no value of type 'E'; write a member of 'E'")]
    [TestCase("Structure P\nPublic X As Integer\nEnd Structure\nSub Main()\nDim p As P = Nothing\nEnd Sub",
        "Nothing has no value of type 'P'; write New P()")]
    [TestCase("Sub Main()\nDim d As DateTime = Nothing\nEnd Sub",
        "Nothing has no value of type 'DateTime'; 'DateTime' is a value type; write a DateTime value")]
    [TestCase("Sub Main()\nDim t As TimeSpan = Nothing\nEnd Sub",
        "Nothing has no value of type 'TimeSpan'; 'TimeSpan' is a value type; write a TimeSpan value")]
    [TestCase("Sub Main()\nDim g As Guid = Nothing\nEnd Sub",
        "Nothing has no value of type 'Guid'; 'Guid' is a value type; write a Guid value")]
    public void ValueType_StaysRefused_WithExactAdvice(string body, string expectedMessage)
    {
        var (ok, errors) = Analyze(body);
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains(expectedMessage));
    }

    /// <summary>
    /// A type parameter may be instantiated with a value type (VB gives <c>default(T)</c>, the
    /// value-type default #186 owns); #173's new arm. <c>Pick(Of T)()</c> reaches the analyzer's
    /// Dim-site refusal INSIDE the generic function body, before any instantiation happens.
    /// </summary>
    [Test]
    public void TypeParameter_StaysRefused_WithExactAdvice()
    {
        var (ok, errors) = Analyze(
            "Function Pick(Of T)() As T\nDim x As T = Nothing\nReturn x\nEnd Function\nSub Main()\nEnd Sub");
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains("Nothing has no value of type 'T'; 'T' is a type parameter and may be a value type"));
    }

    /// <summary>A tuple is a value type the Structure arm never named; #173's new arm.</summary>
    [Test]
    public void Tuple_StaysRefused_WithExactAdvice()
    {
        var (ok, errors) = Analyze("Sub Main()\nDim t As (Integer, String) = Nothing\nEnd Sub");
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains("Nothing has no value of type '(Integer, String)'; write a tuple literal"));
    }

    /// <summary>
    /// A <c>Union</c> is a value type folded into the SAME arm as Structure/UserDefinedType
    /// (<c>"write New {target.Name}()"</c>) — #173's new arm widens that TestCase, it does not add
    /// a distinct message shape.
    /// </summary>
    [Test]
    public void Union_StaysRefused_WithExactAdvice()
    {
        var (ok, errors) = Analyze(
            "Union U\nA As Integer\nB As Single\nEnd Union\nSub Main()\nDim u1 As U = Nothing\nEnd Sub");
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains("Nothing has no value of type 'U'; write New U()"));
    }

    /// <summary>
    /// The NativeOwned P1 structs, refused via the Dim site AND via Return / a user-call argument —
    /// proving the advice is not special-cased to one site (mutant (b2): dropping the NativeOwned
    /// arm would admit all three of these silently at every site alike).
    /// </summary>
    [TestCase("Function F() As DateTime\nReturn Nothing\nEnd Function\nSub Main()\nEnd Sub",
        "Nothing has no value of type 'DateTime'; 'DateTime' is a value type; write a DateTime value")]
    [TestCase("Sub Take(t As TimeSpan)\nEnd Sub\nSub Main()\nTake(Nothing)\nEnd Sub",
        "Nothing has no value of type 'TimeSpan'; 'TimeSpan' is a value type; write a TimeSpan value")]
    public void NativeOwnedStruct_StaysRefused_AtOtherSitesToo(string body, string expectedMessage)
    {
        var (ok, errors) = Analyze(body);
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains(expectedMessage));
    }

    /// <summary>
    /// StringBuilder is the ONE NativeOwned type that is a reference type (a
    /// <c>shared_ptr</c> on the C++ backend); it must NOT take the NativeOwned refusal arm.
    /// Exception is an ordinary unresolvable .NET reference type. Both admitted.
    /// </summary>
    [Test]
    public void NativeOwnedReferenceException_AndUnresolvableNetType_AdmitNothing()
    {
        var (ok, errors) = Analyze(
            "Using System.Text\nSub Main()\nDim sb As StringBuilder = Nothing\nDim ex As Exception = Nothing\nEnd Sub");
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    // ============================================================================================
    // 3. Integer? is admitted (the one value type VB itself admits Nothing into).
    // ============================================================================================

    [Test]
    public void NullableValueType_AdmitsNothing()
    {
        var (ok, errors) = Analyze("Sub Main()\nDim n As Integer? = Nothing\nEnd Sub");
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    // ============================================================================================
    // 4. Object is admitted (the only case that worked before #173).
    // ============================================================================================

    [Test]
    public void Object_AdmitsNothing()
    {
        var (ok, errors) = Analyze("Sub Main()\nDim o As Object = Nothing\nEnd Sub");
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    // ============================================================================================
    // 5. `Dim x = Nothing`, with no `As`, stays refused — #173 never touches type inference.
    // ============================================================================================

    [Test]
    public void UntypedDim_WithNoAsClause_StillRefusesNothing()
    {
        var (ok, errors) = Analyze("Sub Main()\nDim x = Nothing\nEnd Sub");
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains("Cannot infer type"));
        Assert.That(errors, Has.Some.Contains("from 'Nothing'"));
    }

    // ============================================================================================
    // 6. The ONE-answer invariant: the typed array literal and this fixture's Dim site agree.
    // ============================================================================================

    /// <summary>
    /// <c>New String() {Nothing}</c> is admitted, and <c>New Integer() {Nothing}</c> is refused
    /// with the SAME advice a plain <c>Dim n As Integer = Nothing</c> gets — because both go
    /// through <c>JudgeNothingConversion</c> → <c>NothingAdviceFor</c>, one method, not two lists.
    /// A mutation that forked the literal's rule from the Dim site's would pass
    /// <see cref="TypedArrayLiteralTests.TypedLiteral_Nothing"/> alone but fail this cross-check.
    /// </summary>
    [Test]
    public void TypedArrayLiteral_AndDimSite_AgreeOnNothing()
    {
        var (okLiteralS, _) = Analyze("Sub Main()\nDim a() As String = New String() {\"a\", Nothing}\nEnd Sub");
        var (okDimS, _) = Analyze("Sub Main()\nDim s As String = Nothing\nEnd Sub");
        Assert.That(okLiteralS, Is.True, "New String() {Nothing} must be admitted");
        Assert.That(okDimS, Is.True, "Dim s As String = Nothing must be admitted");

        var (okLiteralI, errorsLiteralI) = Analyze("Sub Main()\nDim a() As Integer = New Integer() {Nothing}\nEnd Sub");
        var (okDimI, errorsDimI) = Analyze("Sub Main()\nDim n As Integer = Nothing\nEnd Sub");
        Assert.That(okLiteralI, Is.False, "New Integer() {Nothing} must stay refused");
        Assert.That(okDimI, Is.False, "Dim n As Integer = Nothing must stay refused");
        const string advice = "Nothing has no value of type 'Integer'; write 0";
        Assert.That(errorsLiteralI, Has.Some.Contains(advice), "the literal site's advice");
        Assert.That(errorsDimI, Has.Some.Contains(advice), "the Dim site's advice — SAME text as the literal site's");
    }

    // ============================================================================================
    // 7. The IR re-type: an Object-typed Nothing constant is re-typed to the DECLARED type
    //    (CoerceToDeclaredType, IRBuilder.cs), not left Object — the same in-place re-typing a
    //    numeric literal gets. A backend that holds a reference type as a VALUE with no null
    //    state (C++'s std::string / BasicLang::Array<T>) cannot spell an untyped null; see
    //    CppCodeGenerator.NothingOf, pinned in NothingConversionExecutionTests.
    // ============================================================================================

    private static IRModule BuildIr(string body, string prelude = "")
    {
        var source = (prelude.Length > 0 ? prelude + "\n" : "") + body;
        var parser = new Parser(new Lexer(source).Tokenize());
        var program = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(program), Is.True,
            string.Join("; ", analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).Select(e => e.Message)));
        return new IRBuilder(analyzer).Build(program, "T");
    }

    /// <summary>
    /// <c>Dim s As String = Nothing</c>: the stored <c>IRConstant</c>'s <c>Type</c> must be
    /// <c>String</c>, not <c>Object</c> — mutant (c1)'s target. Without the re-type, the C++
    /// backend's <c>EmitConstant</c> sees an Object-typed null and (per <c>NothingOf</c>'s own
    /// guard) falls back to bare <c>nullptr</c> for a value-held type, which does not compile as a
    /// <c>std::string</c>.
    /// </summary>
    [Test]
    public void IR_DimSite_RetypesTheNothingConstant_ToTheDeclaredType()
    {
        var module = BuildIr("Sub Main()\nDim s As String = Nothing\nEnd Sub");
        var main = module.Functions.Single(f => f.Name == "Main");
        var assignment = main.Blocks.SelectMany(b => b.Instructions).OfType<IRAssignment>()
            .Single(a => a.Target.Name == "s");

        Assert.That(assignment.Value, Is.TypeOf<IRConstant>(), "Nothing must still be a constant, only re-typed");
        Assert.That(assignment.Value.Type?.Name, Is.EqualTo("String"), "the constant's Type must be re-typed to String, not left Object");
        Assert.That(((IRConstant)assignment.Value).Value, Is.Null, "the CLR value stays null");
    }

    /// <summary>
    /// The delegate-invocation arm of <c>CoerceToParameterType</c>: <c>f(Nothing)</c> where
    /// <c>f As Action(Of C)</c> types the argument from the delegate's OWN generic arguments
    /// (there is no parameter-list Symbol to read, unlike a user procedure call) — mutant (c2)'s
    /// target. Finds the <c>IRCall</c> that invokes the delegate VALUE (its <c>CalleeValue</c> is
    /// the loaded <c>f</c>) and asserts its sole argument is re-typed to <c>C</c>.
    /// </summary>
    [Test]
    public void IR_DelegateInvocationArgument_TypesNothing_FromTheDelegatesGenericArgument()
    {
        var module = BuildIr(
            "Sub Main()\nDim f As Action(Of C) = Sub(x As C)\nConsole.WriteLine(\"hi\")\nEnd Sub\nf(Nothing)\nEnd Sub",
            "Class C\nEnd Class");
        var main = module.Functions.Single(f => f.Name == "Main");
        var call = main.Blocks.SelectMany(b => b.Instructions).OfType<IRCall>()
            .Single(c => c.Arguments.Count == 1 && c.Arguments[0] is IRConstant { Value: null });

        Assert.That(call.Arguments[0].Type?.Name, Is.EqualTo("C"),
            "the Nothing argument must be re-typed to the delegate's element type C, not left Object");
    }
}
