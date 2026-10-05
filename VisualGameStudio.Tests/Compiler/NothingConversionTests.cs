using BasicLang.Compiler;                  // Lexer, Parser
using BasicLang.Compiler.AST;              // the node types
using BasicLang.Compiler.SemanticAnalysis; // SemanticAnalyzer, TypeInfo, ErrorSeverity
using BasicLang.Compiler.IR;               // IRBuilder etc.
using BasicLang.Compiler.CodeGen;          // ForeignFeatureException
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
/// <para>⭐ #186 (the owner's decision, "fix #186") widened the rule to VB's: <c>Nothing</c> converts to
/// EVERY type. A reference type gets a null reference and a VALUE type gets its default
/// (<c>Dim n As Integer = Nothing</c> is 0, a Boolean False, a Char <c>ChrW(0)</c>, a Structure every field
/// at its default, a type parameter <c>default(T)</c>). <c>JudgeNothingConversion</c> admits it at all nine
/// sites, and the front end's per-kind advice (<c>NothingAdviceFor</c>, "write 0") is gone. The list of value
/// types is <c>TypeInfo.NothingIsDefaultValue</c>, shared with the IR and two backends. What stays refused is
/// IDENTITY on a value type (<c>n Is Nothing</c>, <c>Case Is Nothing</c>: BC30020, IsIsNotOperatorTests).
/// Section 2 below is the moved pin of the old refusal: each row now says what the value type's Nothing LOWERS
/// to. The RUN is <see cref="NothingIntoValueTypeExecutionTests"/>.</para>
///
/// <para>The reference-type sections are #173's, unchanged: the P1 native structs (DateTime/TimeSpan/Guid,
/// NativeOwned reference-typed StringBuilder excepted), a type parameter, a tuple and <c>Union</c> were each
/// measured wrong once admitted before #186 (CS0037 on C#, a clang error on C++, a silent <c>null</c> on
/// JavaScript for the P1 structs; CS0403 for a type parameter) — #186 gives them their default instead.</para>
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
    // 2. ⭐ #186: Nothing into a VALUE type is that type's default, the VB way (the moved pin of #173's
    //    "Value types stay refused, with the exact advice message").
    // ============================================================================================

    /// <summary>What a lowered constant is, as text a [TestCase] can carry: null, a bool, the NUL character, or a number.</summary>
    private static string Describe(object? value) => value switch
    {
        null => "null",
        bool b => b ? "True" : "False",
        char c => c == '\0' ? "NUL" : c.ToString(),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>The constant a <c>Nothing</c> was lowered to where it was stored into <paramref name="target"/>: the ONE assignment to that local.</summary>
    private static IRConstant StoredConstant(string body, string target, string prelude = "")
    {
        var module = BuildIr(body, prelude);   // BuildIr asserts the front end ADMITTED the program
        var main = module.Functions.Single(f => f.Name == "Main");
        var assignment = main.Blocks.SelectMany(b => b.Instructions).OfType<IRAssignment>().Single(a => a.Target.Name == target);
        Assert.That(assignment.Value, Is.TypeOf<IRConstant>(), "a Nothing converted to a value type is still a constant");
        return (IRConstant)assignment.Value;
    }

    /// <summary>
    /// Every value type the old refusal named is ADMITTED, and lowers to its default: a primitive to the zero LITERAL (exactly the constant a written
    /// <c>0</c> / <c>False</c> lowers to, so a field or module initializer stays a constant), any other value type to a null constant TYPED with it (which
    /// now means "default of T": C# <c>default(T)</c>, C++ <c>T{}</c>). The last column is where JavaScript still refuses the TYPE, with or without Nothing:
    /// Decimal BL7007, Char BL7004, Structure BL7005 — a capability decision of that backend, not a Nothing rule. (The other JavaScript, C++ and MSIL limits of
    /// an Enum, DateTime, TimeSpan or Guid are in the fixture header of <see cref="NothingIntoValueTypeExecutionTests"/>, with no test: asserting one pins a defect.)
    /// </summary>
    [TestCase("Sub Main()\nDim n As Integer = Nothing\nEnd Sub", "n", "Integer", "0", "")]
    [TestCase("Sub Main()\nDim n As Double = Nothing\nEnd Sub", "n", "Double", "0", "")]
    [TestCase("Sub Main()\nDim n As Decimal = Nothing\nEnd Sub", "n", "Decimal", "0", "BL7007")]
    [TestCase("Sub Main()\nDim b As Boolean = Nothing\nEnd Sub", "b", "Boolean", "False", "")]
    [TestCase("Sub Main()\nDim c As Char = Nothing\nEnd Sub", "c", "Char", "NUL", "BL7004")]
    [TestCase("Enum E\nA\nEnd Enum\nSub Main()\nDim e1 As E = Nothing\nEnd Sub", "e1", "E", "null", "")]
    [TestCase("Structure P\nPublic X As Integer\nEnd Structure\nSub Main()\nDim p As P = Nothing\nEnd Sub", "p", "P", "null", "BL7005")]
    [TestCase("Sub Main()\nDim d As DateTime = Nothing\nEnd Sub", "d", "DateTime", "null", "")]
    [TestCase("Sub Main()\nDim t As TimeSpan = Nothing\nEnd Sub", "t", "TimeSpan", "null", "")]
    [TestCase("Sub Main()\nDim g As Guid = Nothing\nEnd Sub", "g", "Guid", "null", "")]
    public void ValueType_AdmitsNothing_AsItsDefault(string body, string target, string typeName, string expectedDefault, string javaScriptRefusal)
    {
        var (ok, errors) = Analyze(body);
        Assert.That(ok, Is.True, "Nothing into a value type is its default, as in VB: " + string.Join("; ", errors));

        var constant = StoredConstant(body, target);
        Assert.Multiple(() =>
        {
            Assert.That(constant.Type?.Name, Is.EqualTo(typeName), "the constant is typed with the declared type, not left Object");
            Assert.That(Describe(constant.Value), Is.EqualTo(expectedDefault), $"{typeName}'s default");
        });

        if (javaScriptRefusal.Length > 0)
        {
            var refusal = Assert.Throws<ForeignFeatureException>(() => JsTestSupport.Compile(body));
            Assert.That(refusal!.Message, Does.StartWith(javaScriptRefusal), $"JavaScript refuses {typeName} by name, with or without a Nothing");
        }
    }

    /// <summary>
    /// A type parameter may be instantiated with a value type, and <c>default(T)</c> is right for a reference one too, so it is ADMITTED and lowers to a null
    /// constant typed <c>T</c> (C# spells it <c>default(T)</c>; a bare <c>null</c> was CS0403). <c>Pick(Of T)()</c> reaches the analyzer's Dim site INSIDE the
    /// generic function body, before any instantiation. Backend limit, pre-existing and with a Nothing-free control that fails alike: C# only (C++ and MSIL do
    /// not build a generic function; on JavaScript an uninitialized T is null).
    /// </summary>
    [Test]
    public void TypeParameter_AdmitsNothing_AsDefaultOfT()
    {
        const string source = "Function Pick(Of T)() As T\nDim x As T = Nothing\nReturn x\nEnd Function\nSub Main()\nEnd Sub";
        var (ok, errors) = Analyze(source);
        Assert.That(ok, Is.True, string.Join("; ", errors));

        var module = BuildIr(source);
        var pick = module.Functions.Single(f => f.Name == "Pick");
        var store = pick.Blocks.SelectMany(b => b.Instructions).OfType<IRAssignment>().Single(a => a.Target.Name == "x");
        Assert.That(store.Value, Is.TypeOf<IRConstant>());
        Assert.Multiple(() =>
        {
            Assert.That(((IRConstant)store.Value).Value, Is.Null, "a typed null constant of a value type means that type's default");
            Assert.That(store.Value.Type?.Name, Is.EqualTo("T"));
        });
    }

    /// <summary>
    /// A tuple is a value type; admitted, and lowered to a null constant typed with the tuple. Backend limit, pre-existing: tuple runs on C# only, so the
    /// Nothing-free control <c>Dim t As (Integer, String) = (0, "a")</c> fails alike elsewhere — what Nothing adds is no refusal of its own.
    /// </summary>
    [Test]
    public void Tuple_AdmitsNothing_AsItsDefault()
    {
        var (ok, errors) = Analyze("Sub Main()\nDim t As (Integer, String) = Nothing\nEnd Sub");
        Assert.That(ok, Is.True, string.Join("; ", errors));
        Assert.That(errors, Has.None.Contains("Nothing has no value"));
    }

    /// <summary>
    /// A <c>Union</c> is a value type that runs on NO backend, Nothing or not (a Nothing-free declaration fails the same way on every one). So the pin is that
    /// the Nothing form adds NOTHING to the front end's verdict: its diagnostics are exactly those of <c>Dim u1 As U</c>.
    /// </summary>
    [Test]
    public void Union_AdmitsNothing_ExactlyAsMuchAsADeclarationWithoutIt()
    {
        const string union = "Union U\nA As Integer\nB As Single\nEnd Union\n";
        var (okNothing, errorsNothing) = Analyze(union + "Sub Main()\nDim u1 As U = Nothing\nEnd Sub");
        var (okControl, errorsControl) = Analyze(union + "Sub Main()\nDim u1 As U\nEnd Sub");

        Assert.Multiple(() =>
        {
            Assert.That(okNothing, Is.True, string.Join("; ", errorsNothing));
            Assert.That(okControl, Is.True, string.Join("; ", errorsControl));
            Assert.That(errorsNothing, Is.EqualTo(errorsControl), "Nothing into a Union adds no diagnostic of its own");
        });
    }

    /// <summary>
    /// The NativeOwned P1 structs, admitted via Return and a user-call argument as well as the Dim site — the rule is not special-cased to one site (the
    /// refusal this replaces was proven the same way: dropping the NativeOwned arm of <c>TypeInfo.NothingIsDefaultValue</c> turns these two back into a
    /// null reference that C# cannot build, CS0037). Backend limit, pre-existing: DateTime and TimeSpan run on C# and C++ only.
    /// </summary>
    [TestCase("Function F() As DateTime\nReturn Nothing\nEnd Function\nSub Main()\nEnd Sub", "DateTime")]
    [TestCase("Sub Take(t As TimeSpan)\nEnd Sub\nSub Main()\nTake(Nothing)\nEnd Sub", "TimeSpan")]
    public void NativeOwnedStruct_AdmitsNothing_AtOtherSitesToo(string body, string typeName)
    {
        var (ok, errors) = Analyze(body);
        Assert.That(ok, Is.True, string.Join("; ", errors));
        Assert.That(errors, Has.None.Contains("Nothing has no value"));

        var module = BuildIr(body);
        var constants = module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions)
            .SelectMany(i => i switch { IRReturn r when r.Value != null => new[] { r.Value }, IRCall c => c.Arguments.ToArray(), _ => Array.Empty<IRValue>() })
            .OfType<IRConstant>().Where(c => c.Value is null).ToList();
        Assert.That(constants, Has.Some.Matches<IRConstant>(c => c.Type?.Name == typeName), $"a null constant typed {typeName}, not an untyped Object null");
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
    // 3. Integer? is admitted (a nullable holds Nothing itself; #186's default rule is for the types that cannot).
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
    /// <c>New String() {Nothing}</c> and <c>New Integer() {Nothing}</c> are BOTH admitted, and each lowers to the SAME constant a plain <c>Dim</c> of that
    /// type gets — the String's null reference, the Integer's 0 — because the literal and the Dim site go through <c>JudgeNothingConversion</c> (the front end)
    /// and <c>CoerceToDeclaredType</c> (the IR), one method each, not two lists. A mutation that forked the literal's rule from the Dim site's would pass
    /// <see cref="TypedArrayLiteralTests.TypedLiteral_Nothing_IsAdmitted_IntoAReferenceAndAValueElement"/> alone but fail this cross-check.
    /// </summary>
    [Test]
    public void TypedArrayLiteral_AndDimSite_AgreeOnWhatNothingBecomes()
    {
        foreach (var (typeName, expected) in new[] { ("String", "null"), ("Integer", "0") })
        {
            var (okLiteral, errorsLiteral) = Analyze($"Sub Main()\nDim a() As {typeName} = New {typeName}() {{Nothing}}\nEnd Sub");
            var (okDim, errorsDim) = Analyze($"Sub Main()\nDim n As {typeName} = Nothing\nEnd Sub");
            Assert.That(okLiteral, Is.True, $"New {typeName}() {{Nothing}} must be admitted: " + string.Join("; ", errorsLiteral));
            Assert.That(okDim, Is.True, $"Dim n As {typeName} = Nothing must be admitted: " + string.Join("; ", errorsDim));

            var dim = StoredConstant($"Sub Main()\nDim n As {typeName} = Nothing\nEnd Sub", "n");
            var module = BuildIr($"Sub Main()\nDim a() As {typeName} = New {typeName}() {{Nothing}}\nEnd Sub");
            var element = module.Functions.Single(f => f.Name == "Main").Blocks.SelectMany(b => b.Instructions).OfType<IRArrayStore>().Single().Value;

            Assert.That(element, Is.TypeOf<IRConstant>(), $"{typeName}: the element is a constant");
            var literal = (IRConstant)element;
            Assert.Multiple(() =>
            {
                Assert.That(Describe(dim.Value), Is.EqualTo(expected), $"{typeName}: the Dim site");
                Assert.That(Describe(literal.Value), Is.EqualTo(Describe(dim.Value)), $"{typeName}: the literal gives the SAME value as the Dim site");
                Assert.That(literal.Type?.Name, Is.EqualTo(dim.Type?.Name), $"{typeName}: and the SAME type");
            });
        }
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
