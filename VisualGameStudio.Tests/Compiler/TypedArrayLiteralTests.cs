using BasicLang.Compiler;                  // Lexer, Parser
using BasicLang.Compiler.AST;              // the node types
using BasicLang.Compiler.SemanticAnalysis; // SemanticAnalyzer, TypeInfo, ErrorSeverity (used by Task 2)
using BasicLang.Compiler.IR;               // IRBuilder etc. (used by Task 3)
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 24a — `New T() { … }`: array creation with an initializer (spec §9). MEASURED before this
/// existed: `New ToolStripItem()` parsed as a parameterless constructor call and the `{` was the
/// enclosing call's "Expected ')' after arguments" error.
/// </summary>
[TestFixture]
public class TypedArrayLiteralTests
{
    private static ProgramNode Parse(string body, out Parser parser)
    {
        var source = "Sub Main()\n" + body + "\nEnd Sub";
        parser = new Parser(new Lexer(source).Tokenize());
        return parser.Parse();
    }

    private static BlockNode MainBody(ProgramNode program) =>
        ((SubroutineNode)program.Declarations[0]).Body;

    private static ExpressionNode FirstInitializer(ProgramNode program) =>
        ((VariableDeclarationNode)MainBody(program).Statements[0]).Initializer!;

    /// <summary>The LAST Dim's initializer — the analyzer tests declare their operands first.</summary>
    private static ExpressionNode LastInitializer(ProgramNode program) =>
        ((SubroutineNode)program.Declarations[^1]).Body.Statements.OfType<VariableDeclarationNode>().Last().Initializer!;

    [Test]
    public void NewTypeParensBraces_IsACollectionInitializerWithTheElementType()
    {
        var program = Parse("Dim items() As Integer = New Integer() {1, 2, 3}", out var parser);

        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var init = FirstInitializer(program);
        Assert.That(init, Is.TypeOf<CollectionInitializerNode>());
        var literal = (CollectionInitializerNode)init;
        Assert.That(literal.ElementType?.Name, Is.EqualTo("Integer"));
        Assert.That(literal.Elements, Has.Count.EqualTo(3));
    }

    [Test]
    public void NewTypeParensEmptyBraces_IsAnEmptyTypedLiteral()
    {
        var program = Parse("Dim items() As String = New String() {}", out var parser);
        Assert.That(parser.Errors, Is.Empty);
        Assert.That(((CollectionInitializerNode)FirstInitializer(program)).Elements, Is.Empty);
    }

    [Test]
    public void NewTypeParens_WithNoBrace_IsStillAConstructorCall()
    {
        var program = Parse("Dim x As Object = New Object()", out var parser);
        Assert.That(parser.Errors, Is.Empty);
        Assert.That(FirstInitializer(program), Is.TypeOf<NewExpressionNode>());
    }

    /// <summary>
    /// The motivating shape — `Items.AddRange(New ToolStripItem() {…})` — is a literal in a
    /// CALL-ARGUMENT position, not a Dim initializer. Pinned here at the parser level.
    /// </summary>
    [Test]
    public void NewTypeParensBraces_InACallArgument_IsTheCallsSingleTypedArgument()
    {
        var program = Parse("Foo(New Integer() {1, 2})", out var parser);

        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var statement = (ExpressionStatementNode)MainBody(program).Statements[0];
        var call = (CallExpressionNode)statement.Expression;
        Assert.That(call.Arguments, Has.Count.EqualTo(1));
        Assert.That(call.Arguments[0], Is.TypeOf<CollectionInitializerNode>());
        var literal = (CollectionInitializerNode)call.Arguments[0];
        Assert.That(literal.ElementType?.Name, Is.EqualTo("Integer"));
        Assert.That(literal.Elements, Has.Count.EqualTo(2));
    }

    [Test]
    public void NewTypeParensBraces_Nested_TypesEachLevelSeparately()
    {
        var program = Parse("Dim rows() As Object = New Object() {New Integer() {1, 2}}", out var parser);

        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var outer = (CollectionInitializerNode)FirstInitializer(program);
        Assert.That(outer.ElementType?.Name, Is.EqualTo("Object"));
        Assert.That(outer.Elements, Has.Count.EqualTo(1));
        Assert.That(outer.Elements[0], Is.TypeOf<CollectionInitializerNode>());
        var inner = (CollectionInitializerNode)outer.Elements[0];
        Assert.That(inner.ElementType?.Name, Is.EqualTo("Integer"));
        Assert.That(inner.Elements, Has.Count.EqualTo(2));
    }

    [TestCase("Dim items() As Integer = New Integer {1, 2}", "New Integer() {")]
    [TestCase("Dim items() As Integer = New Integer(2) {1, 2, 3}", "the initializer sets the size")]
    [TestCase("Dim items As New Integer() {1, 2}", "Dim items() As Integer = New Integer() {")]
    [TestCase("Dim items As New Integer {1, 2}", "Dim items() As Integer = New Integer() {")]
    // ⚠ `TypeReference.Name` is the BARE identifier; the generic list lives only in ToString().
    // A message built from .Name told the user to write `New List() {…}` — a different type.
    [TestCase("Dim items() As Integer = New List(Of Integer) {1}", "New List(Of Integer)() {")]
    public void TheThreeRefusals_NameTheFix(string body, string expectedInMessage)
    {
        Parse(body, out var parser);
        Assert.That(parser.Errors, Is.Not.Empty, "expected a parse refusal");
        Assert.That(parser.Errors[0].Message, Does.Contain(expectedInMessage));
    }

    // ------------------------------------------------------------------------------------------
    // Task 2 — the analyzer types a typed literal by T (spec §9 "Typing").
    // ------------------------------------------------------------------------------------------

    private const string ShapePrelude =
        "MustInherit Class Shape\nEnd Class\nClass Circle\nInherits Shape\nEnd Class\nClass Square\nInherits Shape\nEnd Class";

    private static (bool ok, List<string> errors, List<string> warnings, TypeInfo? type) Analyze(string body, string prelude = "")
    {
        var source = prelude + "\nSub Main()\n" + body + "\nEnd Sub";
        var parser = new Parser(new Lexer(source).Tokenize());
        var program = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(program);
        // ⛔ ONE list, two severities: the analyzer has no `Warnings` member. `Warning(...)` appends an
        // ErrorSeverity.Warning entry to `Errors`, and every synthetic .NET type produces BL6016-style
        // warnings, so the split below is what makes "no error" and "a warning containing …" mean anything.
        var errors = analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).Select(e => e.Message).ToList();
        var warnings = analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Warning).Select(e => e.Message).ToList();
        var init = LastInitializer(program);   // the LAST Dim's initializer — bodies are several lines
        return (ok, errors, warnings, analyzer.GetNodeType(init));
    }

    /// <summary>
    /// The motivating row: two synthetic .NET element types with no base chain the analyzer can see.
    /// Widening cannot type <c>{a, s}</c>; the stated <c>T</c> does, and csc judges the conversion.
    /// </summary>
    [Test]
    public void TypedLiteral_OfTwoUnresolvableNetTypes_IsTypedByT_WithNoWarning()
    {
        var (ok, errors, warnings, type) = Analyze(
            "Dim a As New ToolStripMenuItem()\nDim s As New ToolStripSeparator()\nDim items() As ToolStripItem = New ToolStripItem() {a, s}",
            prelude: "Using System.Windows.Forms");

        Assert.That(ok, Is.True, string.Join("; ", errors));
        Assert.That(warnings, Has.None.Contains("mixed types"));
        Assert.That(type, Is.Not.Null);
        Assert.That(type!.Name, Is.EqualTo("ToolStripItem[]"));
        Assert.That(type.ElementType?.Name, Is.EqualTo("ToolStripItem"));
    }

    /// <summary>
    /// ⚠ <c>MustInherit</c> on purpose: an array OF an abstract type is legal, and the typed literal
    /// never reaches <c>Visit(NewExpressionNode)</c>'s abstract refusal (the parser discards the
    /// <c>NewExpressionNode</c>).
    /// </summary>
    [Test]
    public void TypedLiteral_OverBasicLangClasses_Widens_AndAnAbstractElementTypeIsLegal()
    {
        var (ok, errors, _, type) = Analyze(
            "Dim c As New Circle()\nDim s As New Square()\nDim all() As Shape = New Shape() {c, s}",
            prelude: ShapePrelude);

        Assert.That(ok, Is.True, string.Join("; ", errors));
        Assert.That(errors, Has.None.Contains("abstract"));
        Assert.That(type?.Name, Is.EqualTo("Shape[]"));
    }

    [Test]
    public void TypedLiteral_RefusesAnUnrelatedElement()
    {
        var (ok, errors, _, _) = Analyze(
            "Dim x As String = \"a\"\nDim all() As Shape = New Shape() {x}",
            prelude: ShapePrelude);

        Assert.That(ok, Is.False);
        Assert.That(errors, Has.Some.Contains("cannot put a 'String' in a 'Shape()'"));
    }

    /// <summary>
    /// The non-literal rule is IsAssignableFrom MINUS its permissive narrowing arm — the arm that lets
    /// <c>Dim i As Integer = aDouble</c> compile today and must not leak into a stated element type.
    /// </summary>
    [Test]
    public void TypedLiteral_RefusesNarrowing_ForNonLiterals()
    {
        var (okD, errorsD, _, _) = Analyze("Dim d As Double = 1.5\nDim a() As Integer = New Integer() {d}");
        Assert.That(okD, Is.False);
        Assert.That(errorsD, Has.Some.Contains("cannot put a 'Double' in an 'Integer()'"));

        var (okL, errorsL, _, _) = Analyze("Dim l As Long = 1\nDim a() As Integer = New Integer() {l}");
        Assert.That(okL, Is.False);
        Assert.That(errorsL, Has.Some.Contains("cannot put a 'Long' in an 'Integer()'"));
    }

    /// <summary>
    /// The other half of "T or a WIDENING to T": a non-literal whose every value fits the element type
    /// is admitted. ⚠ IsAssignableFrom lists only Long ← Integer ahead of its narrowing arm; Byte →
    /// Integer was only ever admitted BY that arm, so a rule that merely excluded the arm would refuse
    /// a genuine VB widening. Widening is decided by RANGE here, and this row is what pins that.
    /// </summary>
    [TestCase("Dim b As Byte = 1\nDim a() As Integer = New Integer() {b}")]
    [TestCase("Dim s As Short = 1\nDim a() As Long = New Long() {s}")]
    [TestCase("Dim i As Integer = 1\nDim a() As Long = New Long() {i}")]
    [TestCase("Dim i As Integer = 1\nDim a() As Double = New Double() {i}")]
    [TestCase("Dim i As Integer = 1\nDim a() As Decimal = New Decimal() {i}")]
    public void TypedLiteral_AdmitsWidening_ForNonLiterals(string body)
    {
        var (ok, errors, _, _) = Analyze(body);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    /// <summary>Same-width-but-signed-into-unsigned is NOT a widening: Integer → UInteger is refused.</summary>
    [Test]
    public void TypedLiteral_RefusesSignedIntoUnsigned_ForNonLiterals()
    {
        var (ok, errors, _, _) = Analyze("Dim i As Integer = 1\nDim a() As UInteger = New UInteger() {i}");
        Assert.That(ok, Is.False);
        Assert.That(errors, Has.Some.Contains("cannot put an 'Integer' in a 'UInteger()'"));
    }

    /// <summary>
    /// The literal rule, OWNED here and stricter than the Dim path's IsNumericLiteralAssignable: an
    /// integral literal into any numeric target, a floating literal into Single/Double/Decimal only,
    /// and BC30439 per element. A leading <c>-</c> is a unary node WRAPPING the literal; the rule
    /// must see through it.
    /// </summary>
    [TestCase("Dim a() As Byte = New Byte() {65}")]
    [TestCase("Dim a() As Long = New Long() {1}")]
    [TestCase("Dim a() As Double = New Double() {1}")]
    [TestCase("Dim a() As Decimal = New Decimal() {1.5}")]
    [TestCase("Dim a() As Single = New Single() {1.5}")]
    [TestCase("Dim a() As Short = New Short() {-1}")]
    [TestCase("Dim a() As Single = New Single() {-1.5}")]
    [TestCase("Dim a() As Decimal = New Decimal() {-1.5}")]
    public void TypedLiteral_NumericLiteralRule_Admits(string body)
    {
        var (ok, errors, _, _) = Analyze(body);
        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    [TestCase("Dim a() As Integer = New Integer() {1.5}", "cannot put a 'Double' in an 'Integer()'")]
    [TestCase("Dim a() As Integer = New Integer() {-1.5}", "cannot put a 'Double' in an 'Integer()'")]
    [TestCase("Dim a() As Byte = New Byte() {300}", "not representable")]
    // BC30439 must see THROUGH the unary: -1 folds to -1, and Byte holds 0 through 255.
    [TestCase("Dim a() As Byte = New Byte() {-1}", "not representable")]
    // ⛔ The literal arm's guard is TWO-sided, and each side has a surviving mutant without these rows.
    // Drop `target.IsNumeric()` and `New String() {1}` enters the arm, where CheckConstantFitsNumericTarget
    // returns silently for a String target (no integral range) — admitted, CS0029 later. Drop
    // `elementType.IsNumeric()` and `New Integer() {"a"}` enters it, where TryFoldConstantDouble on a
    // string literal is simply false — admitted. Both must fall through to the non-literal refusal.
    [TestCase("Dim a() As String = New String() {1}", "cannot put an 'Integer' in a 'String()'")]
    [TestCase("Dim a() As Integer = New Integer() {\"a\"}", "cannot put a 'String' in an 'Integer()'")]
    public void TypedLiteral_NumericLiteralRule_Refuses(string body, string expectedInMessage)
    {
        var (ok, errors, _, _) = Analyze(body);
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains(expectedInMessage));
    }

    /// <summary>
    /// <c>Nothing</c> is typed <c>Object</c>, so it has its own arm: admitted into any reference (or
    /// synthetic .NET) element type, refused into a value type with the honest message.
    /// </summary>
    [Test]
    public void TypedLiteral_Nothing()
    {
        var (okS, errorsS, _, _) = Analyze("Dim a() As String = New String() {\"a\", Nothing}");
        Assert.That(okS, Is.True, string.Join("; ", errorsS));

        var (okShape, errorsShape, _, _) = Analyze(
            "Dim c As New Circle()\nDim all() As Shape = New Shape() {c, Nothing}", prelude: ShapePrelude);
        Assert.That(okShape, Is.True, string.Join("; ", errorsShape));

        var (okI, errorsI, _, _) = Analyze("Dim a() As Integer = New Integer() {Nothing}");
        Assert.That(okI, Is.False);
        Assert.That(errorsI, Has.Some.Contains("Nothing has no value of type 'Integer'; write 0"));
    }

    /// <summary>
    /// The value-type refusal's ADVICE is per kind. "write 0" for an enum sends the user to a second
    /// refusal (<c>New Color() {0}</c> is "cannot put an 'Integer' in a 'Color()'"), so each arm names
    /// the thing that actually has a value of that type. The head is the spec's fragment for all.
    /// </summary>
    [TestCase("Enum Color\nRed\nEnd Enum", "Dim a() As Color = New Color() {Nothing}",
        "Nothing has no value of type 'Color'; write a member of 'Color'")]
    [TestCase("Structure Pt\nPublic X As Integer\nEnd Structure", "Dim a() As Pt = New Pt() {Nothing}",
        "Nothing has no value of type 'Pt'; write New Pt()")]
    // A `Type … End Type` UDT is TypeKind.UserDefinedType, a value type distinct from Structure; without
    // its own arm the null default admitted Nothing into it (csc CS0037 later).
    [TestCase("Type Pt2\nX As Integer\nEnd Type", "Dim a() As Pt2 = New Pt2() {Nothing}",
        "Nothing has no value of type 'Pt2'; write New Pt2()")]
    [TestCase("", "Dim a() As Boolean = New Boolean() {Nothing}",
        "Nothing has no value of type 'Boolean'; write False")]
    [TestCase("", "Dim a() As Char = New Char() {Nothing}",
        "Nothing has no value of type 'Char'; write a character literal")]
    public void TypedLiteral_Nothing_IntoAValueType_IsRefusedWithKindAwareAdvice(string prelude, string body, string expectedInMessage)
    {
        var (ok, errors, _, _) = Analyze(body, prelude);
        Assert.That(ok, Is.False, "expected a refusal");
        Assert.That(errors, Has.Some.Contains(expectedInMessage));
    }

    /// <summary>
    /// Spec §9 names this row: <c>Nothing</c> into a synthetic .NET <c>T</c> is admitted without a check
    /// — the analyzer cannot see ToolStripItem, but it is a reference type and csc takes null.
    /// </summary>
    [Test]
    public void TypedLiteral_Nothing_IntoASyntheticNetType_IsAdmitted()
    {
        var (ok, errors, _, _) = Analyze(
            "Dim a As New ToolStripMenuItem()\nDim items() As ToolStripItem = New ToolStripItem() {a, Nothing}",
            prelude: "Using System.Windows.Forms");

        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    /// <summary>
    /// ⛔ Pins the KIND guard on the "unresolvable .NET → csc decides" predicate. IsNetType is
    /// PascalCase-permissive and says yes to <c>Integer[]</c>; without the guard a nested typed
    /// literal is exempted as ".NET" and <c>New Shape() {New Integer() {1}}</c> sails through.
    /// The positive row shows the same element admitted where it belongs — Object takes anything.
    /// </summary>
    [Test]
    public void TypedLiteral_NestedTypedLiteral_IsNotExemptAsNet()
    {
        var (okShape, errorsShape, _, _) = Analyze(
            "Dim all() As Shape = New Shape() {New Integer() {1}}", prelude: ShapePrelude);
        Assert.That(okShape, Is.False, "a nested Integer() literal is not a Shape");
        Assert.That(errorsShape, Has.Some.Contains("cannot put an 'Integer[]' in a 'Shape()'"));

        var (okObject, errorsObject, _, typeObject) = Analyze("Dim rows() As Object = New Object() {New Integer() {1, 2}}");
        Assert.That(okObject, Is.True, string.Join("; ", errorsObject));
        Assert.That(typeObject?.Name, Is.EqualTo("Object[]"));
    }

    /// <summary>
    /// The "either side unresolvable .NET → csc decides" exemption applies to the ELEMENT too: a
    /// synthetic GENERIC handle (<c>List(Of Integer)</c>) into a BasicLang <c>Shape()</c> is accepted
    /// here and judged by csc. Pinned so the predicate's spelling cannot drift.
    /// </summary>
    [Test]
    public void TypedLiteral_SyntheticGenericElement_IsAccepted()
    {
        var (ok, errors, _, _) = Analyze(
            "Dim l As New List(Of Integer)()\nDim all() As Shape = New Shape() {l}", prelude: ShapePrelude);

        Assert.That(ok, Is.True, string.Join("; ", errors));
    }

    /// <summary>An empty typed literal has no element to infer from and is typed by T all the same.</summary>
    [Test]
    public void TypedLiteral_Empty_IsTypedByT()
    {
        var (ok, errors, _, type) = Analyze("Dim s() As String = New String() {}");

        Assert.That(ok, Is.True, string.Join("; ", errors));
        Assert.That(type?.Name, Is.EqualTo("String[]"));
        Assert.That(type?.ElementType?.Name, Is.EqualTo("String"));
    }

    /// <summary>The untyped path's first pins: inferred type, and the mixed-types warning.</summary>
    [Test]
    public void UntypedLiteral_IsUnchanged()
    {
        var (okI, errorsI, _, typeI) = Analyze("Dim a() As Integer = {1, 2, 3}");
        Assert.That(okI, Is.True, string.Join("; ", errorsI));
        Assert.That(typeI?.Name, Is.EqualTo("Integer[]"));

        var (okO, errorsO, warningsO, typeO) = Analyze("Dim o() As Object = {1, \"a\"}");
        Assert.That(okO, Is.True, string.Join("; ", errorsO));
        Assert.That(typeO?.Name, Is.EqualTo("Object[]"));
        Assert.That(warningsO, Has.Some.Contains("mixed types"));
    }

    // ------------------------------------------------------------------------------------------
    // Task 3 — the IR builder coerces each element of a TYPED literal to T (spec §9 "Lowering").
    // The analyzer admits `New Double() {1, i}` (a literal by range, `i As Integer` by widening),
    // but the IR used to store the RAW values — an Integer constant and an Integer variable — into
    // a Double array. CoerceToDeclaredType's rules (`Dim x As Double = 1` / `= i`) apply per element.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the IR for <paramref name="body"/> inside <c>Sub Main()</c>: parse → analyze →
    /// <c>IRBuilder.Build</c>, the construction <c>BclE2E.CompileToCppOptimized</c> uses, but WITHOUT
    /// the optimizer — this pins what the builder emits, not what a constant folder makes of it.
    /// ⚠ Under <c>AddStandardPasses()</c> the typed test's <c>Dim i As Integer = 1</c> would be
    /// propagated into the cast and the cast folded, turning store 1's value into an <c>IRConstant</c>
    /// and failing the <c>IRCast</c> assertion — so this helper must stay optimizer-free, and Task 4's
    /// run rows use a parameter <c>i</c> for the same reason.
    /// Returns Main's one array allocation and its stores in emission order.
    /// </summary>
    private static (IRArrayAlloc alloc, List<IRArrayStore> stores) LowerMain(string body)
    {
        var source = "Sub Main()\n" + body + "\nEnd Sub";
        var parser = new Parser(new Lexer(source).Tokenize());
        var program = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(program), Is.True,
            string.Join("; ", analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(program, "T");

        var instructions = irModule.Functions.Single(f => f.Name == "Main")
            .Blocks.SelectMany(b => b.Instructions).ToList();
        var alloc = instructions.OfType<IRArrayAlloc>().Single();
        var stores = instructions.OfType<IRArrayStore>().Where(s => ReferenceEquals(s.Array, alloc)).ToList();
        return (alloc, stores);
    }

    /// <summary>
    /// A literal element is re-typed IN PLACE (an <c>IRConstant</c> now carrying a Double); a
    /// non-literal is WRAPPED in an <c>IRCast</c> Integer → Double. Same two rules as the Dim path.
    /// </summary>
    [Test]
    public void TypedLiteral_Lowering_RetypesALiteral_AndCastsANonLiteral()
    {
        var (alloc, stores) = LowerMain("Dim i As Integer = 1\nDim a() As Double = New Double() {1, i}");

        Assert.That(alloc.ElementType.Name, Is.EqualTo("Double"));
        Assert.That(stores, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(stores[0].Value, Is.TypeOf<IRConstant>(), "the literal element stays a constant");
            Assert.That(stores[0].Value.Type?.Name, Is.EqualTo("Double"), "the literal element is re-typed to T");
            // The CLR value converts too — a TypeInfo relabelled over an int32 would still emit an
            // int bit pattern on MSIL (see CoerceToDeclaredType's own note).
            Assert.That((stores[0].Value as IRConstant)?.Value, Is.TypeOf<double>().And.EqualTo(1.0), "the literal's CLR value");
            Assert.That(stores[1].Value, Is.TypeOf<IRCast>(), "the variable element is wrapped in a cast");
        });
        var cast = (IRCast)stores[1].Value;
        Assert.That(cast.SourceType?.Name, Is.EqualTo("Integer"));
        Assert.That(cast.Type?.Name, Is.EqualTo("Double"));
    }

    /// <summary>
    /// The UNTYPED literal stores raw, exactly as before: the analyzer types <c>{1, i}</c> by its
    /// common element type and the builder coerces nothing — no re-typed constant, no cast.
    /// </summary>
    [Test]
    public void UntypedLiteral_Lowering_StoresRaw()
    {
        var (alloc, stores) = LowerMain("Dim i As Integer = 1\nDim a() As Integer = {1, i}");

        Assert.That(alloc.ElementType.Name, Is.EqualTo("Integer"));
        Assert.That(stores, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(stores[0].Value, Is.TypeOf<IRConstant>(), "the literal element is a raw constant");
            Assert.That(stores[0].Value.Type?.Name, Is.EqualTo("Integer"), "the literal element keeps its own type");
            Assert.That((stores[0].Value as IRConstant)?.Value, Is.TypeOf<int>(), "the literal's CLR value is untouched");
            Assert.That(stores[1].Value, Is.Not.TypeOf<IRCast>(), "the variable element is not wrapped");
        });
    }
}
