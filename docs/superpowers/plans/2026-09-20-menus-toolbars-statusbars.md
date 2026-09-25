# Menus, toolbars and status bars (Task 24) — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship MenuStrip / ToolStrip / StatusStrip with their items in the form designer on both targets, with the VS "Type Here" in-place editor — after first landing the compiler change the brief asked for (`New T() { … }`) as its own gated commit.

**Architecture:** Five commits, each green on the fast subset and its Integration fixtures (spec Decision 12). **24a** the compiler change plus the JavaScript backend's missing array-literal arms. **24b** the catalog SHAPE (`FormControlDef.Place`, `FormItemRule`, `FormCatalogShapes`) and every gate that must learn it — with NO new row, so nothing can go red. **24c** the seven rows + reader/writer/clipboard/region writer/emitter/recognizer + band layout and drawing + placement/toolbox/grid. **24d** the editing surface (cells, dropdowns, Type Here, the overlay control, paste). **24e** retarget + acceptance on both targets + records + IDE drop.

**Spec:** `docs/superpowers/specs/2026-09-19-menus-toolbars-statusbars-design.md` (@ `fd38201`, four review passes). Read §1–§2 before 24b, §9 before 24a, §6 before 24d. Every rule below that says "spec §N" is binding; where this plan and the spec disagree, the spec wins and the plan is wrong.

**Tech Stack:** C# / .NET 8, NUnit, Avalonia 11.3 + Avalonia.Headless (Skia), the real `BasicLang.exe` CLI (`CliTestHarness.CliPath()`), Roslyn in-process (`WinFormsCompile`, `CSharpRun`), MSVC via `CppCompile`, node via `JavaScriptExecutionTests.RunNodeScript` / `RunPageUnderNode`.

**House rules that bite here** (from `CLAUDE.md` and the Task 21/25 records): one task → one commit → one gate with ACTUAL totals; a passing test prints nothing at normal verbosity — re-run named rows with `--no-build --filter`; "compiles" is not "runs" — the acceptance fixtures RUN both targets; every new test gets a mutant that kills it; never `With`, never `Handles`; `dotnet clean` the Shell after AXAML changes; commit messages via a file + `git commit -F`, trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`; never round-trip repo files through PowerShell `Get-Content`/`Set-Content`; the fixture form is `MenuForm`, never `F` (chip `task_ef845b99`).

**Test commands** (from the repo root):

```powershell
dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --nologo -v q
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --no-build --nologo --filter "FullyQualifiedName~<Fixture>" --logger "console;verbosity=normal"
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --no-build --nologo --filter "TestCategory!=Integration"   # fast subset
```

---

## File structure

**24a — compiler**
- Modify `BasicLang/Parser.cs` — `New` expression branch (~`:4280-4298`), `Dim … As New` branch (~`:2504-2525`).
- Modify `BasicLang/SemanticAnalyzer.cs` — `Visit(CollectionInitializerNode)` (~`:6637-6667`); new helpers `IsUnresolvableNetName`, `CheckTypedLiteralElement`.
- Modify `BasicLang/IRBuilder.cs` — `Visit(CollectionInitializerNode)` (~`:1893-1922`) coerces each element via `CoerceToDeclaredType` (`:3501`).
- Modify `BasicLang/JavaScriptBackend.cs` — `Visit(IRArrayAlloc)` / `Visit(IRArrayStore)` (`:2379-2380`), `Expr` default arm (`:747-748`), and `Visit(IRStore)` (`:2193-2197` — the THIRD arm, found by plan review pass 3).
- Modify `BasicLang/CSharpBackend.cs` — `Visit(IRArrayStore)` (`:3549-3555`) and `Visit(IRIndexerStore)` (`:3564`) render the stored value through `EmitExpression`, not `GetValueName` (found by Task 3's spec review: an `IRCast` in a store came out as an undeclared temp, CS0103).
- Modify `BasicLang/ASTPrettyPrinter.cs` — `Visit(CollectionInitializerNode)` (`:827`).
- Create `VisualGameStudio.Tests/Compiler/TypedArrayLiteralTests.cs` (parser + analyzer + C# emission + untyped pins), `VisualGameStudio.Tests/Compiler/TypedArrayLiteralExecutionTests.cs` (`[Category("Integration")]`: C# run, C++ run, JS run, the M4/M3/two-file CLI rows).

**24b — the shape, no rows**
- Modify `BasicLang/Forms/FormControlCatalog.cs` — `FormPlace` enum, `FormItemRule` record, `FormControlDef.Place`/`Items`/`FormProperty`/`HtmlChildrenWrapper`/`HtmlRole`/`WebCss`, `IsComponent` derived; seven `FormSchematic` values.
- Create `VisualGameStudio.Tests/Compiler/FormCatalogShapes.cs` — `Canonical`, `Locate` (test helper, `internal static`).
- Modify the gates: `VisualGameStudio.Tests/Compiler/WinFormsCatalogSweepTests.cs` (three builders), `VisualGameStudio.Tests/Shell/FormCanvasRenderTests.cs` (fixture + Item exclusion), `VisualGameStudio.Tests/Compiler/FormRetargetTests.cs` (sweep), `VisualGameStudio.Tests/Compiler/FormPropertyGridTests.cs:552-568`, `VisualGameStudio.Tests/Compiler/FormToolboxGlyphTests.cs:75-89` (⚠ in `Compiler/`, not `Shell/`), `VisualGameStudio.Tests/Compiler/FormDocumentTests.cs:80-92`.
- Modify `VisualGameStudio.Shell/Controls/FormCanvasControl.cs` — extract `DrawSchematic(context, schematic, bounds, label, face, client, ink)` seam from `DrawControl` (~`:1420-1890`); `FormToolboxViewModel.cs` — `GlyphFor` arms, `Rebuild` excludes `Place == Item`.
- Create `VisualGameStudio.Tests/Shell/FormSchematicPinTests.cs` — enum-driven pairwise frame hash + glyph pins.

**24c — rows, format, emission, bands**
- Modify `FormControlCatalog.cs` (seven rows), `DesignDiagnostic.cs` (BL8030), `Serialization/FormDocumentReader.cs`, `Serialization/FormDocumentWriter.cs`, `FormDocument.cs` (`FormClipboard`, `RenumberTabIndexes`), `RegionWriter.cs`, `FormAssetEmitter.cs`, `Recognizer/WinFormsDialect.cs`; `VisualGameStudio.Shell/Controls/FormCanvasTransform.cs` (⚠ in `Controls/`, NOT `ViewModels/Designer/` — do not create a second file; `Layout(document, selected)`, bands), `FormCanvasControl.cs` (band arms), `FormPlacement.cs` (Docked branch, `PlaceItem`), `FormToolboxViewModel.cs` (category), `FormPropertyGridViewModel.cs`, `CodeEditorDocumentViewModel.cs` (`TrayDrop`/canvas drop refusal of an Item kind).
- Create `VisualGameStudio.Tests/Compiler/FormStripDocumentTests.cs`, `FormStripEmissionTests.cs`, `FormStripRecognizerTests.cs`; `VisualGameStudio.Tests/Shell/FormStripLayoutTests.cs`.

**24d — the editing surface**
- Modify `FormCanvasTransform.cs` (cells, dropdowns, slots, `HitTest(document, point, selected)`, `TypeHereAt`), `FormCanvasControl.cs` (`TypeHereHost`, `TypeHereBounds`, `BeginTypeHereCommand`, cell/slot drawing), `CodeEditorDocumentViewModel.cs` (`StripEditor`, `BeginTypeHere`/`CommitTypeHere`/`CancelTypeHere`, paste into a host), `Views/Documents/CodeEditorDocumentView.axaml` (+ `.axaml.cs`).
- Create `VisualGameStudio.Shell/Controls/FormTypeHereEditor.cs`, `VisualGameStudio.Shell/ViewModels/Designer/FormStripEditorViewModel.cs`.
- Create `VisualGameStudio.Tests/Shell/FormTypeHereEditorTests.cs`, `FormStripCanvasTests.cs`; `VisualGameStudio.Tests/Compiler/FormStripEditorTests.cs`.

**24e — retarget, acceptance, records**
- Modify `FormRetarget.cs` (`Place` exclusion), `FormRetargetTests.cs`, `FormDesignerAcceptanceTests.cs` (`RunPageUnderNode(outDir, formName, clickId)`), docs, memory.
- Create `VisualGameStudio.Tests/Compiler/FormMenuAcceptanceTests.cs`.

---

# Commit 24a — `New T() { … }` (spec §9)

### Task 1: The parser — the brace after the argument list

**Files:** Modify `BasicLang/Parser.cs:4280-4298` and `:2504-2525`; Create `VisualGameStudio.Tests/Compiler/TypedArrayLiteralTests.cs`.

- [x] **Step 1: Write the failing parser tests**

```csharp
using BasicLang.Compiler;                  // Lexer, Parser
using BasicLang.Compiler.AST;              // the node types
using BasicLang.Compiler.SemanticAnalysis; // SemanticAnalyzer, TypeInfo, ErrorSeverity
using BasicLang.Compiler.IR;               // IRBuilder, IRArrayAlloc, IRArrayStore, IRCast
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

    private static ExpressionNode FirstInitializer(ProgramNode program) =>
        ((VariableDeclarationNode)((SubroutineNode)program.Declarations[0]).Body.Statements[0]).Initializer!;

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

    [Test]
    public void NewTypeParensBraces_InACallArgument_IsTheLiteral()
    {
        // The motivating shape — `Items.AddRange(New ToolStripItem() {…})` (spec §9, M4): after the
        // literal returns, control goes back to the enclosing call's argument loop, which must see `)`.
        var program = Parse("Foo(New Integer() {1, 2})", out var parser);
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var statement = ((SubroutineNode)program.Declarations[0]).Body.Statements[0];
        var call = (CallExpressionNode)((ExpressionStatementNode)statement).Expression;   // ⚠ real node names — see below
        var literal = (CollectionInitializerNode)call.Arguments[0];
        Assert.That(literal.ElementType?.Name, Is.EqualTo("Integer"));
        Assert.That(literal.Elements, Has.Count.EqualTo(2));
    }

    [Test]
    public void NewTypeParensBraces_Nests()
    {
        var program = Parse("Dim rows() As Object = New Object() {New Integer() {1, 2}}", out var parser);
        Assert.That(parser.Errors, Is.Empty);
        var outer = (CollectionInitializerNode)FirstInitializer(program);
        Assert.That(outer.ElementType?.Name, Is.EqualTo("Object"));
        Assert.That(((CollectionInitializerNode)outer.Elements[0]).ElementType?.Name, Is.EqualTo("Integer"));
    }

    [TestCase("Dim items() As Integer = New Integer {1, 2}", "New Integer() {")]
    [TestCase("Dim items() As Integer = New Integer(2) {1, 2, 3}", "the initializer sets the size")]
    [TestCase("Dim items As New Integer() {1, 2}", "Dim items() As Integer = New Integer() {")]
    [TestCase("Dim items As New Integer {1, 2}", "Dim items() As Integer = New Integer() {")]
    [TestCase("Dim items() As Integer = New List(Of Integer) {1}", "New List(Of Integer)() {")]   // generic args survive
    public void TheThreeRefusals_NameTheFix(string body, string expectedInMessage)
    {
        Parse(body, out var parser);
        Assert.That(parser.Errors, Is.Not.Empty, "expected a parse refusal");
        // The FIRST error is the one the IDE shows (ParserErrorTests.cs:31) — a join would let a
        // spurious second error after Synchronize pass unnoticed.
        Assert.That(parser.Errors[0].Message, Does.Contain(expectedInMessage));
    }
}
```

⚠ Check the real AST node names first: `Grep "class ProgramNode|class SubroutineNode|class VariableDeclarationNode" BasicLang/ASTNodes.cs` — the test uses whatever the tree's actual root/sub/declaration types are; adjust the two casts, not the assertions. ⚠ Check how `Parser.Errors` exposes messages (`ParserErrorTests.cs:30-31` uses `parser.Errors[0].Message`).

- [x] **Step 2: Run to verify it fails**

`--filter "FullyQualifiedName~TypedArrayLiteralTests"` → the first two fail (a `NewExpressionNode` comes back / errors present), the refusal cases fail on the message text.

- [x] **Step 3: Implement the expression path** in `Parser.cs` inside `if (Match(TokenType.New))` (`:4280`). After the `if (Match(TokenType.LeftParen)) { … }` block and before `return newExpr;`:

```csharp
                // Task 24a — `New T() { e1, e2 }`: an array creation WITH an initializer. Measured
                // before this branch existed: `New ToolStripItem()` was a parameterless constructor
                // call and the `{` fell to the enclosing call's "Expected ')' after arguments".
                // ⛔ The parentheses are REQUIRED (VB's shape), and a stated size is refused: VB's
                // `New T(2) {…}` names an UPPER BOUND while this compiler's array sizes are element
                // COUNTS, so accepting it would be a silent off-by-one.
                if (Check(TokenType.LeftBrace))
                {
                    if (!sawParens)
                    {
                        throw new ParseException(
                            "An array creation with an initializer needs its parentheses: write `New " +
                            $"{newExpr.Type}() {{…}}`", Peek(), "Add `()` after the type name.");
                    }

                    if (newExpr.Arguments.Count > 0)
                    {
                        // ⚠ The spec's phrase, and the test's: "the initializer sets the size".
                        throw new ParseException(
                            "An array creation with an initializer cannot also state a size — the " +
                            $"initializer sets the size; write `New {newExpr.Type}() {{…}}` (VB's " +
                            "`New T(n) {…}` names an upper bound, and this compiler's sizes are element counts)",
                            Peek(), "Remove the size.");
                    }

                    return ParseTypedCollectionInitializer(newExpr.Type);
                }

                return newExpr;
```

where `sawParens` is a `bool` set to true inside the existing `if (Match(TokenType.LeftParen))` block (declare `var sawParens = false;` before it). ⚠ The messages interpolate the `TypeReference` ITSELF (its `ToString()`, `ASTNodes.cs:274-279`), never `.Name`: `Name` is the bare identifier and drops generic arguments, so `New List(Of Integer) {1}` would otherwise be told to write `New List() {…}` — a different type (code-quality review of Task 1). And:

```csharp
        /// <summary>The brace list of `New T() { … }`, typed by T (spec §9).</summary>
        private CollectionInitializerNode ParseTypedCollectionInitializer(TypeReference elementType)
        {
            Consume(TokenType.LeftBrace, "Expected '{'");
            var token = Previous();
            var node = new CollectionInitializerNode(token.Line, token.Column) { ElementType = elementType };

            if (!Check(TokenType.RightBrace))
            {
                do
                {
                    node.Elements.Add(ParseExpression());
                } while (Match(TokenType.Comma));
            }

            Consume(TokenType.RightBrace, "Expected '}' after array initializer");
            return node;
        }
```

⚠ Look at how the existing `New` branch reports errors (`throw new ParseException(...)` at `:4464` is the shape in `ParsePrimary`) and match the constructor arity you find. The parser is error-tolerant at statement level (`ParserErrorTests.cs:26-31`: `parser.Parse()` then `parser.Errors`) — a thrown `ParseException` inside an expression is collected into `Errors` by the statement loop; the tests read `parser.Errors[i].Message`.

- [x] **Step 4: Implement the `Dim … As New` refusal** at `:2504-2525`: AFTER the closing brace of `if (Match(TokenType.LeftParen)) { … }` (so it fires with or without parentheses) and before `node.Type = newExpr.Type;`:

```csharp
                    // Task 24a: VB has no `Dim x As New T() {…}`; the array form is `Dim x() As T = New T() {…}`.
                    if (Check(TokenType.LeftBrace))
                    {
                        throw new ParseException(
                            $"`Dim {node.Name} As New {newExpr.Type}() {{…}}` is not a form: write " +
                            $"`Dim {node.Name}() As {newExpr.Type} = New {newExpr.Type}() {{…}}`",
                            Peek(), "Move the initializer after `=`.");
                    }
```

- [x] **Step 5: Run** the fixture → all green. Run `--filter "FullyQualifiedName~ParserErrorTests|FullyQualifiedName~CompilationTests"` → still green (the bare-brace path at `:4446` is untouched).

### Task 2: The analyzer — typing a typed literal

**Files:** Modify `BasicLang/SemanticAnalyzer.cs` `Visit(CollectionInitializerNode)` (`:6637-6667`); Test: `TypedArrayLiteralTests.cs`.

- [x] **Step 1: Write the failing analyzer tests** (append to the fixture). Helper:

```csharp
    private static (bool ok, List<string> errors, List<string> warnings, TypeInfo? type) Analyze(string body, string prelude = "")
    {
        var source = prelude + "\nSub Main()\n" + body + "\nEnd Sub";
        var parser = new Parser(new Lexer(source).Tokenize());
        var program = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(program);
        // ⛔ ONE list, two severities: the analyzer has no `Warnings` member. `Warning(...)` appends an
        // ErrorSeverity.Warning entry to `Errors` (SemanticAnalyzer.cs:255, :1934-1936), and every
        // synthetic .NET type produces BL6016-style warnings, so the split below is what makes
        // "no error" and "a warning containing …" mean anything.
        var errors = analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).Select(e => e.Message).ToList();
        var warnings = analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Warning).Select(e => e.Message).ToList();
        var init = LastInitializer(program);   // the LAST Dim's initializer — bodies are several lines
        return (ok, errors, warnings, analyzer.GetNodeType(init));   // GetNodeType is public, returns TypeInfo
    }
```

Tests (one `[Test]` each, names as given). ⛔ Elements are declared with `As New T()`, never `= Nothing` — `Nothing` is typed `Object` and the Dim check refuses `Dim a As ToolStripMenuItem = Nothing` today (BL3001, recorded at `NetGeneratedShimConformanceTests.cs:263`); the measured scratch fixtures all used `New`.
- `TypedLiteral_OfTwoUnresolvableNetTypes_IsTypedByT_WithNoWarning`: prelude `Using System.Windows.Forms`, body `Dim a As New ToolStripMenuItem()\nDim s As New ToolStripSeparator()\nDim items() As ToolStripItem = New ToolStripItem() {a, s}` → `ok`, no warning containing "mixed types", type name `ToolStripItem[]`, `ElementType.Name == "ToolStripItem"`.
- `TypedLiteral_OverBasicLangClasses_Widens_AndAnAbstractElementTypeIsLegal`: prelude `MustInherit Class Shape\nEnd Class\nClass Circle\nInherits Shape\nEnd Class\nClass Square\nInherits Shape\nEnd Class` (⚠ `MustInherit`, on purpose — spec §9's second pin: an array OF an abstract type is legal, and `Visit(NewExpressionNode)`'s abstract refusal must not be reached for the typed literal; `Parser.cs:119/:254` set `IsAbstract`), body `Dim c As New Circle()\nDim s As New Square()\nDim all() As Shape = New Shape() {c, s}` → ok, no error mentioning `abstract`.
- `TypedLiteral_RefusesAnUnrelatedElement`: same prelude, body `Dim x As String = "a"\nDim all() As Shape = New Shape() {x}` → error containing `cannot put a 'String' in a 'Shape()'`.
- `TypedLiteral_RefusesNarrowing_ForNonLiterals`: `Dim d As Double = 1.5\nDim l As Long = 1\nDim a() As Integer = New Integer() {d}` → error `cannot put a 'Double' in an 'Integer()'` (⚠ the article: the implementation uses an `Article()` helper — `an` before a vowel-initial type name — so the spec's two spellings are both produced); and `{l}` → `'Long'`.
- `TypedLiteral_NumericLiteralRule`: admitted — `New Byte() {65}`, `New Long() {1}`, `New Double() {1}`, `New Decimal() {1.5}`, `New Single() {1.5}`, AND the negative spellings `New Short() {-1}`, `New Single() {-1.5}`, `New Decimal() {-1.5}` (a `-` literal is a unary node wrapping the literal; the rule must see through it); refused — `New Integer() {1.5}` and `New Integer() {-1.5}` (error names Double→Integer), `New Byte() {300}` (message contains `not representable` — the BC30439 text `CheckConstantFitsNumericTarget` emits; read its message at `:1633-1665` and assert a stable fragment).
- `TypedLiteral_Nothing`: `New String() {"a", Nothing}`, (prelude Shape) `New Shape() {c, Nothing}` AND (prelude `Using System.Windows.Forms`) `New ToolStripItem() {a, Nothing}` — a synthetic .NET T, which the spec names explicitly — admitted; `New Integer() {Nothing}` refused with `Nothing has no value of type 'Integer'; write 0`. ⚠ AS BUILT: the advice after the `;` is BY TARGET KIND — numeric "write 0", Boolean "write False", Char "write a character literal", a `Structure` "write New T()", an `Enum` "write a member of 'T'" — because "write 0" for an enum sends the user to a SECOND refusal (`New Color() {0}` is refused by the non-literal arm; spec review traced it). One `[TestCase]` per arm (Enum, Structure, Boolean, Char), each red-first or mutant-proven.
- `TypedLiteral_AdmitsWidening_ForNonLiterals` (5 rows: Integer→Long/Double/Decimal, Byte→Integer, Short→Long) and `TypedLiteral_RefusesSignedIntoUnsigned_ForNonLiterals` (Integer→UInteger) — the pins for the AS-BUILT `WidensTo` (see Step 3's note).
- `TypedLiteral_NestedTypedLiteral_IsNotExemptAsNet`: prelude Shape; `New Shape() {New Integer() {1}}` → refused `cannot put an 'Integer[]' in a 'Shape()'`; `New Object() {New Integer() {1, 2}}` → ok, `Object[]` — the pin for the Kind guard on the exemption predicate (mutant: delete the guard → the nested literal is exempted as "csc decides").
- `TypedLiteral_SyntheticGenericElement_IsAccepted`: prelude Shape; body `Dim l As New List(Of Integer)()\nDim all() As Shape = New Shape() {l}` → ok (csc decides).
- `UntypedLiteral_IsUnchanged`: `Dim a() As Integer = {1, 2, 3}` → type `Integer[]`; `Dim o() As Object = {1, "a"}` → type `Object[]` AND a warning containing `mixed types`.

- [x] **Step 2: Run** → all fail (the typed cases come back `Object[]`/warned/wrongly refused).

- [x] **Step 3: Implement.** Replace the body of `Visit(CollectionInitializerNode)`:

```csharp
        public void Visit(CollectionInitializerNode node)
        {
            if (node.ElementType != null)
            {
                VisitTypedCollectionInitializer(node);
                return;
            }

            // — the existing untyped body, byte for byte —
        }

        /// <summary>
        /// `New T() { … }` (Task 24a, spec §9). The element type is STATED, so nothing is inferred and
        /// the "mixed types" warning never applies. Three policies, deliberately distinct from the
        /// Dim path's and from RejectImpossibleConversion's:
        ///   • a numeric LITERAL: integral literals into any numeric target, floating literals into
        ///     Single/Double/Decimal only — stricter than IsNumericLiteralAssignable on purpose;
        ///   • `Nothing`: into any reference or unresolvable .NET T, never into a value type;
        ///   • otherwise: T, or a WIDENING to T (IsAssignableFrom minus its permissive narrowing arm);
        ///   • either side an unresolvable .NET type → accepted, csc decides.
        /// </summary>
        private void VisitTypedCollectionInitializer(CollectionInitializerNode node)
        {
            var elementType = ResolveTypeReference(node.ElementType);
            if (elementType == null)
            {
                Error($"Unknown type '{node.ElementType.Name}' in array initializer", node.Line, node.Column);
                elementType = _typeManager.ObjectType;
            }

            var targetIsUnresolvedNet = IsUnresolvableNetName(elementType.Name);

            foreach (var element in node.Elements)
            {
                element.Accept(this);
                CheckTypedLiteralElement(element, elementType, targetIsUnresolvedNet);
            }

            var arrayType = new TypeInfo($"{elementType.Name}[]", TypeKind.Array)
            {
                ElementType = elementType,
                ArrayRank = 1
            };
            SetNodeType(node, arrayType);
        }

        /// <summary>
        /// "ResolveTypeName fell to its synthetic .NET branch", re-derived from the NAME in that
        /// method's own order — a TypeInfo carries no synthetic marker, and a sibling-file BasicLang
        /// class lives in scope rather than in _typeManager, which is what makes the
        /// `_typeManager.GetType(name) == null && IsNetType(name)` spelling exempt too much.
        /// </summary>
        private bool IsUnresolvableNetName(string? name) =>
            !string.IsNullOrEmpty(name) &&
            !IsUserDefinedTypeName(name) &&
            _typeManager.GetType(name) == null &&
            IsNetType(name);

        private void CheckTypedLiteralElement(ExpressionNode element, TypeInfo target, bool targetIsUnresolvedNet)
        {
            var elementType = GetNodeType(element);

            if (IsNothingLiteral(element))
            {
                if (target.IsNumeric() || target.Name == "Boolean" || target.Name == "Char" ||
                    target.Kind == TypeKind.Structure)
                {
                    Error($"Nothing has no value of type '{target.Name}'; write 0", element.Line, element.Column);
                }
                return;
            }

            // ⚠ `-1` and `-1.5` are a UnaryExpressionNode WRAPPING the literal (Parser.cs:4085-4093, no
            // constant folding), typed as the operand. Unwrap a leading +/- before the literal test,
            // exactly as TryRetypeLiteralToDecimal does (:1825-1826) — or every negative literal takes
            // the non-literal path and `New Single() {-1.5}` / `New Short() {-1}` are refused.
            var bare = element is UnaryExpressionNode { Operator: "-" or "+" } sign ? sign.Operand : element;

            if (bare is LiteralExpressionNode literal && elementType != null && elementType.IsNumeric() && target.IsNumeric())
            {
                if (target.Name == "Decimal" && TryRetypeLiteralToDecimal(element, target))
                {
                    return;
                }

                if (elementType.IsFloatingPoint() && target.IsIntegral())
                {
                    Error($"cannot put {Article(elementType.Name)} '{elementType.Name}' in {Article(target.Name)} '{target.Name}()' — " +
                          "a floating literal never narrows into an integral array; write an integer or change the element type",
                          element.Line, element.Column);
                    return;
                }

                CheckConstantFitsNumericTarget(element, target, "an element of the array initializer",
                    element.Line, element.Column);
                return;
            }

            if (elementType == null)
            {
                return;   // an unresolved expression; the untyped visitor skips these too
            }

            if (targetIsUnresolvedNet || IsUnresolvableNetName(elementType.Name))
            {
                return;   // csc decides — ToolStripMenuItem into ToolStripItem() is exactly this
            }

            if (target.Equals(elementType) || WidensTo(elementType, target))
            {
                return;
            }

            Error($"cannot put {Article(elementType.Name)} '{elementType.Name}' in {Article(target.Name)} '{target.Name}()'",
                element.Line, element.Column);
        }

        /// <summary>"a Double", "an Integer" — the spec's messages use both, so the article is computed.</summary>
        private static string Article(string typeName) =>
            typeName.Length > 0 && "AEIOUaeiou".IndexOf(typeName[0]) >= 0 ? "an" : "a";

        /// <summary>IsAssignableFrom WITHOUT its permissive narrowing arm (SymbolTable.cs:195).</summary>
        private static bool WidensTo(TypeInfo source, TypeInfo target)
        {
            if (source.IsNumeric() && target.IsNumeric())
            {
                if (target.IsIntegral() && (source.IsFloatingPoint() || source.IsIntegral()))
                {
                    // Integral ← anything is the narrowing arm, EXCEPT the genuine widening Long ← Integer,
                    // which IsAssignableFrom lists before that arm.
                    return target.Name == "Long" && source.Name == "Integer";
                }
            }

            return target.IsAssignableFrom(source);
        }
```

⚠ Read `TypeInfo`'s helpers before relying on them (`IsNumeric`, `IsIntegral`, `IsFloatingPoint` exist — `SymbolTable.cs` around `:146-215`; check the exact member names). ⚠ `CheckConstantFitsNumericTarget`'s signature is `(ExpressionNode value, TypeInfo target, string context, int line, int column)` (`:1633`). ⚠ For the `Nothing` value-type test, `TypeKind.Structure` is the user-structure kind — confirm the enum member name (grep `enum TypeKind`).

⚠⚠ **AS BUILT (Task 2, 2026-09-20) — four deviations from the code above, each verified by the spec reviewer against the code and pinned; do not "fix" them back:**
1. **`WidensTo` decides integral←integral by numeric RANGE** (`TryGetIntegralRange` both sides, source ⊆ target), refuses any floating/Decimal→integral, and otherwise defers to `IsAssignableFrom`. The spelling above (`Long ← Integer` only) was wrong: at `SymbolTable.cs:163-196` the only integral pair listed BEFORE the permissive arm is `Long ← Integer`, so Byte→Integer, Short→Long, UInteger→Long were admitted only BY the arm, and excluding the whole arm refuses a genuine VB widening — **the spec's "IsAssignableFrom minus its permissive arm" spelling contradicts its own word "widening"; the implementation follows the word.** The spec's own rows all hold (Double→Integer, Long→Integer refused; Integer→Long/Double/Decimal admitted; Integer→Single admitted, as in VB). Pinned by `TypedLiteral_AdmitsWidening_ForNonLiterals` / `TypedLiteral_RefusesSignedIntoUnsigned_ForNonLiterals`. Record this in spec §10 at commit time.
2. **`IsUnresolvableNetName(string)` is `IsUnresolvableNetType(TypeInfo)` with a `Kind is Class or Delegate` guard.** `IsNetType` (`:2651`) is PascalCase-permissive — `Integer[]` (a nested typed literal's own array name) and a type parameter `TItem` both pass it and neither is in the type manager — so the name-only predicate exempted a nested typed literal as "csc decides". Every synthetic .NET mint is `TypeKind.Class` (`:2278`, `:2587`, `:4522`, `:8650`, …) or `Delegate`; every user type is in `_typeManager`. `!IsUserDefinedTypeName` is retained (Task 5's two-file row). Pinned by `TypedLiteral_NestedTypedLiteral_IsNotExemptAsNet`.
3. **`ResolveTypeReference` never returns null** (it reports "Unknown type" itself and answers `ObjectType`, `:2302-2306`), so the null branch above is dead and would double-report; it is `?? _typeManager.ObjectType`.
4. **The `Nothing` value-type arm also refuses `TypeKind.Enum`** (an enum is a value type; spec §9 "never into a value type"), and the advice is BY KIND (Step 1's `TypedLiteral_Nothing` note).
Also: `Article("UInteger")` is "a" (a `U` followed by a capital is the unsigned family), and the plan's single `TypedLiteral_NumericLiteralRule` is split into `_Admits` (8 rows) / `_Refuses` (6 rows — the code-quality review added `New String() {1}` and `New Integer() {"a"}`, the two rows that kill the literal arm's `elementType.IsNumeric() && target.IsNumeric()` guard one side each, and `New Byte() {-1}` for BC30439 through the unary). The `Nothing` advice arm also covers `TypeKind.UserDefinedType` (`Type … End Type` is a value type; `write New T()`), pinned by a fifth advice row; `TypedLiteral_Empty_IsTypedByT` pins the zero-element path. Task 2's final fixture count: 45 (10 parser + 35 analyzer).

- [x] **Step 4: Run** the fixture → green. Run `--filter "FullyQualifiedName~CompilationTests|FullyQualifiedName~CppCollectionTests|FullyQualifiedName~ReturnCoercionTests"` (bare-literal users) → green.

### Task 3: The IR builder — coerce each element

**Files:** Modify `BasicLang/IRBuilder.cs:1893-1922`.

- [x] **Step 1: Write the failing test** in `TypedArrayLiteralTests` — `TypedLiteral_Lowering_RetypesALiteral_AndCastsANonLiteral`: build the IR for `Dim i As Integer = 1\nDim a() As Double = New Double() {1, i}` (`new IRBuilder(analyzer).Build(program, "T")`, as `BclE2E.CompileToCppOptimized` does at `CppBclEndToEndTests.cs:49-54`); walk `irModule.Functions` (`IRNodes.cs:1406`) → `function.Blocks` (a `List<BasicBlock>`, `:1310`) → `block.Instructions` (`:1252`); find the `IRArrayAlloc` (`:915`), assert `ElementType.Name == "Double"`; find the two `IRArrayStore`s (`:935`): the first's `Value` is an `IRConstant` whose `Type.Name == "Double"` (re-typed in place), the second's `Value` is an `IRCast` (`:797`; a non-literal is wrapped). No existing test references `IRArrayStore` — this is the first. ⚠ AS BUILT: a second test `UntypedLiteral_Lowering_StoresRaw` pins the untyped path (neither store is an `IRCast`; the constant keeps its own type), and its fixture is `Dim a() As Integer = {1, i}` — the `As Double` spelling does NOT analyze (`Cannot assign value of type 'Integer[]' to variable of type 'Double[]'`, measured through the CLI: the bare literal is typed by its elements and `Double[]` does not accept `Integer[]`). The typed test also asserts the re-typed constant's CLR value is a `double` (a relabelled TypeInfo over an Int32 would pass `Type.Name` alone). ⚠ The `node.ElementType != null && value != null` guard is REDUNDANT BY ANALYSIS today (spec review, Task 3): the untyped analyzer never promotes — `TypeInfo.Equals` is by exact name, so `{1, 2L}` and `{1.5, 2}` are `Object[]` and `CoerceToDeclaredType(_, Object)` returns at once — every untyped element's IR type IS its analyzer type (so `declared.Name == actual.Name`), and `CoerceToDeclaredType(null, …)` returns null. `UntypedLiteral_Lowering_StoresRaw` is therefore a PIN, not a mutant-killer (the "coerce unconditionally" mutant survives by construction; the "coerce nothing" mutant is the RED run's three assertions). The guard becomes load-bearing the day followup 23 (common-base widening for the UNTYPED literal) changes `SemanticAnalyzer.cs:6659-6664` — keep it, and do not report it as dead.

- [x] **Step 2: Run** → fails (both stores are raw).

- [x] **Step 3: Implement.** In `Visit(CollectionInitializerNode)` (`:1902-1906`), replace the element loop:

```csharp
            foreach (var element in node.Elements)
            {
                element.Accept(this);
                var value = _expressionResult;

                // Task 24a: a TYPED literal coerces each element to T — a literal is re-typed in
                // place, a non-literal is wrapped in an IRCast (CoerceToDeclaredType's own rules).
                // The untyped literal stores raw, as before.
                if (node.ElementType != null && value != null)
                {
                    value = CoerceToDeclaredType(value, elementType);
                }

                elements.Add(value);
            }
```

- [x] **Step 4: Run** → green.

### Task 4: The JavaScript backend — the missing array arms

**Files:** Modify `BasicLang/JavaScriptBackend.cs:2379-2380`, the `Expr` switch (`:747-748`) and `Visit(IRStore)` (`:2193-2197`); Modify `BasicLang/CSharpBackend.cs` `Visit(IRArrayStore)` (`:3549-3555`) and `Visit(IRIndexerStore)` (`:3564`) — Step 3b; Test: `TypedArrayLiteralExecutionTests.cs` (new, `[Category("Integration")]`).

- [x] **Step 1: Write the failing tests**

```csharp
[TestFixture]
[Category("Integration")]
public class TypedArrayLiteralExecutionTests
{
    private static void RequireNode()
    {
        // ⛔ FAIL loudly, never Assert.Ignore: these are the first tests of a backend arm that has
        // never run, and a pass-by-absence is what this project calls a false green.
        Assert.That(BasicLang.Runtime.NodeLocator.Find(), Is.Not.Null, "node is required for this row");
    }

    private const string SumTyped = "Sub Main()\nDim a() As Integer = New Integer() {1, 2, 3}\nDim total As Integer = 0\nFor Each x As Integer In a\ntotal = total + x\nNext\nConsole.WriteLine(\"SUM \" & total)\nEnd Sub";
    private const string SumBare  = "Sub Main()\nDim a() As Integer = {1, 2, 3}\nDim total As Integer = 0\nFor Each x As Integer In a\ntotal = total + x\nNext\nConsole.WriteLine(\"SUM \" & total)\nEnd Sub";
    // ⚠ The Double program prints the NUMBER ALONE. The C++ backend DELIBERATELY refuses floating-point
    // string concatenation (CppCodeGenerator.cs:2192-2198 — `StringifyForText` has no Single/Double arm;
    // :2245-2277 — `"SUM " & aDouble` becomes `std::string("SUM ") + aDouble`, the intended build break),
    // while its print arm formats a Double .NET-style (CppBclEndToEndTests.cs:698-710 → `19.99`).
    // C# prints `3` for 3.0 and node prints `3`, so one expected string serves all three.
    // ⛔ `i` is a PARAMETER, never `Dim i As Integer = 2`. The optimizer folds IRCast(IRConstant)
    // (IROptimizer.cs:255-268) and ReplaceUses reaches IRArrayStore.Value (:116-119), so a constant `i`
    // folds the cast away and every optimized row goes green WITHOUT ever emitting a cast into a store —
    // the shape §9 mandates and the C# backend could not render (Step 3b). A same-file bare `Sub`
    // called from `Main` works on all three backends (the cross-FILE call is the broken one); if a
    // backend refuses it, use a `For i As Integer = 2 To 2` loop variable and say so in the report.
    // VERIFY, do not assume: the optimized C# text must still contain a cast of `i` inside the store.
    // ⛔ AS BUILT: the helper is `SumFrom`, not `Run` — `Run` is a BasicLang BUILTIN
    // (`Run(exePath As String, arguments As String) As Integer`) and the analyzer refused `Run(2)`
    // ("expects 2 argument(s), got 1") on all three routes before any backend ran. The same-file
    // bare-Sub call then worked on all three backends; no loop-variable fallback was needed.
    private const string SumDouble = "Sub Main()\nSumFrom(2)\nEnd Sub\nSub SumFrom(i As Integer)\nDim a() As Double = New Double() {1, i}\nDim total As Double = 0\nFor Each x As Double In a\ntotal = total + x\nNext\nConsole.WriteLine(total)\nEnd Sub";

    [TestCase(SumTyped, "SUM 6"), TestCase(SumBare, "SUM 6"), TestCase(SumDouble, "3")]
    public void JavaScript_RunsTheLiteral(string source, string expected)
    {
        RequireNode();
        // ⛔ BOTH routes. `RunJs` compiles through `JsTestSupport.Compile`, which runs NO optimizer pass,
        // while every shipping route runs `AddStandardPasses()` unconditionally (JsTestSupport.cs:
        // 101-118 says so in its own doc); the new `Expr` arm's `Bound()` branch depends on the alloc
        // still sitting in block.Instructions after those passes, which only the optimized run shows
        // (CLAUDE.md: never the non-optimizing helper alone).
        Assert.That(JavaScriptExecutionTests.RunJs(source), Is.EqualTo(expected), "non-optimized");
        Assert.That(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(source)).Trim(),
            Is.EqualTo(expected), "optimized — the IR a user gets");
    }

    [TestCase(SumTyped, "SUM 6"), TestCase(SumDouble, "3")]
    public void CSharp_RunsTheLiteral(string source, string expected)
    {
        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(source);   // the real CLI, optimizer on
        Assert.That(VisualGameStudio.Tests.Native.CSharpRun.CompileAndRun(csharp).Trim(), Is.EqualTo(expected));
    }

    [TestCase(SumTyped, "SUM 6"), TestCase(SumDouble, "3")]
    public void Cpp_RunsTheLiteral(string source, string expected)
    {
        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source)).Trim(), Is.EqualTo(expected));
    }
}
```

⚠ `WinFormsCatalogSweepTests.CompileToCSharp` writes a `SweepForm.bas` and compiles it; its output file is `SweepForm.cs` — fine for a Module-less `Sub Main`; if the CLI needs a class, wrap in `Public Class Prog … Public Shared Sub Main()` as the JS scratch measurement did. ⚠ `JavaScriptExecutionTests.RunJs` is `internal static` and `Assert.Ignore`s without node — `RequireNode()` runs first so this fixture fails instead. ⚠ `JsTestSupport.CompileOptimized` is at `JsTestSupport.cs:119-129` and `RunNodeScript` beside `RunJs` (`JavaScriptExecutionTests.cs:26-33`) — check their accessibility and whether `RunNodeScript` trims before relying on the `.Trim()`. ⚠ `BclE2E.CompileRun` ignores without a C++ compiler; this box has MSVC.

- [x] **Step 2: Run** the JS rows → `NotSupportedException … IRArrayAlloc` (the arm that has never run). ⚠ After the two array arms land they STILL throw — `IRAlloca (as an expression)` — because of the THIRD arm Step 3 names; that exception fires before any JS text exists, so at this point a `NotYet` is a missing arm, never a codegen defect to read the output for. The C# `SumTyped` row passes already; the C# `SumDouble` row FAILS TO COMPILE with CS0103 (an undeclared `t2` — the cast temp rendered by name) — that is Step 3b's backend defect, NOT Task 3's coercion; read the csc error and name it in the report. The C++ `SumTyped` row passes today (`CppCollectionTests.cs:461-475` runs the same bare-literal shape); the C++ Double row is why `SumDouble` prints the number alone (its note above) — a C++ build failure on it means the concat crept back, not that the IR is wrong.

- [x] **Step 3: Implement** in `JavaScriptBackend.cs`. Replace the two `NotYet` visitors (`:2379-2380`):

```csharp
        // Task 24a. An array literal is an allocation of N slots followed by N index stores; both
        // arms are required (the renderer rebuilds operand trees). ⛔ The expression arm returns the
        // BOUND name when the alloc already appeared in block.Instructions — the M4 shape passes the
        // temp as a call argument AFTER its stores, and re-rendering it inline would allocate a
        // second, empty array.
        public void Visit(IRArrayAlloc arrayAlloc) => Bind(arrayAlloc, ArrayAlloc(arrayAlloc));

        public void Visit(IRArrayStore arrayStore) =>
            Line($"{Expr(arrayStore.Array)}[{Expr(arrayStore.Index)}] = {Expr(arrayStore.Value)};");

        private static string ArrayAlloc(IRArrayAlloc a) => $"new Array({a.Size})";
```

and in the `Expr` switch before `default:`:

```csharp
                case IRArrayAlloc alloc:
                    return Bound(alloc) ? SanitizeName(alloc.Name) : ArrayAlloc(alloc);
```

**AS BUILT (deviates from plan:557-560).** The plan prescribes `case IRArrayAlloc alloc: return Bound(alloc) ? SanitizeName(alloc.Name) : ArrayAlloc(alloc);`. As built, the unbound branch is split in two: `Size == 0` still returns `ArrayAlloc(alloc)`, and `Size > 0` throws `NotYet("IRArrayAlloc with unemitted element stores …")`. Reason: an unbound alloc means `_suppressEmit` (a `When` guard) swallowed the alloc *and* its element stores together, so `new Array(Size)` renders a sparse array of holes — measured, `Case Is > 0 When Total(New Integer() {1, 2}) = 3` built clean and printed the `Case Else` arm. `Size == 0` is excluded because `New Integer() {}` has no stores to lose; measured, it compiles and prints the correct arm today, and refusing it would regress a working shape. `IRArrayAlloc` is constructed in exactly one place (`IRBuilder.cs:1926`) with `Size == elements.Count`, so `Size > 0` is the exact condition for "stores were suppressed".

**The THIRD arm** (plan review pass 3, verified against the code). An array-typed local WITH an initializer lowers to `IRAlloca a_addr` + `IRStore(value, a_addr)` + `IRAssignment(a, value)` (`IRBuilder.cs:643-687`, `needsMemory = varType.Kind == TypeKind.Array`) — for the typed literal and the bare `{1, 2, 3}` alike (no JS fixture has ever compiled either shape). `Visit(IRStore)` (`:2193-2197`) renders `Expr(store.Address)`; `IRAlloca` derives from `IRValue`, not `IRVariable` (`IRNodes.cs:347`), and `Expr` has no arm for it, so the store throws `NotYet("IRAlloca (as an expression)")`. Replace `Visit(IRStore)` with:

```csharp
        public void Visit(IRStore store)
        {
            // Task 24a. An array-typed local with an initializer lowers to IRAlloca `a_addr` +
            // IRStore(value, a_addr) + IRAssignment(a, value) (IRBuilder.cs:643-687). The alloca is a
            // memory-model artefact this backend has no counterpart for (`Visit(IRAlloca)` is already a
            // no-op), and the IRAssignment that follows ALWAYS carries the value: TryRenameToVariable
            // (IRBuilder.cs:237-254) renames only IRCall/IRAwait/IRBinaryOp/IRUnaryOp/IRCompare, never
            // an IRArrayAlloc. Rendering the address threw NotYet("IRAlloca (as an expression)") before
            // any JS existed. The C# backend renders the alloca as its variable (CSharpBackend.cs:
            // 2994-3005) and lives with a duplicate assignment; skipping the store is the choice that
            // does not depend on WHERE this backend declares the local.
            if (store.Address is IRAlloca) return;

            Line($"{Expr(store.Address)} = {Expr(store.Value)};");
        }
```

⚠ `Bind(IRValue, string)` exists at `:1513`; it declares a `const` when the name is not a declared local, or assigns when it is (`:1516-1522`). `TryRenameToVariable` never renames an `IRArrayAlloc`, so `a` always arrives through the `IRAssignment` and `Bind` always declares `const t1` — that is the one case that occurs. If a JS row still fails AFTER all three arms, print the generated JS (`JsTestSupport.Compile(source)` AND `CompileOptimized`) and read it before touching anything else; a `NotYet` at that point names a FOURTH arm — report it, do not guess at it.

- [x] **Step 3b — the C# backend renders a store's value by NAME (Task 3's spec review).** `CSharpBackend.Visit(IRArrayStore)` (`:3549-3555`) renders the stored value with `GetValueName`, not `EmitExpression` — the route `Visit(IRAssignment)` (`:3135`), `Visit(IRStore)` (`:3153`) and `Visit(IRReturn)` (`:3338`) all take. `Visit(IRCast)` (`:3426-3429`) emits nothing for a non-named temp and `_declaredIdentifiers` holds only params/locals/globals (`:1441-1452`), so an `IRCast` in a store comes out as `t1[1] = t2;` with `t2` never declared — CS0103. Latent today for any COMPUTED untyped element (`{i + 1}`; no test exercises one — grep found none), reachable now for the spec's own gate row, and INVISIBLE through the optimizer when the element is a constant (the cast folds). Fix: `EmitExpression(arrayStore.Value)` in `Visit(IRArrayStore)`, and the same pattern at `Visit(IRIndexerStore)` (`:3564`). RED: `CSharp_RunsTheLiteral(SumDouble)` fails to compile with CS0103 (the parameter `i` keeps the cast alive). GREEN after. Add `CSharp_Emission_CastsTheStoredNonLiteral` (non-Integration is fine): the text from `WinFormsCatalogSweepTests.CompileToCSharp(SumDouble)` contains a cast of `i` inside the array store — state the EXACT rendering you measured (e.g. `(double)i`), and assert that. ⚠ The JS arms above already render `Expr(arrayStore.Value)` inline — the same choice; do not "simplify" them to a name lookup. ⛔⛔ **AS BUILT addendum (Task 4's spec review): the `EmitExpression` store ALONE turns a loud break into a silent DOUBLE CALL.** `CSharpBackend.GetOperands` (~`:3023-3081`) had no `IRArrayStore` arm, so `_useCounts` never counted an array-literal element: a call element had use-count 0, `Visit(IRCall)` emitted it BARE (`Foo();`), and the new inline store rendered it AGAIN — `Foo(); t1[0] = Foo();`, green build, `Foo` runs twice (typed or untyped literal; before the change it was CS0103). JS is unaffected (`IROperandWalker.cs:121-124` counts `IRArrayStore`). Fix in the same commit: `case IRArrayStore ast: return new[] { ast.Array, ast.Index, ast.Value };` in `GetOperands`, pinned by a three-backend run row whose elements are two `Bump()` calls (`N 2`, not `N 4`). Two more rows added: `SumViaCall` — the M4 shape `Show(New Integer() {1, 2})` RUN on JS (both routes), C# and C++, since the `Expr` arm's bound-name branch exists for exactly that shape and it had only ever been PARSED — and `IndexerStoreCast` (`l(0) = i`, a cast into an `IRIndexerStore`, C#).

- [x] **Step 4: Run** the fixture → green on all three backends. Run `--filter "FullyQualifiedName~JavaScriptExecutionTests|FullyQualifiedName~JavaScriptArrayTests"` → still green.

### Task 5: The M4 / M3 / two-file rows through the real CLI and csc

**Files:** `TypedArrayLiteralExecutionTests.cs`.

- [x] **Step 1: Write the failing tests**

- `WinForms_TheVsIdiom_BuildsUnderCsc_AndRuns`: the M4 program, INLINE in the fixture (never a scratchpad path — a fresh session cannot find it):

```basic
Using System
Using System.Windows.Forms
Using System.Drawing

Public Class MenuForm
    Inherits Form

    Private menuStrip1 As MenuStrip
    Private mnuFile As ToolStripMenuItem
    Private sep As ToolStripSeparator

    Public Sub New()
        menuStrip1 = New MenuStrip()
        mnuFile = New ToolStripMenuItem()
        mnuFile.Text = "File"
        sep = New ToolStripSeparator()
        menuStrip1.Items.AddRange(New ToolStripItem() {mnuFile, sep})
        Me.MainMenuStrip = menuStrip1
        Me.Controls.Add(menuStrip1)
    End Sub

    Public Sub Poke()
        Console.WriteLine("ITEMS " & menuStrip1.Items.Count)
    End Sub
End Class
```

  → `WinFormsCatalogSweepTests.CompileToCSharp(source)` (assert the C# contains `new ToolStripItem[2]`) → `WinFormsCompile.AssertCompiles(csharp, …)`; then a dotnet-build + run exactly as `FormComponentAcceptanceTests.cs:146-204` does (copy its csproj/driver text; `new MenuForm()`, `form.Poke()`), asserting the run prints `ITEMS 2`.
- `WinForms_TheDeclaredArrayShape_BuildsUnderCsc`: the same class with the constructor's `AddRange` line replaced by the TYPED M3 shape, two lines: `Dim items() As ToolStripItem = New ToolStripItem() {mnuFile, sep}` then `menuStrip1.Items.AddRange(items)` (⚠ the scratch `M3n` file holds the BARE literal, which is the refused shape — do not copy it) → `CompileToCSharp` + `WinFormsCompile.AssertCompiles`. (`CreateArrayType` names arrays `T[]`, `SymbolTable.cs:657/:677`, so the declared type and the literal's `Equals` — there is no `()` spelling to chase.)
- `TwoFiles_ASiblingClass_IsNotExemptedAsANetType`: ⛔ the single-file CLI REFUSES two source files before parsing (`Program.cs:194-202`, exit 2 on stderr), and the build route prints semantic errors to STDERR (`:870-877`, exit 1 at `:1180`). So: write `Shape.bas` (`Public Class Shape\nEnd Class`), `Main.bas` (`Public Class Prog\nPublic Shared Sub Main()\nDim s As String = "a"\nDim all() As Shape = New Shape() {s}\nEnd Sub\nEnd Class`) and `App.blproj` copied from `CliTestHarness.cs:126-138` (`CompileRunCSharp`'s template) with TWO `<Compile Include>` items and `<TargetBackend>CSharp</TargetBackend>`; run `CliTestHarness.RunProcess(CliTestHarness.CliPath(), new[] { "build", "App.blproj" }, dir, 120_000)` → `ExitCode != 0` AND `StdErr` contains `cannot put a 'String' in a 'Shape()'` (both halves, or mutant (c) below is not discriminated — the exit code alone is also what a multi-file refusal gives).

- [x] **Step 2: Run** → the first two fail today only if Tasks 1–3 are incomplete; the two-file row fails if the predicate was spelled without `IsUserDefinedTypeName`. All three must be green at the end.

- [x] **Step 3: Mutants (kill each, revert each — (a)–(d) here; (e)–(h) are run at Task 4 Step 5 by its implementer, whose results go into the commit message):** (a) parser: drop the `Arguments.Count > 0` refusal → `TheThreeRefusals` fails; (b) analyzer: replace `WidensTo` with `target.IsAssignableFrom` → `RefusesNarrowing` fails; (c) analyzer: drop `!IsUserDefinedTypeName` from the predicate → the two-file row fails; (d) IR: drop the coercion → the lowering test fails; (e) JS: make the `Expr` arm always render `new Array(n)` → the C++/C# rows stay green and `JavaScript_RunsTheLiteral(SumTyped)` prints anything but `SUM 6` (MEASURED at Task 4: the Integer rows print `SUM 0` — the Integer `For Each` skips the empty array's holes — and the Double row prints `NaN`; killed either way) — this is the "second empty array" trap; (f) JS: remove the `IRArrayStore` arm → `SumBare` throws; (g) JS: remove the `store.Address is IRAlloca` guard → all three JS rows throw `NotYet … IRAlloca (as an expression)`; (h) C#: revert `Visit(IRArrayStore)` to `GetValueName` → `CSharp_RunsTheLiteral(SumDouble)` fails with CS0103 AND `CSharp_Emission_CastsTheStoredNonLiteral` fails — if only one of the two fails, the other is not measuring the store.

### Task 6: Pretty printer, gate, commit 24a

- [ ] **Step 1:** `ASTPrettyPrinter.Visit(CollectionInitializerNode)` (`:827`): when `node.ElementType != null`, write `CollectionInitializer New {ElementType.Name}() ({n} elements):`. Pin with one test in `TypedArrayLiteralTests` if a pretty-printer test fixture exists (grep `ASTPrettyPrinter` in Tests); otherwise leave it to the untyped format and skip.

  **SKIPPED (2026-09-20).** A repo-wide grep for `ASTPrettyPrinter` across `VisualGameStudio.Tests` returns nothing — no pretty-printer fixture exists to pin it, and this step's own text permits skipping in exactly that case. Shipping an unpinned production change is exactly the "a thing with no caller" failure this project keeps hitting, so the change was not made.
- [ ] **Step 1b: Open spec §10 "What building it changed"** with the Task 2 row: `WidensTo` is integral←integral by numeric range — the spec's "IsAssignableFrom minus its permissive arm" spelling would refuse Byte→Integer (only ever admitted BY the arm, `SymbolTable.cs:163-196`) and so contradicts its own word "widening"; the implementation follows the word, and the spec's example rows all hold. Also the exemption predicate's `Kind is Class or Delegate` guard (a nested typed literal's `Integer[]` passes `IsNetType`). 24c's Task 19 APPENDS the layout-entry row to this section.
- [ ] **Step 2: Gate.** Build; fast subset; `TypedArrayLiteralExecutionTests`; then the FULL SUITE (`dotnet test … -c Release --no-build --logger "console;verbosity=normal" > $env:TEMP\bl-t24a-full.log 2>&1`, both streams), compare failure NAMES with the 8-row baseline (2 `SearchSnippets`, `Cli_Build_CppProject_ProjectReference_WarnsAndStillSucceeds`, 4 game-template rows, `NonEx_variants_marshal_and_are_screen_size_dependent`). Zero new, or stop.
- [ ] **Step 3: Commit** `feat(compiler): New T() { … } array creation with initializer` — message via a scratch file: the measured facts (M1–M4 and the JS "successful, no output" measurement), the three policies, the JS arms, the gates with totals, the trailer.

---

# Commit 24b — the shape, and every gate learns it (spec §1, §2 "Gates" row, Decision 12)

### Task 7: `FormPlace`, `FormItemRule`, and the derived `IsComponent`

**Files:** Modify `BasicLang/Forms/FormControlCatalog.cs` (`FormControlDef` `:405-455`, `FormSchematic` `:274-370`); Test: `VisualGameStudio.Tests/Compiler/FormCatalogCoverageTests.cs`.

- [ ] **Step 1: Failing tests** (append to `FormCatalogCoverageTests`):
  - `EveryRow_HasAPlace_AndIsComponentIsDerived`: for every row, `def.IsComponent == (def.Place == FormPlace.Tray)`; the four tray kinds are `Tray`; every other row today is `Positioned` (⚠ this half is REWRITTEN in Task 12 Step 1 to "every row that is not a strip or item is Positioned" when the seven rows land — do not leave the 24b wording in place).
  - `FormSchematic_HasTheSevenStripValues`: `Enum.GetValues<FormSchematic>()` contains `MenuBar, ToolBar, StatusBar, MenuItem, Separator, ToolButton, StatusLabel` and no row uses them yet (`FormControlCatalog.All.All(d => !new[]{…}.Contains(d.Schematic))`) — ⚠ the "no row uses them yet" half is INVERTED in Task 12 Step 1 to "exactly the seven rows use exactly these seven, one each".
- [ ] **Step 2: Run** → compile errors (`Place`, the enum members).
- [ ] **Step 3: Implement.** Above `FormWebScript`:

```csharp
/// <summary>
/// The SHAPE of a catalog row — what a control of this kind is, on the canvas and in the code (Task 24,
/// spec §1). One enum rather than a third bool: every site that once asked <c>IsComponent</c> must
/// decide what a Docked strip and an Item are, and a switch without a default is what forces it.
/// </summary>
public enum FormPlace
{
    /// <summary>Pixels or a cell, a TabIndex, children if <c>IsContainer</c>; added with <c>Controls.Add</c>, reversed.</summary>
    Positioned,
    /// <summary>The tray (Task 25): no geometry, no TabIndex, no children, in <c>FormDocument.Components</c>.</summary>
    Tray,
    /// <summary>A strip docked to the form: no geometry, a <c>Dock</c> PROPERTY, items under its rule, in <c>Controls</c>.</summary>
    Docked,
    /// <summary>A strip item: no geometry, no TabIndex, in its host's <c>Children</c>, added by the HOST row's verb in document order.</summary>
    Item
}

/// <summary>
/// What a host row holds and how it adds one (spec §1). <paramref name="Kinds"/>[0] is the default kind
/// Type Here creates; <paramref name="Add"/> is a template with <c>{parent}</c> and <c>{child}</c>,
/// emitted in DOCUMENT order (items are ordered, not layered). On the PARENT's row, because the same
/// ToolStripMenuItem is <c>Items.Add</c>ed under a strip and <c>DropDownItems.Add</c>ed under a menu item.
/// </summary>
public sealed record FormItemRule(IReadOnlyList<string> Kinds, string Add)
{
    public bool Accepts(string kind) => Kinds.Any(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase));
}
```

Then on `FormControlDef`: remove the positional `bool IsComponent = false` parameter (all four rows pass it BY NAME — `:757/:779/:789/:797` — so change those four to `Place: FormPlace.Tray`; ⚠ also delete or re-home the orphaned `<param name="IsComponent">` doc comment at `:398-402`, or CS1572 warns on every build), add after `WebScript`:

```csharp
    FormPlace Place = FormPlace.Positioned,
    FormItemRule? Items = null,
    string? FormProperty = null,
    string? HtmlChildrenWrapper = null,
    string? HtmlRole = null,
    string? WebCss = null)
{
    /// <summary>Task 25's flag, now derived: the eleven sites that read it keep reading it.</summary>
    public bool IsComponent => Place == FormPlace.Tray;

    /// <summary>A strip or a menu item — something whose children are items.</summary>
    public bool IsHost => Items != null;
```

with `<param>` docs for each (spec §2 rows: `FormProperty` = "MainMenuStrip"; `HtmlChildrenWrapper` = "ul"; `HtmlRole` = "toolbar"/"status"/"separator"; `WebCss` = the per-kind stylesheet block appended once per kind present). Add the seven `FormSchematic` members with one-line docs after `Worker`.

- [ ] **Step 4: Run** `FormCatalogCoverageTests`, `FormToolboxGlyphTests`, `FormPropertyGridTests` → green (nothing uses the new members yet). Build the Shell → green.

### Task 8: `FormCatalogShapes` — the one canonical-shape helper, and the three catalog gates move onto it

**Files:** Create `VisualGameStudio.Tests/Compiler/FormCatalogShapes.cs`; Modify `WinFormsCatalogSweepTests.cs` (`:139-173`, `:182-218`, `:235-273`), `FormCanvasRenderTests.cs` (`DocumentWith` `:39-57`, `:75-102`), `FormRetargetTests.cs` (the sweep, `~:957-1010`).

- [ ] **Step 1: Write the helper** (test-support, not product):

```csharp
namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The ONE answer to "how does a catalog row appear in a document" for every catalog-driven gate
/// (spec §2, Gates row). Three gates each chose their fixture by IsComponent alone; a fifth shape
/// would have been forgotten by all three again.
/// </summary>
internal static class FormCatalogShapes
{
    /// <summary>Adds <paramref name="definition"/>'s canonical shape to <paramref name="document"/> and returns the control it added.</summary>
    public static FormControl Canonical(FormDocument document, FormControlDef definition, string id, string? hostId = null,
        FormGeometry? geometry = null)
    {
        switch (definition.Place)
        {
            case FormPlace.Tray:
            {
                var c = new FormControl { Kind = definition.Kind, Id = id };
                document.Components.Add(c);
                return c;
            }
            case FormPlace.Docked:
            {
                var c = new FormControl { Kind = definition.Kind, Id = id };
                var dock = definition.Property("Dock");
                if (dock?.Default != null) c.Properties["Dock"] = dock.Default;
                document.Controls.Add(c);
                return c;
            }
            case FormPlace.Item:
            {
                // The first DOCKED host row listing it — deterministic, never a menu item hosting a menu item.
                var hostDef = FormControlCatalog.All.First(d => d.Place == FormPlace.Docked && d.Items?.Accepts(definition.Kind) == true);
                var host = Canonical(document, hostDef, hostId ?? id + "Host");
                var c = new FormControl { Kind = definition.Kind, Id = id };
                if (definition.Property("Text") != null) c.Properties["Text"] = id;
                host.Children.Add(c);
                return c;
            }
            default:
            {
                var c = new FormControl
                {
                    Kind = definition.Kind, Id = id, TabIndex = 0,
                    Geometry = geometry ?? (document.Target == FormTarget.Web
                        ? new GridGeometry { Col = 0, Row = 0 }
                        : new PixelGeometry { X = 96, Y = 80, Width = 120, Height = 24, Anchor = "Top" })
                };
                document.Controls.Add(c);
                return c;
            }
        }
    }

    /// <summary>Finds the canonical control of <paramref name="definition"/> again — on a crossed retarget, say.</summary>
    public static FormControl? Locate(FormDocument document, FormControlDef definition) =>
        document.AllControls().Concat(document.AllComponents()).FirstOrDefault(c => c.Kind == definition.Kind);
}
```

⚠ Keep the property sweep's `Anchor = "Top"` in the default geometry (spec Decision 12 note). ⚠ For a `.blwebform` document the Positioned shape needs a `GridGeometry`: when `geometry == null`, use `document.Target == FormTarget.Web ? new GridGeometry { Col = 0, Row = 0 } : new PixelGeometry { … }` (the parameter is `FormGeometry?` for exactly this). ⚠ Moving the retarget sweep onto `Canonical` gives its Positioned controls geometry they never had; that adds BL8025 `RetargetLayoutCrossed` warnings, never `RetargetPropertyLost`, so the sweep's property assertions stand.

- [ ] **Step 2: Move the gates onto it**, one at a time, running each after:
  - `WinFormsCatalogSweepTests.EveryProperty_OfEveryControl_…` (`:143-162`): replace the hand-built control with `var control = FormCatalogShapes.Canonical(form, definition, "ctl");` then the property loop. Same in `TheDefaultEvent_OfEveryControl_…` (`:237` onward — the enum sweep sits at `:184-218` in between). The enum sweep: `Canonical(form, definition, $"ctl{i}", hostId: $"ctl{i}Host")` per value.
  - `FormCanvasRenderTests.DocumentWith(kind)` (⚠ this fixture is in namespace `VisualGameStudio.Tests.Shell` and the helper in `VisualGameStudio.Tests.Compiler` — add `using VisualGameStudio.Tests.Compiler;`): `FormCatalogShapes.Canonical(document, FormControlCatalog.Find(kind)!, SharedId, hostId: SharedId, geometry: new PixelGeometry { X = 20, Y = 20, Width = 140, Height = 40 })`; `EveryControlKindRendersDistinctly` filter becomes `d.SupportsTarget(WinForms) && d.Place != FormPlace.Tray && d.Place != FormPlace.Item` (items leave the hash — spec §7).
  - `FormRetargetTests.EveryCatalogKind_Retargets_…`: build with `Canonical(source, definition, "c")`, locate the crossed control with `FormCatalogShapes.Locate(result.Document, definition)`, and scope `reportedLost` to messages containing `'c.`, i.e. `.Where(m => m.Contains("'c."))` before the `Single` lookup.
- [ ] **Step 3: Run** the three fixtures (`WinFormsCatalogSweepTests` is Integration: run it explicitly) → green with today's rows (no behaviour change yet).

### Task 9: The toolbox membership pins and the structural-name pin learn `Place`

**Files:** Modify `FormPropertyGridTests.cs:552-568`, `FormToolboxGlyphTests.cs:75-89`, `FormDocumentTests.cs:80-92`, `FormToolboxViewModel.cs:53-56`.

- [ ] **Step 1:** In both toolbox pins, the expected set becomes `FormControlCatalog.For(target).Where(c => c.Place != FormPlace.Item)`; in `FormToolboxViewModel.Rebuild`, `.Where(c => c.Place != FormPlace.Item)` before the `OrderBy`, with a comment: items are created from Type Here (spec Decision 7). In `Catalog_NoPropertyCollidesWithAStructuralAttribute`, keep iterating EVERY row and vary the forbidden-name set by `Place` instead of skipping non-`Positioned` rows: `FormControlCatalog.IsStructural` treats `Id`/`TabIndex` as structural for every target before it ever consults the layout vocabulary, and the reader's attribute skip applies to any non-component control regardless of `Place` — so a Docked or Item row's own property named `Id` is silently dropped by the reader exactly as a Positioned row's would be, and even a Tray row is not exempt (its skip clause names `Id` specifically). Say so in a comment; a Docked row's `Dock` is a catalog PROPERTY, which is a separate fact and not a reason to exempt the Place from this gate.
- [ ] **Step 2: Run** the three fixtures → green (no rows changed).

### Task 10: The `DrawSchematic` seam and the enum-driven pins

**Files:** Modify `FormCanvasControl.cs` `DrawControl` (`:1420-1890`), `FormToolboxViewModel.GlyphFor` (`:93-134`); Create `VisualGameStudio.Tests/Shell/FormSchematicPinTests.cs`.

- [ ] **Step 1: Failing tests**

```csharp
[TestFixture]
public class FormSchematicPinTests
{
    [Test]
    public void EverySchematic_HasItsOwnGlyph()
    {
        var marks = Enum.GetValues<FormSchematic>().ToDictionary(s => s, FormToolboxViewModel.GlyphFor);
        Assert.That(marks.Values, Has.None.EqualTo("?"), "a schematic fell through to the fallback mark");
        Assert.That(marks.Values.Distinct().Count(), Is.EqualTo(marks.Count), "two schematics wear one mark");
    }

    [AvaloniaTest]
    public void EverySchematic_PaintsDifferently()
    {
        // Through the seam, at one bounds, with one label — so only the SHAPE can differ.
        var hashes = new Dictionary<string, List<FormSchematic>>();
        foreach (var schematic in Enum.GetValues<FormSchematic>())
        {
            var hash = FormCanvasControl.RenderSchematicForTest(schematic, new Rect(20, 20, 140, 40), "X");
            (hashes.TryGetValue(hash, out var l) ? l : hashes[hash] = new()).Add(schematic);
        }
        var collisions = hashes.Values.Where(l => l.Count > 1).ToList();
        Assert.That(collisions, Is.Empty, "identical pixels: " + string.Join(" | ", collisions.Select(c => string.Join(",", c))));
    }
}
```

⚠ The fixture lives in `VisualGameStudio.Tests.Shell` and needs `using VisualGameStudio.Shell.ViewModels.Designer;` (`GlyphFor`), `using VisualGameStudio.Shell.Controls;` (`FormCanvasControl`), `using Avalonia;` (`Rect`) and `using Avalonia.Headless.NUnit;`. `RenderSchematicForTest` is a `public static` helper on `FormCanvasControl` that creates a headless `Window` hosting a `FormCanvasControl` with a `SchematicOverride`, renders, and hashes the frame (reuse `FormCanvasRenderTests.RenderHash`'s body). ⛔ PUBLIC, and `FormToolboxViewModel.GlyphFor` becomes `public static` too: the Shell grants NO `InternalsVisibleTo` to the test project, by convention (the only IVT in the repo is BasicLang's; `CodeEditorDocumentView.axaml.cs:770` records the "public seams, never internal+IVT" rule, and no test calls `GlyphFor` today — the glyph gates read the public `FormToolboxItem.Glyph`).

- [ ] **Step 2: Run** → the glyph test fails (seven `?`), the paint test fails (seven schematics fall to the default arm and hash alike).
- [ ] **Step 3: Implement.** Extract from `DrawControl` everything from `var labelOrigin = …` (`FormCanvasControl.cs:1443`) THROUGH the post-switch label draw (`:1881-1889`) into `private void DrawSchematic(DrawingContext context, FormSchematic schematic, Rect bounds, string label, IBrush face, IBrush client, IBrush ink)` — the `switch (schematic)` verbatim, AND the label draw after it — and have `DrawControl` call it. ⛔ The seam OWNS the post-switch label draw, because `labelOrigin` is a local declared before the switch and MUTATED by arms (Button centres it at `:1459-1461`, Check/Radio move it past the glyph at `:1490`) and the post-switch draw consumes the mutated value; leaving that draw in `DrawControl` would paint every Button caption top-left and every CheckBox caption over its tick, from a green suite. The three band arms `return` before the label draw — a band draws NO caption (spec §6; a strip's id as a caption would be the only pixel a render gate sees). Add the seven arms with distinct shapes: `MenuBar` (a flat band with a 1px bottom rule), `ToolBar` (a band with a left grip of two vertical lines), `StatusBar` (a band with a 1px top rule and a sizing-grip triangle bottom-right), `MenuItem` (a `client`-filled band behind the caption, no border, caption inset 8px — ⚠ NOT "the caption with a 2px pad, no box": that is the existing `Text` arm but for a 2px shift, and the pairwise pin refuses it), `Separator` (a 1px vertical line, or horizontal when `bounds.Width > bounds.Height`), `ToolButton` (a small raised box with the caption), `StatusLabel` (the caption vertically CENTRED, with a 1px `ink` rule down the left edge — the status-panel divider; ⚠ "caption at left" alone is pixel-identical to the `Text` arm, which draws no box and paints its caption at `labelOrigin = (X+4, Y+2)`, `FormCanvasControl.cs:1443/:1448-1451`, so the pin fails on `Text,StatusLabel` by construction). ⛔ Bands draw NO caption (spec §6). Add the seven `GlyphFor` arms: `MenuBar => "≡_"`, `ToolBar => "[▸]"`, `StatusBar => "_≡"`, `MenuItem => "≡"`, `Separator => "—"`, `ToolButton => "[▸"`, `StatusLabel => "_A"` — distinct from every existing mark (run the glyph test) — and make `GlyphFor` `public static`. Add `public static string RenderSchematicForTest(FormSchematic, Rect, string)` (PUBLIC — see Step 1) and a `SchematicOverride` used only when non-null.
- [x] **Step 4: Run** `FormSchematicPinTests`, `FormCanvasRenderTests`, `FormToolboxGlyphTests` → green.

⚠⚠ **AS BUILT (Task 10, 2026-09-21) — four deviations from the code above, each measured.**

1. ⛔ **ELEVEN arms were needed, not seven.** Step 2 predicts "seven schematics fall to the default arm
   and hash alike". It is ELEVEN: `Clock`, `Hint`, `Alert` and `Worker` (Task 25's component glyphs)
   have NEVER had a `DrawControl` arm either and painted pixel-for-pixel what `Input` paints, so the
   enum-driven pin collides on `Input,Clock,Hint,Alert,Worker`. Arms were added for all four rather
   than narrowing a pin the test-writer may not edit. ⚠ **They are UNREACHABLE from the live canvas**
   — a component has no geometry, `AllControls()` excludes the tray, so `Layout` never yields bounds
   for one — i.e. production code with no shipping caller, which this repo has shipped five times and
   documents as its top failure mode. Accepted here because the alternative is an enum-driven gate
   with a hand-maintained exclusion list, which is the second failure mode (a new value silently
   exempted). **24c should decide whether the tray surface draws through this seam**, which would make
   them reachable and settle it; the `FormSchematic` banner still says these values name the glyph only.
2. ⛔ **`RenderSchematicForTest` cannot reuse `FormCanvasRenderTests.RenderHash`'s body.** That helper
   uses `CaptureRenderedFrame` from **`Avalonia.Headless`, which `VisualGameStudio.Shell` does not
   reference** — and referencing it would ship a test platform inside the IDE. Built on
   `RenderTargetBitmap` (base Avalonia) instead: `Measure`/`Arrange`, `bitmap.Render(control)`, SHA256
   of the PNG bytes. Same control, same `Render` override, same seam, same Skia backend. 33 values
   produced 33 distinct hashes, which is what proves the frames are genuinely rasterised — a blank
   frame would have collided all of them.
3. **`SchematicOverride` is a private field plus a private `SchematicProbe` record**, not a public
   property. The test reaches it only through `RenderSchematicForTest`, so exposing it would widen the
   public surface past the two seams this task requires public.
4. ⛔⛔ **`DrawSchematic` RETURNS `Point?` — the caption origin it used — and that is beyond this plan.**
   Mutation testing found the gap: **every gate in 24b is a DISTINCTNESS gate, so a caption that MOVES
   is invisible.** Deleting the Button arm's centring left the pairwise pin, the label-invariance pin
   and the caption-presence pin all green. That matters because this task's own text calls three new
   offsets load-bearing (MenuItem's 8px inset, ToolButton's `box.X+4,box.Y+3`, StatusLabel's vertical
   centring). Golden hashes were refused — they break on any Avalonia/Skia/font bump — so the seam
   reports the ARITHMETIC instead: `RenderSchematicForTest` returns
   `SchematicFrame(string Hash, Point? CaptionOrigin)`. ⚠ `CaptionOrigin` is null in THREE cases, not
   one: the three bands, an EMPTY label, and bounds too small for text. ⛔ A test-only write-back onto
   the probe record was REJECTED — it would put a write in the per-control render path, record only the
   last control drawn on a real document, and require making an immutable record mutable. A pure
   `CaptionOriginFor(...)` helper was also rejected: it would be a MIRRORED copy of every offset that
   drifts while agreeing with its own test, and it is impossible anyway because Button and StatusLabel
   centre against measured caption metrics. ⚠ Changing the return type left FOUR of five call sites
   still COMPILING with silently changed semantics (`Is.EqualTo` fell through to record equality,
   comparing `(Hash, CaptionOrigin)` pairs) — every site now says `.Hash` explicitly.

### Task 11: Gate and commit 24b

- [x] Build (the Shell: `dotnet clean` not needed — no AXAML changed). Fast subset; `WinFormsCatalogSweepTests`; `FormCanvasRenderTests`; `FormSchematicPinTests`. Mutants: (a) remove one `GlyphFor` arm → the glyph pin; (b) make `Separator` draw like `MenuItem` → the paint pin; (c) `Canonical`'s Item branch pick `All.First(d => d.Items?.Accepts(…))` (any host) → no test yet (rows absent) — record that it is pinned in 24c's coverage test.
- [x] Commit `feat(designer): Task 24b — the row SHAPE (Place, FormItemRule) and every gate learns it before a strip exists`.

⚠⚠ **AS BUILT (Task 11, 2026-09-21) — the gate run, and SEVEN mutants not three.**

**Gate: fast subset 5922 / 5919 passed / 2 failed / 1 skipped** — the two failures are the standing
`SearchSnippets` pair (`task_b9620d48`), zero new — **plus EVERY Form Integration fixture, 116/116**,
stderr empty. ⚠ The Integration tier was widened beyond this task's list on purpose: `FormControlDef`
is a record every form consumer reads and its parameter list changed shape, so the blast radius is the
whole designer, not three fixtures. The FULL suite was NOT run (it is ~2h29m and 24b adds no control
row and changes no existing kind's behaviour); say so rather than implying otherwise.

**Mutants (a) and (b) killed as written. (c) SURVIVES and is recorded honestly** — `Canonical`'s Item
branch is unreachable until 24c's rows exist (`FormControlCatalog` has exactly four `Place:` arguments,
all `Tray`, and no `Items:` anywhere), so its pin belongs in 24c's coverage test, as this plan already
schedules. Four more were run because the review demanded them:

| mutant | killed by |
|---|---|
| (d) delete the Button arm's centring | `CaptionOrigin_MetricDependentArms_MoveByExactlyTheBoundsDelta` — the delta fell to 0 |
| MenuItem's 8px inset → `bounds.X + 4` | `CaptionOrigin_ConstantOffsetArms_MatchTheirExactOffset` — expected `(28,22)`, got `(24,22)` |
| a band's `return null` → `return labelOrigin` | `CaptionOrigin_Bands_AreAlwaysNull` AND `EveryBand_IgnoresItsLabel` |
| `Group`'s `break` → `return null` | the REWRITTEN `EveryCaptioningSchematic_ActuallyDrawsItsCaption` |

⛔⛔ **(d) survived EVERY gate in this commit until the caption-origin seam existed** — that is the
whole argument for AS BUILT note 4 above.

⛔ **The original `EveryCaptioningSchematic_ActuallyDrawsItsCaption` was TAUTOLOGICAL for `Group` and
`Link`.** Both arms draw label-SIZED geometry inside their own case (Group's background patch
`caption.Width + 4`; Link's underline to `linkText.Width`), so deleting the shared caption draw still
changed the frame between a short and a long label and the test stayed green — while its own doc
comment claimed it caught exactly that. Rewritten to assert `CaptionOrigin is not null`, which is null
**iff** the shared draw never ran, whatever else the arm measured.

**Two gates the review found narrowed, both fixed here:**
- `FormRetargetTests`' sweep lost its cardinality claim when `destinationList.Single()` became
  `Locate(...)!` (a `FirstOrDefault`): a regression adding the converted control TWICE would have
  passed. Now `Is.Empty` in the unsupported branch and `Count == 1` before reading properties.
  ⛔ Deliberately NOT a total-control-count assertion — an Item with no destination row legitimately
  leaves its Docked host behind, which would red the gate the day 24c lands.
- `FormDocumentTests.Catalog_NoPropertyCollidesWithAStructuralAttribute` had been narrowed to
  `Place == Positioned` **on a rationale this plan itself got wrong** (Task 9 said "the write-twice
  hazard exists only where geometry is written"). `IsStructural` returns true for `Id`/`TabIndex`
  BEFORE consulting any layout vocabulary, and the writer emits `Id` on every element outside the
  component guard, so a Tray row declaring a property named `Id` would lose its identity and be
  dropped on reload. It now iterates EVERY row and varies only the forbidden-name set by `Place`.
  Task 9's wording and spec §2's matching line were corrected in the same commit.
  ⭐ It carries an instrument-proving row (a locally built `Place: Tray` def with a property named
  `Id` must be REJECTED) — the discipline `WinFormsCatalogSweepTests` already uses.

---

# Commit 24c — rows, format, emission, bands, placement (spec §3–§5, §2)

### Task 12: The seven rows, BL8030, coverage

**Files:** `FormControlCatalog.cs` (after the tray rows), `DesignDiagnostic.cs` (band comment `:54-75`, a constant), `FormCatalogCoverageTests.cs`.

- [ ] **Step 1: Failing coverage tests:** first flip the two Task 7 pins: `EveryRow_HasAPlace_AndIsComponentIsDerived` asserts every row whose kind is not one of the seven is `Positioned` (the tray four `Tray`), and `FormSchematic_HasTheSevenStripValues` asserts each of the seven schematics is used by EXACTLY ONE row and that row is one of the seven kinds. Then `ExpectedWinFormsKinds` gains the seven names; `TheStripRows_HaveTheShapeTheSpecStates`: MenuStrip/ToolStrip/StatusStrip are `Docked`, `IsContainer == false`, have `Items` with `Kinds[0]` = `ToolStripMenuItem`/`ToolStripButton`/`ToolStripStatusLabel` and `Add` containing `Items.Add`; ToolStripMenuItem is `Item` with `Items` (`DropDownItems.Add`); the other three are `Item` with `Items == null`; every strip and item `SupportsTarget` both; `MenuStrip.FormProperty == "MainMenuStrip"`; `HtmlChildrenWrapper == "ul"` on MenuStrip and ToolStripMenuItem; `ToolStripButton.HtmlTag == "input"` with `HtmlInputType == "button"`; `ToolTipText` has `HtmlAttributeName == "title"`; `Enabled` is WinForms-only on every strip/item row EXCEPT ToolStripButton; `Checked/CheckOnClick/DisplayStyle/Spring/GripStyle/SizingGrip` are WinForms-only; every item kind's canonical host is Docked (`FormCatalogShapes` picks one). Also `DesignCodes.StripMisplaced == "BL8030"`.
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement the rows** (spec §4 table, csc facts M8; `WinFormsEnumType`s `DockStyle`, `ToolStripGripStyle`, `ToolStripItemDisplayStyle`):

```csharp
        // ==================================================================
        // Task 24 — menus, toolbars and status bars. Strips are Docked (no geometry, a Dock
        // PROPERTY); items live in their host's Children and are added by the HOST row's verb in
        // document order. ⛔ No Common(). ⛔ Enabled is WinForms-only except on the <input> —
        // a browser ignores `disabled` on <nav>/<li>/<span> (spec §4).
        // ==================================================================
        new("MenuStrip", "MenuStrip", "nav", null, false, new List<FormPropertyDef>
            {
                new("Dock", FormPropertyType.Enum, "Top", new[] { "Top", "Bottom" }, WinFormsEnumType: "DockStyle"),
                new("Enabled", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms }),
                new("Visible", FormPropertyType.Bool, "true")
            },
            DefaultHeight: 24, Schematic: FormSchematic.MenuBar,
            WinFormsEvent: "ItemClicked", WebEvent: "click", WinFormsEventArgs: "ToolStripItemClickedEventArgs",
            Place: FormPlace.Docked,
            Items: new FormItemRule(new[] { "ToolStripMenuItem", "ToolStripSeparator" }, "{parent}.Items.Add({child})"),
            FormProperty: "MainMenuStrip", HtmlChildrenWrapper: "ul",
            WebCss: ".vgs-MenuStrip ul{list-style:none;margin:0;padding:0;display:flex;background:#f0f0f0}" +
                    ".vgs-MenuStrip li{position:relative;padding:4px 10px;cursor:default}" +
                    ".vgs-MenuStrip li ul{display:none;position:absolute;left:0;top:100%;flex-direction:column;min-width:10em;border:1px solid #ccc;background:#fff}" +
                    ".vgs-MenuStrip li:hover>ul{display:flex}" +
                    ".vgs-MenuStrip li ul li ul{left:100%;top:0}"),
        new("ToolStrip", "ToolStrip", "menu", null, false, new List<FormPropertyDef>
            {
                new("Dock", FormPropertyType.Enum, "Top", new[] { "Top", "Bottom" }, WinFormsEnumType: "DockStyle"),
                new("GripStyle", FormPropertyType.Enum, "Hidden", new[] { "Hidden", "Visible" }, WinFormsEnumType: "ToolStripGripStyle", Targets: new[] { FormTarget.WinForms }),
                new("Enabled", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms }),
                new("Visible", FormPropertyType.Bool, "true")
            },
            DefaultHeight: 25, Schematic: FormSchematic.ToolBar,
            WinFormsEvent: "ItemClicked", WebEvent: "click", WinFormsEventArgs: "ToolStripItemClickedEventArgs",
            Place: FormPlace.Docked,
            Items: new FormItemRule(new[] { "ToolStripButton", "ToolStripSeparator" }, "{parent}.Items.Add({child})"),
            HtmlRole: "toolbar",
            WebCss: ".vgs-ToolStrip{display:flex;gap:4px;margin:0;padding:2px;background:#f0f0f0}"),
        new("StatusStrip", "StatusStrip", "footer", null, false, new List<FormPropertyDef>
            {
                new("Dock", FormPropertyType.Enum, "Bottom", new[] { "Top", "Bottom" }, WinFormsEnumType: "DockStyle"),
                new("SizingGrip", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("Enabled", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms }),
                new("Visible", FormPropertyType.Bool, "true")
            },
            DefaultHeight: 22, Schematic: FormSchematic.StatusBar,
            WinFormsEvent: "ItemClicked", WebEvent: "click", WinFormsEventArgs: "ToolStripItemClickedEventArgs",
            Place: FormPlace.Docked,
            Items: new FormItemRule(new[] { "ToolStripStatusLabel" }, "{parent}.Items.Add({child})"),
            HtmlRole: "status",
            WebCss: ".vgs-StatusStrip{display:flex;gap:8px;padding:2px 6px;background:#f0f0f0;border-top:1px solid #ccc}"),
        new("ToolStripMenuItem", "ToolStripMenuItem", "li", null, false, new List<FormPropertyDef>
            {
                new("Text", FormPropertyType.String),
                new("Enabled", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms }),
                new("Visible", FormPropertyType.Bool, "true"),
                new("Checked", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("CheckOnClick", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("ToolTipText", FormPropertyType.String, HtmlAttribute: "title")
            },
            Schematic: FormSchematic.MenuItem, WinFormsEvent: "Click", WebEvent: "click",
            Place: FormPlace.Item,
            Items: new FormItemRule(new[] { "ToolStripMenuItem", "ToolStripSeparator" }, "{parent}.DropDownItems.Add({child})"),
            HtmlChildrenWrapper: "ul"),
        new("ToolStripSeparator", "ToolStripSeparator", "li", null, false, new List<FormPropertyDef>
            {
                new("Visible", FormPropertyType.Bool, "true")
            },
            Schematic: FormSchematic.Separator, WinFormsEvent: "Click", WebEvent: "click",
            Place: FormPlace.Item, HtmlRole: "separator"),
        new("ToolStripButton", "ToolStripButton", "input", "button", false, new List<FormPropertyDef>
            {
                new("Text", FormPropertyType.String),
                new("Enabled", FormPropertyType.Bool, "true"),
                new("Visible", FormPropertyType.Bool, "true"),
                new("Checked", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("CheckOnClick", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("ToolTipText", FormPropertyType.String, HtmlAttribute: "title"),
                new("DisplayStyle", FormPropertyType.Enum, "Text", new[] { "None", "Text", "Image", "ImageAndText" }, WinFormsEnumType: "ToolStripItemDisplayStyle", Targets: new[] { FormTarget.WinForms })
            },
            Schematic: FormSchematic.ToolButton, WinFormsEvent: "Click", WebEvent: "click", Place: FormPlace.Item),
        new("ToolStripStatusLabel", "ToolStripStatusLabel", "span", null, false, new List<FormPropertyDef>
            {
                new("Text", FormPropertyType.String),
                new("Enabled", FormPropertyType.Bool, "true", Targets: new[] { FormTarget.WinForms }),
                new("Visible", FormPropertyType.Bool, "true"),
                new("Spring", FormPropertyType.Bool, "false", Targets: new[] { FormTarget.WinForms }),
                new("ToolTipText", FormPropertyType.String, HtmlAttribute: "title")
            },
            Schematic: FormSchematic.StatusLabel, WinFormsEvent: "Click", WebEvent: "click", Place: FormPlace.Item),
```

⚠ Positional parameters of `FormControlDef` are `(Kind, WinFormsType, HtmlTag, HtmlInputType, IsContainer, Properties, …)` — the six positional then named. ⚠ `Dock` stays `Targets: null` (shared). Add `DesignCodes.StripMisplaced = "BL8030"` with the spec §3 doc, and update the band comment: `BL8030 a strip item outside a host / a non-item in a host / a strip below the top level (here)`, `BL8031 reserved`, and a line "the enumerated table is exhausted; the next claim starts at BL8032".

- [ ] **Step 4: Run** `FormCatalogCoverageTests`, `FormToolboxGlyphTests`, `FormPropertyGridTests` (`TheToolbox_GroupsContainersAfterCommonControls` at `:586-589` stays GREEN here — a Docked row falls to "Common Controls" until Task 18 gives it the new category, and Task 18 Step 3 updates that pin first), `FormSchematicPinTests`. ⛔ Do NOT run `FormCanvasRenderTests` yet: `Layout` skips null geometry, so the three strips hash identical to the empty form until Task 17's bands land — the gate IS red between Task 12 and Task 17 by construction. Run it after Task 17.

⚠⚠ **AS BUILT (Task 12, 2026-09-21) — the rows went in exactly as written above; four things the
build taught that the next reader needs:**

1. **Step 2's red was only a COMPILE error, which proves nothing about the assertions.** The single
   missing symbol (`DesignCodes.StripMisplaced`) failed the whole test assembly, so not one of the
   new row-shape assertions ever executed — a tautological pin would have been invisible and the
   later green would have been unreadable. Step 3 was therefore split: the BL8030 constant and the
   band comment landed ALONE first, the fixture was re-run, and it produced a genuine red naming the
   seven missing rows (`'MenuStrip' is not in the catalog at all`, `'MenuBar' must be used by exactly
   one row, found 0`). Only then did the rows go in. **Do this for every task in this plan whose red
   is a compile error.**
2. **`TheStripRows_HaveTheShapeTheSpecStates` had a silent skip** — `if (enabled == null) continue;`
   meant nothing pinned that `ToolStripSeparator` has NO `Enabled` (spec §4 gives it only `Visible`),
   so a later row adding one as WinForms-only would have passed. Replaced with a two-way assertion:
   the separator declares none, every other strip/item row declares one.
3. **Eleven rows now bypass the shared `FormPropertyDef` fields** (`Text`/`Enabled`/`Visible` at
   `:580-582`) that exist expressly to stop rows drifting, and NOTHING pins the inline copies against
   them. Not changed here — the fix is a pin, not a refactor, and swapping the copies for the fields
   would make the table look consistent while leaving the same missing check. Filed as
   `docs/form-designer-followups.md` 25.
4. **`FindByHtmlTag("li")` is now ambiguous** (ToolStripMenuItem and ToolStripSeparator both emit
   `li`) and returns null. That is the documented rule and its one caller — `DomDialect.cs:160`,
   *"Reported rather than guessed"* — already sets `CatalogKind = null` on ambiguity, exactly as it
   did for `input` before this change. No action.

### Task 13: The reader — the `Place` branch and BL8030

**Files:** `Serialization/FormDocumentReader.cs` (`ReadControl` `:308-490`, the `case "Components"` caller); Create `VisualGameStudio.Tests/Compiler/FormStripDocumentTests.cs`.

- [ ] **Step 1: Failing tests** (fixture constant = spec §3's document with `MenuForm`, plus a Button):
  - `Read_AStrip_IsGeometryLess_WithDockAsAProperty`: `Geometry == null`, `Properties["Dock"] == "Top"`, `TabIndex == 0`, its children are the items in document order, `mnuOpen` nested under `mnuFile`, `sep1` between.
  - `Read_AnItem_HasNoGeometryAndNoTabIndex_AndKeepsStrayStructuralAttributes`: `<ToolStripMenuItem Id="x" Text="T" X="5" TabIndex="3"/>` under a strip → `Geometry == null`, `TabIndex == 0`, `UnknownAttributes["X"] == "5"`, `UnknownAttributes["TabIndex"] == "3"`.
  - `Read_RefusesAnItemOutsideAHost` (top-level `<ToolStripMenuItem>` under `<Controls>`), `Read_RefusesAControlInsideAHost` (`<Button>` under `<MenuStrip>`), `Read_RefusesAnItemKindTheHostDoesNotList` (`<ToolStripButton>` under `<MenuStrip>`), `Read_RefusesAStripBelowTheTopLevel` (`<MenuStrip>` under `<Panel>`): each `IsRefused` with exactly one `BL8030` naming the id.
  - `Algebra_RoundTrip_IsByteIdentical`, `Algebra_ANoOpSave_WritesNothing` (with `IsRefused` asserted false first), `Algebra_ReadApply_EqualsApplyRead_ForAnItemEdit` — the D9 algebra over the fixture (copy the shape of `FormComponentDocumentTests`).
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement.** `ReadControl` gains `FormControlDef? parentDefinition, int depth` (or a `parent` FormControl) parameters. Replace the `isComponent` bool logic with `var place = definition.Place;`:
  - BL8020 stays for `Tray` vs `<Components>` (`(place == FormPlace.Tray) != isComponentList`).
  - New, after it: if `place == FormPlace.Item` and `(parentDefinition == null || parentDefinition.Items?.Accepts(definition.Kind) != true)` → `Error(StripMisplaced, "'{id}' is a {Kind}, which lives inside a {hosts…} — under <Controls> it would be added with Me.Controls.Add, which does not compile" / "…but '{parentId}' holds only {kinds}")`, return null. If `parentDefinition?.IsHost == true && place != FormPlace.Item` → `"'{id}' is a {Kind}, but it sits under '{parentId}'. A {parentKind} holds only {kinds}"`. If `place == FormPlace.Docked && parentDefinition != null` → `"'{id}' is a {Kind}, which docks to the form — it sits under '{parentId}'"`.
  - `TabIndex = place == FormPlace.Positioned ? IntAttribute(…) ?? 0 : 0`; `Geometry = place == FormPlace.Positioned ? ReadGeometry(…) : null`.
  - The attribute skip: `place == FormPlace.Positioned ? IsStructural(name, target) : name == "Id"` (the component branch, now for Docked and Item too — so `Dock` reaches `definition.Property("Dock")`).
  - Children: recurse for `place != FormPlace.Tray`, passing `definition` as `parentDefinition`.
- [ ] **Step 4: Run** the fixture + `FormComponentDocumentTests` + `FormDocumentRoundTripTests` + `BlFormRoundTripTests` → green.

⚠⚠ **AS BUILT (Task 13, 2026-09-21) — one correction to the code above, and one measured fact that
justifies this whole commit being indivisible:**

1. **The parameter is `FormControl? parent`, not `FormControlDef? parentDefinition`.** Three of the
   four refusal messages name the parent's `Id` *as well as* its row, and passing
   `(parentDefinition, parentId)` would be two parameters carrying two halves of one parent that can
   disagree. `FormControl.Definition` is already a catalog lookup, so both are derived at the top.
2. **⛔ The plan's refusal condition above admits a shape its two-arm message does not cover, and the
   literal code would have CRASHED on it.** `<Panel><ToolStripMenuItem/></Panel>`: `parentDefinition`
   is non-null but `Items` is null, so the "holds only {kinds}" wording has no list and `Items!.Kinds`
   NREs *inside the reader* — turning a precise refusal into a crash on a document a user can easily
   hand-write. A third arm was added in the same voice ("…which lives inside a MenuStrip or a
   ToolStripMenuItem — it sits under 'pnl', a Panel, which is not one"), and its test was added in
   Task 14's round rather than left uncovered.
3. **`HostsOf(kind)` derives the host list from `FormControlCatalog.All`** instead of spelling
   "a MenuStrip or a menu item" into the message, so a row added later changes the sentence with it.
4. **⛔⛔ Task 12 ALONE puts a silent DATA-LOSS bug in the tree, which is the real reason 24c cannot
   be split into per-task commits.** With the `Dock` catalog property present but the reader still
   classifying `Dock` as a structural attribute, `Dock` never reaches `Properties`, and
   `FormDocumentWriter.ApplyControl`'s "a catalog property the model dropped" sweep then DELETES
   `Dock="Top"` from every strip **on the first save**. Measured, not theorised: the round-trip pin
   failed at 852 bytes against 880, and the diff is exactly the stripped attribute. Confirmed fixed
   by this reader-only change; the writer did NOT need pulling forward.
5. ⚠ **The round trip is green here by accident, not by construction.** The writer still passes
   `isComponent: false` for strips and items, so `SetIntAttributeIfChanged(element, "TabIndex", 0, 0)`
   *does* run on them — harmless only because that helper writes nothing when the attribute is absent
   and the value equals the default (`FormDocumentWriter.cs:655-667`). Task 14's `place == Positioned`
   guard is what makes it true by construction, and Task 14's pins must be built on a NON-default
   TabIndex or they pin nothing.

### Task 14: The writer and the clipboard mirror

**Files:** `Serialization/FormDocumentWriter.cs` (`ApplyControl` `:388-454`, `ControlElement` `:530-600`, `ApplyControlList`, `Create`), `FormDocument.cs` (`FormClipboard.ToElement` `:342-404`, `FromElement` `:415-490`, `RenumberTabIndexes` `:216-223`), `FormPlacement.NextTabIndex` (`:202-206`); Tests: `FormStripDocumentTests`, `FormDesignerCommandTests`.

- [ ] **Step 1: Failing tests:** `Create_WritesNoZerosAndNoTabIndex_OnAStripOrItem` (`FormDocumentWriter.Create(model)` of the fixture: the `<MenuStrip` element has attributes `Id` and `Dock` only; items have no `TabIndex`); `Apply_NeverWritesTabIndexOnAnItem` (renumber, then `Write` → the item element still has no `TabIndex`); `RenumberTabIndexes_SkipsStripsAndItems` (the Button after a strip is `TabIndex 0`); `Clipboard_ASerializedStrip_ComesBackGeometryLess_WithDockKept_AndNoTabIndex` (`FormClipboard.SerializeSubtree` → `DeserializeSubtree`); `Clipboard_AnItemRoot_KeepsItsChildren`.
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement:** replace every `isComponent` bool in the writer with a `FormPlace place = control.Definition?.Place ?? FormPlace.Positioned` derived per control (the `Components` list still passes `isComponent: true` for the reader-parity check — keep the parameter but compute `place` from the row): `TabIndex` and geometry written only when `place == Positioned`; children written when `place != Tray`. Same in `FormClipboard.ToElement` (`:348`) / `FromElement` (`:425-434`, `:439-441`: the attribute skip is `place == Positioned ? IsStructural(name, target) : name == "Id"`). `RenumberTabIndexes` and `NextTabIndex`: iterate `AllControls().Where(c => c.Definition?.Place is null or FormPlace.Positioned)` (⚠ `is null or` — a control with no row is Positioned, and `== Positioned` would be false for a null `Definition`).
- [ ] **Step 4: Run** the fixtures + `FormDesignerCommandTests` + `FormComponentDocumentTests` → green.

⚠⚠ **AS BUILT (Task 14, 2026-09-21) — one shape decision, one path the plan did not name, and one
mutant found before the gate:**

1. **`isComponent` was kept LOAD-BEARING, not forwarding-only.** The plan says "keep the parameter but
   compute `place` from the row", which admits two readings. The writer uses
   `PlaceOf(control, isComponent) => isComponent ? Tray : control.Definition?.Place ?? Positioned`,
   so the parameter still decides something. Deriving from the row ALONE would give a control with a
   null `Definition` sitting in `model.Components` the Positioned treatment — geometry and a tab
   order — which is the writer disagreeing with its own reader. Not observable from any current test
   (the reader refuses a non-Tray row under `<Components>` with BL8020); it guards a model the IDE
   builds in memory.
2. **⛔ `Create` and `Apply` are SEPARATE paths and both needed the guard.** `ControlElement` writes
   `element.SetAttributeValue("TabIndex", control.TabIndex)` **unconditionally** — not the incremental
   `SetIntAttributeIfChanged` — so `Create` stamped `TabIndex="0"` on all ten strip and item elements
   even at the default value. The plan names `ApplyControl` and `ControlElement` separately for this
   reason; do not assume fixing one fixes the other.
3. **⚠ The clipboard was wrong in THREE ways at once**, because it gated on `Definition.IsComponent`
   (Tray-only) and its geometry switch was not gated at all: a copied strip came back with a non-null
   `PixelGeometry{Dock="Top"}`, lost `Dock` from `Properties` entirely (consumed as geometry), and
   carried a stale `TabIndex` straight through.
4. **⛔ A mutant that survives the ENTIRE suite was found here, not at the gate.** Mutating
   `c.Definition?.Place is null or FormPlace.Positioned` to `== FormPlace.Positioned` in
   `RenumberTabIndexes` and `NextTabIndex` survives all 842 tests, because every fixture control has a
   catalog row so the null arm never runs. The arm is deliberate — a control whose `Kind` the catalog
   does not know must still be renumbered — and pinning it needs a HAND-BUILT `FormControl` with an
   unknown `Kind`, since the reader returns null for an unknown element and cannot produce one. The
   pin was written in Task 15's round.

### Task 15: The region writer — host verb, document order, `MainMenuStrip`

**Files:** `RegionWriter.cs` (`GenerateInit` `:487`, `AppendSiblings` `:561-579`, `AppendControlInit` `:591-613`); Create `VisualGameStudio.Tests/Compiler/FormStripEmissionTests.cs`.

- [ ] **Step 1: Failing tests** (`Emit(doc)` helper as in `FormComponentEmissionTests`; the §3 document as the model):
  - `WinForms_ItemsAreAddedWithTheHostVerb_InDocumentOrder`: the text contains, IN THIS ORDER, `mnuFile.DropDownItems.Add(mnuOpen)`, `mnuFile.DropDownItems.Add(sep1)`, `mnuFile.DropDownItems.Add(mnuExit)`, `menuStrip1.Items.Add(mnuFile)`; and `Does.Not.Contain("Controls.Add(mnuFile)")`, `Does.Not.Contain("Controls.Add(mnuOpen)")`.
  - `WinForms_StripsAreAddedToTheFormReversed_AndMainMenuStripFollows`: `Me.Controls.Add(statusStrip1)` before `Me.Controls.Add(toolStrip1)` before `Me.Controls.Add(menuStrip1)`, then `Me.MainMenuStrip = menuStrip1` AFTER the last add; exactly one `MainMenuStrip` line; `menuStrip1.Dock = DockStyle.Top` present; no `menuStrip1.Location`.
  - `WinForms_AnItemsSubtreeIsBuiltBeforeItsHostIsAdded`: index of `mnuFile.DropDownItems.Add(mnuOpen)` < index of `menuStrip1.Items.Add(mnuFile)`.
  - `Web_ItemsAreFetchedById_AndWired`: `mnuOpen = doc.getElementById("mnuOpen")`, `mnuOpen.addEventListener("click", AddressOf mnuOpen_Click)`, no `MainMenuStrip`, no `Items.Add`.
- [ ] **Step 2: Run** → red (today: `Controls.Add`, reversed).
- [ ] **Step 3: Implement.** `AppendSiblings(body, form, controls, parent, …)` gains a `FormControl? parentControl` parameter (null for the root; `control` at the call in `AppendControlInit`). The add run becomes:

```csharp
        if (form.Target != FormTarget.WinForms) return;

        var rule = parentControl?.Definition?.Items;
        if (rule != null)
        {
            // A host's items: the HOST row's verb, in DOCUMENT order — items are ordered, not layered.
            // Reversing them here would run File/Edit/Help as Help/Edit/File from a green build.
            foreach (var child in controls)
            {
                body.Append(inner).Append(rule.Add.Replace("{parent}", parent).Replace("{child}", child.Id)).Append(newline);
            }
            return;
        }

        for (var i = controls.Count - 1; i >= 0; i--) { … the existing reversed Controls.Add … }
```

In `GenerateInit`, after the root `AppendSiblings(...)` call and INSIDE a `form.Target == FormTarget.WinForms` check:

```csharp
            // The first MenuStrip in document order (row flag FormProperty), AFTER the add run — WinForms only.
            var main = form.Controls.FirstOrDefault(c => c.Definition?.FormProperty != null);
            if (main != null)
            {
                body.Append($"{inner}Me.{main.Definition!.FormProperty} = {main.Id}").Append(newline);
            }
```

- [ ] **Step 4: Run** `FormStripEmissionTests`, `FormRegionWriterTests`, `FormComponentEmissionTests` → green. Then the csc sweep (Integration) → the seven rows pass all three sweeps (an item builds under its canonical host).

⚠⚠ **AS BUILT (Task 15, 2026-09-21) — the code above went in as written; three measured facts:**

1. **The csc sweep is 98/98, not 80/80, and the +18 belongs to Task 12, not to this task.** Each of
   the seven new rows adds one case to both `[TestCaseSource(nameof(EveryWinFormsControl))]` tests
   (14), and four of them declare an Enum property — MenuStrip (`Dock`), ToolStrip (`Dock`,
   `GripStyle`), StatusStrip (`Dock`), ToolStripButton (`DisplayStyle`) — adding one case each to
   `EveryWinFormsControlWithAnEnum` (4). ToolStripMenuItem, ToolStripSeparator and
   ToolStripStatusLabel declare none. This run is also the first to put `Me.MainMenuStrip` and BOTH
   host verbs through real csc.
2. ⚠ **Only the item ADD RUN was wrong today — the strip half already worked.** Top-level strip
   reversal and `menuStrip1.Dock = DockStyle.Top` emitted correctly before this change; the second
   pin failed solely on the missing `Me.MainMenuStrip` line. What was broken was host-blind and
   reversed: `mnuFile.Controls.Add(mnuExit)/Add(sep1)/Add(mnuOpen)` and
   `menuStrip1.Controls.Add(mnuFile)`.
3. ⚠ **`AppendSiblings`'s own summary claimed the reversal UNCONDITIONALLY** and had to be corrected
   with the change, or it would have described the opposite of what the method now does for a host.
4. ⚠ **vstest filter syntax: every clause needs its property name.** `(~Form)&(TestCategory!=…)` is
   rejected with *"Invalid Condition '~Form'"*; it must be
   `(FullyQualifiedName~Form)&(TestCategory!=Integration)&(FullyQualifiedName!~FormCanvasRenderTests)`.
   Carry this into Task 19's gate commands.

### Task 16: The web emitter — chrome, wrappers, roles, no tabindex, `&`

**Files:** `FormAssetEmitter.cs` (`Html` `:100-136`, `AppendControl` `:138-272`, `Css` `:282-331`); Tests: `FormStripEmissionTests`.

- [ ] **Step 1: Failing tests:** `Web_StripsArePageChrome`: the HTML has `<nav id="menuStrip1"` and `<menu id="toolStrip1"` BEFORE `<div class="vgs-form">`, `<footer id="statusStrip1"` AFTER `</div>`, `data-form` on `<body>`, none of the strip ids inside the div; `Web_TwoBottomStrips_AreEmittedInReverseDocumentOrder`; `Web_MenuNesting` — ⚠ the emitter writes a newline plus indent between a parent's opening tag and its children (`FormAssetEmitter.cs:260-269`), so assert ORDERED fragments with `IndexOf`, never one contiguous string: `<nav id="menuStrip1" class="vgs-MenuStrip"` < `<ul>` < `<li id="mnuFile" class="vgs-ToolStripMenuItem"` < `File` < `<ul>` < `<li id="mnuOpen"` < `<li id="sep1" class="vgs-ToolStripSeparator" role="separator">` < `</ul>` < `</li>` (and `&amp;File` is NOT present — the `&` is stripped); `<menu id="toolStrip1" class="vgs-ToolStrip" role="toolbar"` then `<input id="tsbOpen" class="vgs-ToolStripButton" type="button" value="Open">`; `<footer` … `role="status"` then `<span id="lblStatus"` … `>Ready</span>`; no `tabindex` on any strip or item element; `Web_ToolTipTextBecomesTitle`; `Web_TheStylesheetCarriesEachPresentStripsCss_Once`.
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement** in `Html`: partition `form.Controls` into `top = Docked with Dock (property or default) == "Top"`, `bottom = Docked … "Bottom"`, `rest`; emit `top` in document order before the div, `rest` inside, `bottom` in REVERSE document order after `</div>`. In `AppendControl`: `tabindex` only when `definition.Place == FormPlace.Positioned`; after `class`, `if (definition.HtmlRole != null) sb.Append($" role=\"{definition.HtmlRole}\"")`; the text for an `Item` row strips `&` (`text.Replace("&", "")`, once — a literal `&&` in a WinForms caption means one `&`; handle `&&` → `&` first); children wrapped: `if (definition.HtmlChildrenWrapper is { } w) sb.Append($"<{w}>")` before the child loop and the close after. In `Css`: after the per-control rules, `foreach (var css in form.AllControls().Select(c => c.Definition?.WebCss).Where(s => s != null).Distinct()) sb.Append(css).Append('\n')`.
- [ ] **Step 4: Run** the fixture + `FormAssetEmitterTests` (or whatever the existing emitter fixture is called — grep `FormAssetEmitter.Html(` in Tests) → green.

⚠⚠ **AS BUILT (Task 16, 2026-09-21) — the code above went in as written; one new shared question and
four measured gaps:**

1. **`Dock` is resolved by a new `DockOf`** — the document `Properties["Dock"]` first, falling back to
   `Definition?.Property("Dock")?.Default`, then `"Top"` — so a hand-written `.blwebform` with no
   `Dock` attribute still puts a StatusStrip at the bottom from its row default. ⛔ This is the SAME
   question Task 17's `Bands` snippet asks, in a different project; it was consolidated into one
   answer in `BasicLang/Forms` when Task 17 landed, because two copies would let the designer draw
   the status band on one edge while the page puts the `<footer>` on the other, from one document.
2. **Accelerator stripping is `Regex.Replace(text, "&(&?)", "$1")` on the `text` local**, the same
   spelling Task 18 uses for `ItemId` — one pass, so `&&` → `&` and a lone `&` → nothing with no
   ordering hazard between two `Replace` calls. Because it rewrites the local, it also covers a
   ToolStripButton's `value=`; `ToolTipText` is untouched (generic property loop, and a tooltip
   carries no accelerator).
3. ⛔ **`tabindex` was written UNCONDITIONALLY at the old `:153`** for every control regardless of
   `Place` — measured, every strip and item carried `tabindex="0"`. It is now behind
   `Place == FormPlace.Positioned`.
4. ⚠⚠ **Four gaps these tests do NOT close, all measured; closed at Task 19:**
   - **No test parses the emitted HTML.** `Web_MenuNesting` asserts ordered fragments, which proves
     SEQUENCE but not BALANCE — a wrapper opened and never closed, or closed twice, passes every
     assertion in the file. This is "a green build is not a running page" one level up from
     `FormBuildEmissionTests`.
   - `DockOf`'s fallback arm is unpinned: every fixture writes `Dock=` explicitly, so dropping the
     row-default lookup survives the whole sweep.
   - The `&&` arm of the accelerator regex is unpinned: fixtures use only a lone `&`, so mutating the
     regex to `text.Replace("&", "")` fails nothing.
   - `Css`'s `Distinct()` is on the BLOCK STRING, not the kind — two rows sharing a byte-identical
     `WebCss` would collapse to one. Correct today only because every block is kind-prefixed.

### Task 17: The canvas bands — `Layout(document, selected)` and the band arms

**Files:** `FormCanvasTransform.cs` (`Layout` `:320-376`, `HitTest` `:89-126`, `ContainerAt` `:259-294`, `ControlsIn` `:143-162`), `FormCanvasControl.cs` (`Render` `:1108`, `FormBoundsOf` `:993`, `OnPointerPressed` `:583/:617`, `OnCanvasDoubleTapped` `:498`); Create `VisualGameStudio.Tests/Shell/FormStripLayoutTests.cs`; the transform tests that deconstruct 2-tuples (`FormCanvasTransformTests.cs`, `FormCanvasRenderTests.cs:123-124`).

- [ ] **Step 1: Failing tests** (pure transform tests, no rendering):
  - `Layout_YieldsATopStripAsABandAcrossTheSurface`: MenuStrip → entry with `Bounds == new Rect(0, 0, surface.Width, 24)`, `Role == FormLayoutRole.Band`.
  - `Layout_StacksTopStrips_InDocumentOrder_AndBottomStripsFromTheEdge`: MenuStrip then ToolStrip → y 0 then 24; StatusStrip at `surface.Height - 22`; a second Bottom strip above it.
  - `Layout_NeverYieldsAZeroRect_ForAStrip`; `Layout_YieldsBandsOnAWebPage_RegardlessOfLayoutKind` (a Flow page with a MenuStrip still yields the band).
  - `HitTest_ReturnsTheStrip_OnTheBand_OnBothTargets`; `ContainerAt_NeverReturnsAStrip`; `ControlsIn_NeverReturnsAStripOrAnItem`.
  - Items yield NOTHING yet (24d adds cells): `Layout_DoesNotYieldItemsYet` — assert no entry whose control is an Item (this test is replaced in 24d).
- [ ] **Step 2: Run** → compile errors (`Role`, the overload).
- [ ] **Step 3: Implement.** New `public enum FormLayoutRole { Control, Band, Cell, TypeHere }` and — declared ONCE, here, with all four fields, because a positional record struct's `Deconstruct` changes when a field is added later and 24c's tests would deconstruct three — `public readonly record struct FormLayoutEntry(FormControl? Control, Rect Bounds, FormLayoutRole Role, FormControl? Host = null);` (`Host` is null for everything but a `TypeHere` slot, which Task 20 adds; spec §2 wrote a 3-tuple and its own `TypeHereHost` row says two slots can be visible at once, which a 3-tuple cannot attribute — Task 29's §10 records the change). `Layout(FormDocument document, FormControl? selected = null)` returns `IEnumerable<FormLayoutEntry>`: the existing positioned walk (skipping `Docked` roots — they are yielded as bands, never through `BoundsOf`), then `Bands(document)`:

```csharp
    private static IEnumerable<FormLayoutEntry> Bands(FormDocument document)
    {
        var surface = SurfaceSize(document);
        double top = 0, bottom = surface.Height;
        foreach (var strip in document.Controls.Where(c => c.Definition?.Place == FormPlace.Docked))
        {
            var height = strip.Definition!.DefaultHeight;
            var dock = strip.Properties.TryGetValue("Dock", out var d) ? d : strip.Definition.Property("Dock")?.Default ?? "Top";
            Rect band;
            if (string.Equals(dock, "Bottom", StringComparison.OrdinalIgnoreCase))
            {
                bottom -= height;
                band = new Rect(0, bottom, surface.Width, height);
            }
            else
            {
                band = new Rect(0, top, surface.Width, height);
                top += height;
            }
            yield return new FormLayoutEntry(strip, band, FormLayoutRole.Band);
        }
    }
```

`WebLayout` yields its cells only for a Grid layout but ALWAYS appends `Bands`. `HitTest(document, canvasPoint, selected = null)` reads `Layout(document, selected)` on BOTH targets (`.Where(e => e.Control != null && e.Bounds.Contains(formPoint)).Select(e => e.Control).LastOrDefault()`), deleting the recursive `BoundsOf` walk; `ControlsIn` filters `e.Control != null && e.Control.Definition?.Place is not (FormPlace.Item or FormPlace.Docked)` and then `.Select(e => e.Control!)` — it still returns `IReadOnlyList<FormControl>` under `<Nullable>enable`; `ContainerAt` skips `Docked` roots. Update the consumers: `Render` (`foreach (var entry in FormCanvasTransform.Layout(document, SelectedControl))` → `if (entry.Control != null) DrawControl(context, entry.Control, _transform.ToCanvas(entry.Bounds));`), `FormBoundsOf` (`FormCanvasControl.cs:993`, and `Render` at `:1108`), `FormCanvasRenderTests.cs:123-124`, `FormCanvasTransformTests.cs:217-222/:351-354/:363/:376` — exactly these deconstruct the old 2-tuple (⚠ `FormZOrderTests` calls only `HitTest`, whose `selected` is defaulted; nothing there changes). ⚠ The overflow edge (spec §2 Layout row): add `FormStripLayoutTests.HitTest_FindsAChildWhereItIsPainted_EvenOutsideItsPanel` and pin whichever the new walk does.
- [ ] **Step 4: Run** `FormStripLayoutTests`, `FormCanvasTransformTests`, `FormZOrderTests`, `FormCanvasMultiSelectTests`, `FormCanvasRenderTests` (the three strips now hash as bands, distinct), `FormTrayViewTests` → green. Add `FormCanvasRenderTests.AStripWithAButton_DrawsABandAtTheTop_AndLeavesTheButtonAlone` (frame with MenuStrip+Button differs from Button alone; the Button's own region — crop or compare a sub-rectangle hash — is unchanged).

⚠⚠ **AS BUILT (Task 17, 2026-09-21) — the mirrored pair was collapsed, the consumer list above is
partly wrong, and three of the nine pins were vacuous at first:**

1. **The `Dock` question is ONE answer now, on `FormControl` in `BasicLang/Forms/FormControl.cs`:**
   `IsDockedToBottom` (public) over a private `DockEdge` (document property, then the row default,
   then `"Top"`). Both `FormAssetEmitter.Html` and `FormCanvasTransform.Bands` call it; Task 16's
   private `DockOf` is deleted. ⚠ The **decision** is exposed, not the edge string — a public
   `DockEdge` would leave both sites spelling `string.Equals(…, "Bottom", OrdinalIgnoreCase)`
   themselves, which is the same drift one level down. Without this, the designer could draw the
   status band on one edge while the page put the `<footer>` on the other, from one document.
2. **⛔ The empty-string guard is REACHABLE — Task 16's note calling it unreachable is wrong.**
   `Dock=""` is legal XML and `FormDocumentReader.cs:508` stores an attribute value VERBATIM before
   the `property.Accepts` check, because the D9 Degraded tier keeps a bad value so it round-trips.
   Without the guard `""` is merely "not Bottom", so `<StatusStrip Dock=""/>` docks to the TOP
   instead of falling back to its row's own `Bottom` default. Kept, and documented at the member.
3. ⚠ **The consumer list above over-states the damage.** `FormCanvasTransformTests.cs:351-354`,
   `:363` and `:376` did NOT need touching — they access `.Control.Id`, which the record preserves;
   only a null-forgiving `!` was needed where the field became nullable. The real 2-tuple
   deconstructions were in `FormCanvasControl.cs` at `:995` and `:1124`.
4. ⚠ **Three of the nine pins passed before `Bands` existed.** Two became load-bearing afterwards —
   `ControlsIn`'s strip half now kills a dropped `Docked` filter, which it could not have caught
   while `Layout` yielded no strip at all. The third, `ContainerAt_NeverReturnsAStrip`, still passes
   BY ACCIDENT: `BoundsOf` returns null for a geometry-less strip, so `ContainerAt` skips it whether
   or not the new explicit `Docked` rule is there. **That rule is unfalsifiable today** — same shape
   as 24b's unreachable schematic arms. Recorded, not faked.
5. The render gate closed here as predicted. `EveryControlKindRendersDistinctly` selects
   `Place != Tray && Place != Item`, so the three strip rows ARE in its loop; with no band arm all
   three rendered as the empty form and collided on one hash.

### Task 18: Placement, toolbox category, grid, drop refusal, recognizer

**Files:** `FormPlacement.cs` (`Place` `:37-106`, new `PlaceItem`), `FormToolboxViewModel.cs` (category), `FormPropertyGridViewModel.cs` (`:176-183`), `CodeEditorDocumentViewModel.cs` (`PlaceDroppedControl` `:566`, `TrayDrop` `:587`, `ReportPlacementRefusal` `:613`), `Recognizer/WinFormsDialect.cs` (`:161`, `:302-315`); Tests: `FormPlacementTests`, `FormToolboxGlyphTests`/`FormPropertyGridTests` (incl. `TheToolbox_GroupsContainersAfterCommonControls`), `FormTrayTests`-style VM tests in a new `FormStripEditorTests.cs` (VM level), `FormStripRecognizerTests.cs`.

- [ ] **Step 1: Failing tests:** `Place_ADockedKind_LandsTopLevel_GeometryLess_WithTheRowsDock_IgnoringThePoint`; `Place_AnItemKind_IsRefused_NamingTypeHere` (message contains "Type Here"); `PlaceItem_AppendsToTheHost_WithTheCaptionAsId` (`PlaceItem(doc, menuStrip, "ToolStripMenuItem", "&Open...")` → id `openToolStripMenuItem`, `Text == "&Open..."`, last child of the host; a second `Open` → `openToolStripMenuItem1`; `"-"` → a `ToolStripSeparator` `toolStripSeparator1`; `"123"` → `toolStripMenuItem1` fallback; a kind the host does not accept → refusal); toolbox: the three strips appear under category `Menus & Toolbars`, no item kind is offered; grid: `Rows_ForAStrip_HaveDockAndNoTabIndexOrGeometry`, `Rows_ForAnItem_HaveNoTabIndex`; VM: `TrayDropCommand.Execute("ToolStripMenuItem")` and `PlaceDroppedControl` of an Item kind publish BL8019 whose message contains "Type Here" and place nothing; recognizer: `design --check` on a `.bas` with `menuStrip1.Items.Add(mnuFile)` and `mnuFile.DropDownItems.Add(mnuOpen)` reports NO BL8006 for `mnuFile`/`mnuOpen` (build the source through `DesignCheck.CheckSource`).
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement.** Toolbox category pin first: `FormPropertyGridTests.TheToolbox_GroupsContainersAfterCommonControls` (`:586-589`) becomes `["Common Controls","Containers","Menus & Toolbars","Components"]` with the new category ranked 2 and Components 3. Then `FormPlacement.Place`: after the Tray branch, `if (definition.Place == FormPlace.Item) return new FormPlacementResult(null, $"'{definition.Kind}' is created from its menu's Type Here slot, not dropped.");` and `if (definition.Place == FormPlace.Docked) { var strip = new FormControl { Kind, Id = NextId(…) }; var dock = definition.Property("Dock"); if (dock?.Default != null) strip.Properties["Dock"] = dock.Default; document.Controls.Add(strip); return new(strip, null); }`. New:

```csharp
    /// <summary>Type Here (spec §6): appends an item of <paramref name="kind"/> to <paramref name="host"/>, or refuses.</summary>
    public static FormPlacementResult PlaceItem(FormDocument document, FormControl host, string kind, string text)
    {
        var rule = host.Definition?.Items;
        var definition = FormControlCatalog.Find(kind);
        if (rule == null || definition == null || !rule.Accepts(definition.Kind))
            return new FormPlacementResult(null, $"'{host.Id}' ({host.Kind}) holds {string.Join(", ", rule?.Kinds ?? Array.Empty<string>())}, not a {kind}.");

        var item = new FormControl { Kind = definition.Kind, Id = ItemId(document, definition, text) };
        if (definition.Property("Text") != null && definition.Kind != "ToolStripSeparator") item.Properties["Text"] = text;
        host.Children.Add(item);
        return new FormPlacementResult(item, null);
    }

    /// <summary>
    /// VS's id: the caption camel-cased and sanitised + the kind (`openToolStripMenuItem`); `-` →
    /// `toolStripSeparator1`; an unusable caption (empty, leading digit) → `toolStripMenuItem1`.
    /// </summary>
    public static string ItemId(FormDocument document, FormControlDef definition, string text)
    {
        var kindPart = char.ToLowerInvariant(definition.Kind[0]) + definition.Kind[1..];

        // `&&` is a literal ampersand in a WinForms caption, a lone `&` is the accelerator mark:
        // one regex — never String.Replace with an empty pattern, which throws.
        var plain = System.Text.RegularExpressions.Regex.Replace(text, "&(&?)", "$1");
        var words = new string(plain.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var caption = string.Concat(words.Select((w, i) =>
            i == 0 ? char.ToLowerInvariant(w[0]) + w[1..] : char.ToUpperInvariant(w[0]) + w[1..]));

        var usable = caption.Length > 0 && char.IsLetter(caption[0]) && definition.Kind != "ToolStripSeparator";
        Func<string, bool> taken = id => document.FindById(id) != null;

        // ⚠ MakeUniqueId returns its argument UNCHANGED when free (FormDocument.cs:153-158), so the
        // fallback is seeded with the "1" the way NextId seeds `kind + "1"` — `toolStripSeparator1`,
        // not `toolStripSeparator`. The caption stem is not seeded: `openToolStripMenuItem`, then
        // `openToolStripMenuItem1` when taken.
        return usable && FormDocument.IsLegalControlId(caption + definition.Kind)
            ? FormDocument.MakeUniqueId(caption + definition.Kind, taken)
            : FormDocument.MakeUniqueId(kindPart + "1", taken);
    }
```

(⚠ Read `FormDocument.MakeUniqueId`'s real signature at `FormDocument.cs:153-174` and match it.) Toolbox: category `Place == Docked ? "Menus & Toolbars"` sorted after Containers (`OrderBy(c => c.Place switch { Tray => 3, Docked => 2, _ when c.IsContainer => 1, _ => 0 })`). Grid: `if (control.Definition?.Place is null or FormPlace.Positioned)` around the TabIndex row. VM: `TrayDrop`'s refusal text for a non-component already names "drop it on the form"; add an `Item` check first with the Type Here message; `PlaceDroppedControl` gets the refusal from `Place` automatically. Recognizer: at `WinFormsDialect.cs:161` also match `CheckName(2, "Items") && CheckName(4, "Add")` and `CheckName(2, "DropDownItems") && CheckName(4, "Add")` → `MarkParented` (same token layout: `Peek(6)` is the child id). Comment at the site: a ComboBox's `cmb.Items.Add("Apple")` reaches this arm with a StringLiteral at `Peek(6)`, which `MarkParented` ignores — the same net effect as today's fallthrough — so nobody later "fixes" the arm to exclude ComboBox.
- [ ] **Step 4: Run** all named fixtures → green.

### Task 19: Gate and commit 24c

- [ ] Ordering inside this commit (spec Decision 12): Tasks 12→13→14 (format), 15→16 (emission; run the csc sweep after 15), 17 (bands; run the render gate after), 18 (surface entry points). ⚠ TWO gates are red by construction inside this commit: the render gate between Task 12 and Task 17 (strips hash like the empty form until bands land), and `WinFormsCatalogSweepTests` between Task 12 and Task 15 (`Canonical` nests an item under its host and the unchanged `AppendSiblings` emits `ctlHost.Controls.Add(ctl)` — CS1503, spec M8). Run each after the task that closes its window, and say so in the commit message. (The retarget sweep stays green throughout: `FormRetarget.ToPixels` `:548-551` tolerates a null Layout and `DeriveCells` `:514-527` folds Anchor into BL8025, never BL8024.)
- [ ] Spec §10 (opened by 24a's Task 6 with the `WidensTo` row) gains its designer row in THIS commit: "the layout entry is a four-field record (`Host` on a slot; §2 wrote a 3-tuple)". The record struct lands here, so its record does too; Task 29 extends §10, it does not write this row again.
- [ ] `dotnet clean` is NOT needed (no AXAML yet). Build; fast subset; `WinFormsCatalogSweepTests`; `FormCanvasRenderTests`; `FormSchematicPinTests`; `FormRetargetTests` (the sweep builds canonical shapes — items nest, strips have no geometry; if `Place` (web→pixels) throws on the Docked canonical shape here, do 24e's Task 26 rule NOW and move it into this commit).
- [ ] Mutants: reverse the host-verb loop → `InDocumentOrder` fails; emit `Controls.Add` for items → csc sweep fails (CS1503) AND the emission test; drop the `<ul>` wrapper → `Web_MenuNesting`; forward-order the Bottom chrome → `TwoBottomStrips`; write TabIndex on an item in `Create` → `Create_WritesNoZeros…`; let the reader accept a Button under a MenuStrip → `Read_RefusesAControlInsideAHost`; `Bands` yields a 0-height rect → `Layout_NeverYieldsAZeroRect`.
- [ ] Commit `feat(designer): Task 24c — menus, toolbars and status bars: rows, format, emission on both targets, bands`.

---

# Commit 24d — the editing surface (spec §6)

## ⛔⛔ PRE-FLIGHT CORRECTIONS (2026-09-21, measured — READ BEFORE TASK 20)

A read-only 9-agent recon verified every anchor in Tasks 20–25 against the tree at `392bcb5d` and
adversarially hunted this repo's measured defect classes. **The tasks below are left verbatim as written
evidence; where they disagree with this block, THIS BLOCK WINS.** Six blocker-class defects were found in a
plan that had already had three review passes — four of them ship a GREEN BUILD WITH A DEAD FEATURE.

### The anchors have drifted — 21 of them. Re-locate, never trust a cited line.

The plan was written 2026-09-20; 24a/24b/24c have landed since. Corrected:

| Cited | Actual |
|---|---|
| `FormCanvasControl.cs` styled block `:34-215` | registrations `:36-199`, accessors to `:217`, first non-property member `:219`; `AffectsRender` static ctor `:93-99` |
| `OnPointerPressed` `:563-632` | **`:565-675`** — the cited end is 43 lines short and lands *inside* the marquee branch |
| `Render` `:1085-1160` | **`:1087-1179`**; the Layout loop is `:1124-1130` |
| HitTest call sites `:498`, `:583`, `:617` | **`:500`, `:585`, `:619`** (each 2 low) |
| `CodeEditorDocumentView.axaml:223-235` | the `FormCanvasControl` element is **`:224-235`**; `:223` is its containing Grid |
| `CodeEditorDocumentViewModel.cs:864` Selection.Changed | **`:875`** (`:864` is `_eventAggregator = …`). It IS expression-bodied — that half confirmed |
| `Layout :320-376`, `HitTest :89-126`, `ContainerAt :259-294`, `ControlsIn :143-162` | `:330-339`, `:96-116`, `:255-300`, `:133-158`; `Bands` is `:357-380` |
| "**Create** `VisualGameStudio.Tests/Compiler/FormStripEditorTests.cs`" | ⛔⛔ **IT ALREADY EXISTS** (24c, 89 lines, 2 tests). **EDIT it. A subagent using Write on an untracked test file has already destroyed four tests once in this feature.** |
| `FormStripCanvasTests.cs` | genuinely NOT FOUND — new work |
| `Cells(...)`, `TypeHereAt(...)` | NOT FOUND — Task 20 creates both; Task 21 will not compile without them |

⚠ Task 20 declares `Cells`/`TypeHereAt` as **static** but Task 21's snippet calls `_transform.TypeHereAt(...)`
on an instance. Pick one and make both sites agree.

### BLOCKER 1 — `[ObservableProperty]` applies to ONE field, and the whole feature is bound through the other two

`[ObservableProperty] bool _isActive; FormControl? _host; string _text = "";` is **three** field declarations;
the attribute binds `_isActive` alone. `Host` and `Text` then raise no notification, and Task 24 binds exactly
those three (`TypeHereHost`, `Host`, `Text` TwoWay). They bind once at attach and never update: the slot
highlight never moves, the editor never re-focuses, "type a whole menu in one run" is dead.
**Nothing in 24d can see it** — Task 22 sets the control's own properties, Task 23 reads `vm.StripEditor.Host`
directly, Task 24 parses XML text. `IsActive` works, so the overlay appears and looks wired.
⛔ Worse, measured: `TypeHereBounds` is written only inside `Render`, and `Render` runs only on an
`AffectsRender` change. On the real gesture **the host is ALREADY the selection** (a slot is yielded only for
the selected strip), so `SelectInDesigner(host)` is a no-op on both stores (`FormSelection.cs:53-56`) and
`TypeHereHost` is the only thing that changes. No notification → no render → `TypeHereBounds` stays `default`
→ and since the plan forces `MinWidth`/`MinHeight` to 0, the shipped editor is a **0×0 focused TextBox**.
**FIX:** `[ObservableProperty]` on all three fields. **Add a test that subscribes to
`StripEditor.PropertyChanged`** and requires a raise for `Host` on Begin and `Text` on Commit. A binding defect
needs a notification assertion; nothing else sees it.

### BLOCKER 2 — nothing executes the three new commands, and this repo has NO compiled bindings

⭐ **Measured:** `CodeEditorDocumentView.axaml:13` has `x:DataType`, but `AvaloniaUseCompiledBindingsByDefault`
appears **nowhere in the repo** and there is no `Directory.Build.props`. Every `{Binding …}` there is a
**reflection** binding — it binds successfully to nothing. Task 23's tests call the METHODS
(`vm.BeginTypeHere(...)`); Task 24's gate compares attribute STRINGS. So if the toolkit generates no command —
the exact `AddNewFormCommand` failure, where a doc comment between the attribute and its method made it bind to
the next declaration — build green, VM tests green, AXAML gate green, menu impossible to create.
⛔ **The plan's stated justification for public methods is FALSE:** it cites "`PlaceControl` is the precedent",
but `PlaceControl` (`:655`) carries **no** `[RelayCommand]` — the one at `:652` belongs to `CommitGeometry`
(`:653`). **All 15 `[RelayCommand]`s in that file are on PRIVATE methods.** There is no precedent for the shape.
**FIX:** (a) Task 23 drives at least one full cycle through the GENERATED members —
`vm.BeginTypeHereCommand.Execute(strip)`, `CommitTypeHereCommand.Execute("&File")`,
`CancelTypeHereCommand.Execute(null)`; (b) the Task 24 gate reflects every `{Binding *Command}` name it parses
against `typeof(CodeEditorDocumentViewModel)` and asserts a non-null `ICommand` property — data-driven off the
XML so it covers future bindings too; (c) keep every attribute physically adjacent to its method.

### BLOCKER 3 — `PasteControls`' Item branch adds the control to TWO lists

The snippet puts `continue` only in the `else`. It is inserted into a loop whose last statement is the
**unconditional** `(… ? Components : Controls).Add(control);` at `:461`. The success path falls through and one
`FormControl` reference lands in both `host.Children` and `document.Controls`. It compiles; the plan's own Step 1
assertion passes. Then `AllControls()` (`FormDocument.cs:95`) yields it twice → the writer emits it twice,
`RenumberTabIndexes` walks it twice, and `ListContaining` returns `Controls` first, so **the next Delete removes
the wrong copy**.
⚠ Compounding: `Selection.SetRange(added)` with an EMPTY `added` *clears* the selection
(`FormSelection.cs:96-110`), contradicting the same step's "the selection unchanged" and — with the new
leave-rule — cancelling the editor on a refused paste.
**FIX:** one `if / else if / else` chain whose final `else` is the existing `:461` line, and
`if (added.Count == 0) return;` before the renumber/`SetRange`/write. Assert
`AllControls().Count(c => ReferenceEquals(c, pasted)) == 1`.

### BLOCKER 4 — the overlay is born VISIBLE

Avalonia's `IsVisible` defaults **true**, `IsActive` defaults **false**, and setting a styled property to the
value it already holds raises no notification — so the Task 24 binding resolving to `false` fires nothing and
`OnPropertyChanged` never runs at startup. An empty focusable TextBox sits permanently over the design surface
at (0,0). Step 1's only hidden-state assertion reaches `false` by first setting `true`, exercising the one edge
the shipping path never takes.
⛔ And if an implementer gives the overlay a `Background`, it swallows every canvas click — the natural thing to
do, because `FormCanvasControl.cs:1091-1096` teaches the opposite rule (it fills the viewport with
`Brushes.Transparent` precisely *in order to be* hit-testable).
**FIX:** `IsVisible = false; IsHitTestVisible = false;` in the CONSTRUCTOR too; leave `Background` null. Assert
both on a freshly constructed editor **before** any activation — that assertion fails against the plan's own
snippet, which is the point.

### BLOCKER 5 — Task 20's cell assertions are tautological, and the one fixture that would catch it agrees by accident

Step 1 asserts only `Role == Cell`, `Bounds.X == 0` and the width. `Bounds.X == 0` is also a default `Rect`'s X.
⭐ **Measured:** no Item row declares `DefaultHeight` (`FormControlCatalog.cs:948/:962/:969/:981`), so all four
inherit the record default **24** (`:508`) — **identical to MenuStrip's band height 24** (`:909`), but ToolStrip
is **25** (`:927`) and StatusStrip is **22** (`:941`). An implementer who writes `item.Definition!.DefaultHeight`
instead of the band's height, or lays every band's cells at `y = 0`, passes every Step 1 assertion on a MenuStrip
fixture. Task 25's render pin is a DISTINCTNESS hash and cannot see a wrong-but-unique drawing.
**FIX:** assert the FULL rect of one cell in a **ToolStrip** band and one in a **StatusStrip** band — the two
heights the inherited 24 cannot fake. Implement by taking `band.Y`/`band.Height` off the Band entry the same loop
just produced (`FormCanvasTransform.cs:370/:374`), never by re-reading any `DefaultHeight`.

### BLOCKER 6 — `Cells` as specified forces a second copy of the band stacking

`Bands()` (`:357-380`) uniquely owns where a band sits. A separate `Cells(document, selected)` must re-derive it.
This is the `SurfaceSize` / `Tracks`-vs-`ParseTracks` / `DockOf` lesson arriving a fourth time, and Blocker 5 is
exactly what makes the drift invisible.
**FIX:** yield cells from **inside** `Bands()`, so the band rect is computed once.

### ORDERING — corrected

`20 → 21 → 22 → 23 → 24`, **with Task 25's render pin pulled UP to immediately after Task 21.** It depends only
on 20+21, and leaving it at the end means the commit's only drawing code goes ungated across three tasks.

### INDIVISIBILITY — 24d is NOT 24c, and the honest reason matters

⭐ **24d modifies NOTHING under `BasicLang/Forms/`** — no catalog row, no reader, no writer, no emitter. No
document ever changes shape, so **no partial state can corrupt a saved form.** 24c's Task 12 caused measured data
loss (852 vs 880 bytes); 24d has no equivalent, and no gate is red by construction between tasks.
It therefore **can** honestly split, at **A = 20+21+22+25's pin** (the surface) / **B = 23+24** (the wiring).
**Recommendation: still ONE commit — but say why honestly.** Not "it would corrupt otherwise" (false), but
"the split costs a second full gate to isolate a tree nobody will run". If broken for review size, break at A/B
and state in the message that the intermediate tree has a dead slot.

### SEAMS THE TASKS NEVER MENTION

- ⛔ **`BringToFront`/`SendToBack` become reachable for items in 24d and reorder a shipped menu.**
  `FormDocument.MoveWithin` (`:193-213`) resolves siblings via `ListContaining`, so it reorders `host.Children` —
  which IS the `Items.Add` emission order 24c pinned with `InDocumentOrder`. Spec `:377` promises it; nothing
  tests it. First gesture that can silently reorder a menu.
- **`CutControls` (`:395-415`) for an item is untested** and writes the grid directly at `:413` — the second
  standing violation of the one-selection-store rule (`:466` is the other, and Task 23 edits that one).
- **A `Bottom`-docked host's dropdown has no rule.** Dock is `{Top,Bottom}` (`:905`, `:922`); Task 20's rule is
  one-directional, so a bottom-docked strip's rows land below the form — and `Render` has **no `PushClip`**, so
  they paint outside the form and are hit-testable there. Undecided in plan AND spec.
- **Delete/Escape while the editor is open is untested.** `OnKeyDown` reaches `Key.Delete` → `DeleteCommand` at
  `:406-416` whenever the canvas has focus. Spec `:339` names this as the reason the editor must focus on EVERY
  Begin. (Arrow-nudge is safe: the geometry switch's `default: changed = false` at `:461-463`.)
- ⛔ **`FormTrayViewTests.cs:87-91` contains a copied `Assert.Ignore`** — a gate that cannot find its file reports
  SUCCESS. Make it a hard fail before copying that fixture's shape into `FormStripCanvasTests`.
- ⚠ **Edit hazard:** `OnDesignModelRevisionChanged` (`:885`) is expression-bodied `=> Tray.Rebuild(DesignDocument);`.
  Converting it to a block for the cancel hook must KEEP the Rebuild, or the component tray empties on every edit
  (caught only by `FormTrayTests.PlacingAComponent_ShowsItInTheTray…`).

### ✅ CONFIRMED SOUND — do not re-litigate

- **The commit does not self-cancel.** `WriteDesignerEditBack` sets `_designFileText` inside the
  `_applyingDesignerEdit` guard (`:708`) and `OnTextChanged` skips cache invalidation while it is set (`:911`), so
  `DesignFile` returns the SAME `FormFile` and the same `FormControl` references and `IsInside`'s `ReferenceEquals`
  walk succeeds. Had the cache dropped, the editor would cancel itself on every Enter. No report had checked this.
- **Geometry-less controls are already handled** by `FormArrange.Apply` (`:54`, `:66`) and `AddIntrinsicRows`
  (`FormPropertyGridViewModel.cs:139-173`). Spec `:151`/`:152` hold as written.

---

### Task 20: Cells, dropdowns and Type Here slots in `Layout`

**Files:** `FormCanvasTransform.cs`; Tests: `FormStripLayoutTests`.

- [ ] **Step 1: Failing tests:** with the §3 document and `selected: menuStrip1` → after the band, cells for `mnuFile` (`Role == Cell`, `Bounds.X == 0`, width `8 + 7*len("&File") + 8` — the schematic width rule, `&` counted as written), then ONE `TypeHere` entry (`Control == null`) at `x = cell.Right` in the band; with `selected: mnuOpen` → `mnuFile`'s dropdown cells `[mnuOpen, sep1, mnuExit]` stacked below `mnuFile`'s cell (each 22 high; a separator 6 high) + `mnuFile`'s slot below them + `mnuOpen`'s own dropdown (empty) to the RIGHT of `mnuOpen`'s cell at the same y with its slot; the dropdown entries come LAST in the sequence; with `selected: null` → no `TypeHere` entry at all; `TypeHereAt(document, point, selected)` returns the host for a point in the slot and null elsewhere; `HitTest` on a dropdown cell returns the nested item; `ControlsIn` still excludes items.
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement** `Cells(document, selected)`: for each band, lay its top-level items left→right (`x += CellWidth(item)`; `CellWidth = item is Separator ? 6 : 8 + 7 * (Text ?? Id).Length + 8`); the ACTIVE strip (selected strip, or the selected item's root strip via a parent map built from `AllControls()`) gets a `TypeHere` entry after its cells; then the EXPANSION PATH: walk from the selected item up to the strip collecting item ancestors; expanded = ancestors ∪ {selected item if `IsHost`}; for each, outermost first: a dropdown at `(cell.X, cell.Bottom)` for a band cell or `(cell.Right, cell.Y)` for a nested cell, rows stacked, then the slot; yield all dropdown entries after every band/cell entry. The entry shape was declared in Task 17 with its `Host` field; a slot is `(null, rect, TypeHere, host)`, so Task 17's `Control != null` filters stay exactly as they are, and `TypeHereAt(document, canvasPoint, selected)` = the LAST entry with `Role == TypeHere` whose bounds contain the form point → its `Host`. Task 21's `TypeHereBounds` reads `Host` too. Rects, stated once so the drawing and the layout agree: a BAND cell is `new Rect(x, band.Y, CellWidth(item), band.Height)` — the band's full `DefaultHeight`, never a row height (Task 21's cell drawing and Task 25's per-item render pin both depend on it); a dropdown is as wide as its widest child cell (`CellWidth`), minimum 80 px; a slot is `CellWidth("Type Here")` = 79 px wide in a band (same y and height as a band cell) and the dropdown's width in a dropdown; dropdown rows are 22 px, a separator 6 px.
- [ ] **Step 4: Run** the fixture (+ Task 17's) → green.

### Task 21: The canvas — `TypeHereHost`, `TypeHereBounds`, `BeginTypeHereCommand`, cell/slot drawing, presses

**Files:** `FormCanvasControl.cs` (styled properties block `:34-215`, `OnPointerPressed` `:563-632`, `Render` `:1085-1160`, `DrawSchematic`); Create `VisualGameStudio.Tests/Shell/FormStripCanvasTests.cs`.

- [ ] **Step 1: Failing headless tests** (the `FormCanvasMultiSelectTests` rig: a `Window` hosting the canvas, `MouseDown/MouseUp` at canvas points computed from `FormCanvasControl.Fit` — read `FormCanvasMultiSelectTests.cs:47-89` for the exact helper; ⚠ that rig's `Centre` helper (`:33-38`) dereferences `FormCanvasTransform.BoundsOf(control, default)!.Value`, which is NULL for a strip or an item — an NRE that looks like a layout bug. Cell and slot press points come from the matching `FormCanvasTransform.Layout(doc, selected)` entry's `Bounds` (Role `Cell`/`TypeHere`, the `Host` for a slot) mapped through `FormCanvasControl.Fit(...).ToCanvas(...)`, and `Selection.Set(mnuFile)` is how the dropdown is opened before a dropdown cell is pressed): a press on `mnuFile`'s cell → `Selection.Primary == mnuFile` (give the canvas a `Selection`); a press on a dropdown cell (after selecting `mnuFile`) → the nested item; a double-click on a dropdown cell → `ActivateControlCommand` executed with it; a press on the Type Here slot (with the strip selected) → a `Recorder` bound to `BeginTypeHereCommand` fired with the host; with `TypeHereHost = menuStrip1`, after a render `TypeHereBounds` equals the canvas rect of the slot entry (compare against `Fit(...).ToCanvas(slot.Bounds)`); the frame with `mnuFile` selected differs from the frame with nothing selected (the dropdown paints) and the frame with `TypeHereHost` set differs again (the slot is highlighted).
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement:** styled properties `TypeHereHost` (`FormControl?`, in `AffectsRender`), `TypeHereBounds` (`Rect`, NOT in `AffectsRender`, set at the end of `Render` from the `TypeHere` entry whose host is `TypeHereHost`, else `default`), `BeginTypeHereCommand` (`ICommand?`). `Render`: iterate `Layout(document, SelectedControl)`; `Band`/`Control` → `DrawControl`; `Cell` → `DrawSchematic(item schematic)`; `TypeHere` → draw a greyed italic "Type Here" box (highlighted when `Control == TypeHereHost`). `OnPointerPressed`, left button, BEFORE `HandleUnder` (comment at the site: stricter than the spec's "before the marquee branch" on purpose — a selected LAST band cell's right edge coincides with the slot's left edge, and a handle test first would swallow a click 1–4 px into the slot): `var slotHost = _transform.TypeHereAt(document, point, SelectedControl); if (slotHost != null) { BeginTypeHereCommand?.Execute(slotHost); e.Handled = true; return; }`; a hit on a `Cell` entry selects through `ApplyClickSelection` and arms NO drag (guard `SelectedControl?.Geometry is PixelGeometry` before the move branch — items have none, so the existing early-returns already hold; pin it). Pass `SelectedControl` to all three `HitTest` calls (`:498`, `:583`, `:617`).
- [ ] **Step 4: Run** the fixture + every `FormCanvas*Tests` + `FormTrayViewTests` → green.

### Task 22: `FormTypeHereEditor` — the overlay control

**Files:** Create `VisualGameStudio.Shell/Controls/FormTypeHereEditor.cs`; Create `VisualGameStudio.Tests/Shell/FormTypeHereEditorTests.cs`.

- [ ] **Step 1: Failing tests:** a `Window` hosting the editor; set `SlotBounds = new Rect(30, 40, 120, 22)`, `IsActive = true` → the inner `TextBox.Bounds` (after layout, `window.UpdateLayout()`/a render) equals that rect and the box `IsFocused` (⚠ the control POSTS the focus call to the dispatcher, so the test calls `Dispatcher.UIThread.RunJobs()` before asserting); `window.KeyTextInput("Open")` then `window.KeyPress(Key.Enter, RawInputModifiers.None)` → a `Recorder` on `CommitCommand` fired once with `"Open"` and `Text` is cleared; `KeyPress(Escape)` → `CancelCommand` fired; the focus-on-every-Begin rule, tested the only way it can be seen: with the editor active and focused, focus ANOTHER control in the window (host a plain `Button` beside the editor and call `other.Focus()`; `RunJobs`; assert the box is NOT focused), then set `Host` to another object while `IsActive` stays true → `RunJobs` → the box is focused again (without moving focus away first the assertion passes whether or not the control re-focuses, and Task 25's mutant would not be a kill); `IsActive = false` → the editor is collapsed/hidden.
- [ ] **Step 2: Run** → compile error.
- [ ] **Step 3: Implement** a `TemplatedControl`-free `Panel` subclass (a `Canvas` with one `TextBox` child): styled `IsActive` (bool), `Host` (object?), `Text` (string, TwoWay to the box), `SlotBounds` (Rect), `CommitCommand`/`CancelCommand` (ICommand?). `OnPropertyChanged`: `IsActive` or `Host` changed while active → `IsVisible = IsActive; if (IsActive) Dispatcher.UIThread.Post(() => _box.Focus())`; `SlotBounds` changed → `Canvas.SetLeft/SetTop(_box, …)`, `_box.Width/Height`. ⛔ In the constructor: `_box.MinHeight = 0; _box.MinWidth = 0; _box.Padding = new Thickness(2, 0);` — both the headless app (`DesignerHeadlessApp.cs:32`) and the Shell (`App.axaml:12`) load `FluentTheme`, whose TextBox theme sets `MinHeight` 32 / `MinWidth` 64, and the layout clamp raises a 22px slot's box to 32px: without this the Step 1 bounds assertion reads 120x32 and the overlay overhangs the slot by 10px in the IDE. The Shell's own AXAML does the same for every small control it hosts (`CodeEditorDocumentView.axaml:359-397`, `ProblemsView.axaml:37-116`). `_box.KeyDown`: `Enter` → `CommitCommand?.Execute(_box.Text ?? "")`, `_box.Text = ""`, handled; `Escape` → `CancelCommand?.Execute(null)`, handled. `IsHitTestVisible` false when inactive so the canvas below receives clicks.
- [ ] **Step 4: Run** → green.

### Task 23: The view model — `StripEditor`, Begin/Commit/Cancel, the selection rule, paste into a host

**Files:** Create `VisualGameStudio.Shell/ViewModels/Designer/FormStripEditorViewModel.cs`; Modify `CodeEditorDocumentViewModel.cs` (ctor, `PasteControls` `:429-468`, `SelectInDesigner`); Create `VisualGameStudio.Tests/Compiler/FormStripEditorTests.cs` (VM-level, the `FormTrayTests` rig).

- [ ] **Step 1: Failing tests:** open a `.blform` with a MenuStrip (the §3 document without items, `MenuForm`); ⛔ the three methods are PUBLIC (`public void BeginTypeHere(FormControl? host)` etc. — a `[RelayCommand]` on a public method still generates the command; `PlaceControl` is the precedent) because the Shell grants no internals access to the tests; `vm.BeginTypeHere(menuStrip)` → `vm.StripEditor.IsActive`, `Host == menuStrip`, `Selection.Primary == menuStrip`; `vm.CommitTypeHere("&File")` → `fileToolStripMenuItem` under the strip with `Text == "&File"`, selected through `Selection` (and the grid), the document text contains it (undoable), editor still active with `Host == menuStrip`; `BeginTypeHere(fileToolStripMenuItem)` then `CommitTypeHere("&Open...")` → `openToolStripMenuItem` under it; `CommitTypeHere("-")` → `toolStripSeparator1`, a `ToolStripSeparator`; `CommitTypeHere("E&xit")` → `exitToolStripMenuItem`; the order is Open, sep, Exit; `FormCanvasTransform.Layout(doc, selected: exitToolStripMenuItem)` yields `fileToolStripMenuItem`'s dropdown cells, its slot, and `exitToolStripMenuItem`'s own slot to the right; `CommitTypeHere("-")` on a StatusStrip → refused via a `DesignerDiagnosticsEvent` BL8019 whose message contains `Separator` (the `PlaceItem` refusal names the kind: `not a ToolStripSeparator`) and nothing placed; `vm.DeleteControlCommand.Execute(openToolStripMenuItem)` → gone from the host's `Children` and from `vm.Text` (`ListContaining` walks nested children), and the editor is cancelled (any delete clears the selection through `SelectInDesigner(null)`, and a null primary is inside no host — the leave-rule fires; no host-specific cancel path exists or is needed); `CancelTypeHere()` → inactive; with the editor active on the strip, `vm.Selection.Set(theButton)` (the public store — `SelectInDesigner` is private) → the editor is cancelled; `vm.Selection.Set(fileToolStripMenuItem)` (inside the host) → still active; undo after a commit → the item is gone from `DesignDocument` and the editor is cancelled (the model was rebuilt); paste: copy `fileToolStripMenuItem`, select `menuStrip`, paste → a renamed item appended to the strip and it is the selection; paste a STRIP: copy `menuStrip`, paste → a renamed strip appended to `Controls` with `Geometry == null`, `Properties["Dock"]` kept, `TabIndex == 0`, and it is the selection (today's `PasteControls` `:451-462` happens to do this for any geometry-less non-component, so the Docked branch below is a restatement — this pin is what stops a later edit offsetting or renumbering it); select the Button, paste an item → refused-and-reported, nothing added, the selection unchanged.
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement.** `public partial class FormStripEditorViewModel : ObservableObject` (⚠ `partial`, or `[ObservableProperty]` generates nothing and `IsActive` does not exist) with `[ObservableProperty] bool _isActive; FormControl? _host; string _text = ""`. On the document VM:

```csharp
    public FormStripEditorViewModel StripEditor { get; } = new();

    [RelayCommand]
    public void BeginTypeHere(BasicLang.Forms.FormControl? host)
    {
        if (host?.Definition?.Items == null) return;
        SelectInDesigner(host);                 // the slot exists only for the selected strip/item's path
        StripEditor.Host = host;
        StripEditor.Text = "";
        StripEditor.IsActive = true;
    }

    [RelayCommand]
    public void CommitTypeHere(string? text)
    {
        var host = StripEditor.Host;
        var file = DesignFile;
        if (host == null || file == null || string.IsNullOrWhiteSpace(text)) return;

        var rule = host.Definition!.Items!;
        var kind = text.Trim() == "-" ? "ToolStripSeparator" : rule.Kinds[0];
        var result = ViewModels.Designer.FormPlacement.PlaceItem(file.Model, host, kind, text.Trim());
        if (result.Control == null) { ReportPlacementRefusal(result.Refusal ?? "the item could not be placed."); return; }

        WriteDesignerEditBack();
        SelectInDesigner(result.Control);
        StripEditor.Host = host;                // the parent stays the editor's host: type a whole menu in one run
        StripEditor.Text = "";
        StripEditor.IsActive = true;
    }

    [RelayCommand]
    public void CancelTypeHere() { StripEditor.IsActive = false; StripEditor.Host = null; StripEditor.Text = ""; }

    /// <summary>Whether <paramref name="control"/> is <paramref name="host"/> or lies inside it — the editor's "leave" test.</summary>
    private static bool IsInside(BasicLang.Forms.FormDocument document, BasicLang.Forms.FormControl? control, BasicLang.Forms.FormControl host)
    {
        for (var c = control; c != null; c = ViewModels.Designer.FormGeometryEdit.ParentOf(document, c))
        {
            if (ReferenceEquals(c, host)) return true;
        }
        return false;
    }
```

`FormGeometryEdit.ParentOf` (`FormGeometryEdit.cs:262`) is `private static` today — make it `internal static` (same assembly; no test needs it). In the ctor's `Selection.Changed` handler (`:864` — an expression-bodied lambda today; make it a block lambda), after the grid update:

```csharp
            var model = DesignFile?.Model;
            if (StripEditor.IsActive && StripEditor.Host is { } host && model != null &&
                !IsInside(model, Selection.Primary, host))
            {
                CancelTypeHere();
            }
```

Cancel in `OnDesignModelRevisionChanged` when the host is no longer in the document (`DesignDocument?.FindById(host.Id)` is not the same reference — an undo re-parsed the model). `PasteControls`: for a pasted root whose row is `Item`: `var host = Selection.Primary; if (host?.Definition?.Items?.Accepts(control.Kind) == true) { host.Children.Add(control); added.Add(control); } else { ReportPlacementRefusal(...); continue; }`; a pasted `Docked` root → `Controls`, no offset; ⚠ collect the controls ACTUALLY added into `added` and pass THAT to `Selection.SetRange` (`:465` passes every pasted root, which would select a refused one).
- [ ] **Step 4: Run** the fixture + `FormTrayTests` + `FormDesignerCommandTests` → green.

### Task 24: Wire the view — AXAML, gate

**Files:** `Views/Documents/CodeEditorDocumentView.axaml` (`:223-235`) only — ⚠ NO code-behind change: the editor is a bound control (`CommitCommand`/`CancelCommand`) and the canvas raises `BeginTypeHereCommand`, so there is no handler to write and the gate asserts AXAML attributes only (spec §7's "handler name" wording predates the control-based design; Task 29's §10 records it). Tests: `FormTrayViewTests`-style AXAML gate in `FormStripCanvasTests` (or a new `FormStripViewTests`).

- [ ] **Step 1: Failing gate:** parse the AXAML; the `FormCanvasControl` element has `TypeHereHost="{Binding StripEditor.Host}"` and `BeginTypeHereCommand="{Binding BeginTypeHereCommand}"`; a `designer:FormTypeHereEditor` element in the SAME `Grid.Row="0"` cell (after the canvas, so it paints above) with `x:Name="TypeHereEditor"`, `IsActive="{Binding StripEditor.IsActive}"`, `Host="{Binding StripEditor.Host}"`, `Text="{Binding StripEditor.Text, Mode=TwoWay}"`, `SlotBounds="{Binding #DesignCanvas.TypeHereBounds}"`, `CommitCommand="{Binding CommitTypeHereCommand}"`, `CancelCommand="{Binding CancelTypeHereCommand}"` — assert each by attribute; the canvas element gains `x:Name="DesignCanvas"`.
- [ ] **Step 2: Run** → red. **Step 3:** edit the AXAML as asserted (namespace `designer:` already maps to `VisualGameStudio.Shell.Controls`). **Step 4:** `dotnet clean VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release` then build; run the gate → green. Launch the IDE once from `VisualGameStudio.Shell\bin\Release\net8.0\VisualGameStudio.exe`, open a `.blform`, drop a MenuStrip, type `&File` Enter `&Open...` Enter — record what you saw in the commit message (this is the owner's acceptance surface; the tests above are the gate).

### Task 25: Per-item render pin, gate, commit 24d

- [ ] `FormCanvasRenderTests.EveryItemKind_ChangesItsHostsFrame`: for each `Place == Item` row, `RenderHash(Canonical(host with item, SharedId, hostId: SharedId, item Text "X"))` ≠ `RenderHash(host alone)` (cells are laid out for every band unconditionally in Task 20, so no selection is needed). Mutant: blank the `MenuItem` arm in `DrawSchematic` → the pin fails.
- [ ] The Task 21 bounds test must see TWO slots to discriminate: select `mnuFile` (so the strip's slot AND `mnuFile`'s dropdown slot are yielded) and set `TypeHereHost = mnuFile`; assert `TypeHereBounds` is the DROPDOWN slot's canvas rect — the "wrong entry" mutant then fails it.
- [ ] Build (after `dotnet clean` Shell); fast subset; `FormCanvasRenderTests`; `FormSchematicPinTests`; `FormTypeHereEditorTests`; `FormStripCanvasTests`; `FormStripEditorTests`. Mutants: `BeginTypeHere` not selecting the host → the slot test; commit selecting nothing → the "selected through Selection" assertion; the cancel-on-leave handler removed → its test; the editor not re-focusing on `Host` change → the editor test; `TypeHereBounds` computed from the wrong entry → the bounds test; the slot press placed after the marquee branch → the slot press test (for a BAND slot the band entry contains the point, so `HitTest` returns the strip and it is merely selected — the `Recorder` bound to `BeginTypeHereCommand` never fires; a marquee appears only for a dropdown slot over empty surface).
- [ ] Commit `feat(designer): Task 24d — the Type Here strip: cells, dropdowns, the in-place editor`.

---

# Commit 24e — retarget, acceptance, records

### Task 26: The retarget rule

**Files:** `FormRetarget.cs` (`Place` `:584-676`), `FormRetargetTests.cs`.

- [x] **Step 1: Failing tests:** `ToWinForms_AStripAndItsItems_CrossGeometryLess_WithDock` (a `.blwebform` with a MenuStrip+items and a Button in a cell → the strip has `Geometry == null`, `Properties["Dock"] == "Top"`, items nested with null geometry, the Button placed in pixels, no BL8025 naming the strip or an item); `ToWinForms_APageWhoseTopLevelIsAStripAlone_DoesNotThrow`; `ToWeb_AStripCrosses_AndTheRetargetedPageIsChrome` (through `ConvertToPair`, the HTML has `<nav` before the div, AND every crossed item has `Geometry == null` — `DeriveCells` (`FormRetarget.cs:536`) recurses into a strip's items and nulls their geometry through its else-branch (`:533`) with no warning; pin that branch rather than assume it); the sweep (`Canonical` for every Docked/Item row, both directions) → `Locate` finds the crossed control, geometry null, no BL8025 for it.
- [x] **Step 2: Run** → red for the RIGHT reason: today the strip simply receives a `PixelGeometry` and a BL8025 (`Place` sizes every sibling, `:593-611`; a lone strip is one `sizes` entry so `Max` succeeds) — the geometry-null and no-BL8025 assertions fail. ⚠ The `InvalidOperationException` (`Max` on empty) appears only once the `positioned` filter exists WITHOUT its early return — which is why Step 3 has one.
- [x] **Step 3: Implement** in `Place`: `var positioned = siblings.Where(c => c.Definition?.Place is null or FormPlace.Positioned).ToList(); if (positioned.Count == 0) return (0, 0);` BEFORE `Max` is reached, and use `positioned` everywhere `siblings` was used (sizes, pitch, the placement loop); never recurse into a Docked/Item control's children.
- [x] **Step 4: Run** `FormRetargetTests`, `DesignRetargetCliTests`, `SolutionExplorerRetargetTests` → green.

### Task 27: The node harness learns a form name and a target element

**Files:** `FormDesignerAcceptanceTests.cs:270-336`; callers `FormComponentAcceptanceTests.cs:250`, `FormRetargetPairTests.cs:293`.

- [x] `internal static string? RunPageUnderNode(string outDir, string formName = "LoginForm", string? clickId = null)`: `body.setAttribute("data-form", "{{formName}}")`; the click loop becomes `clickId == null ? every element : els.get("{{clickId}}")?.click()`. Existing callers unchanged (defaults). Run `FormDesignerAcceptanceTests`, `FormComponentAcceptanceTests`, `FormRetargetPairTests` → green.

### Task 28: Acceptance — a menu built by the designer RUNS on both targets

**Files:** Create `VisualGameStudio.Tests/Compiler/FormMenuAcceptanceTests.cs` (`[Category("Integration")]`, `[NonParallelizable]`; copy `FormComponentAcceptanceTests`' scaffolding: temp dir, `FormScaffolder.Create("MenuForm", target)`, the VM `Open`, `SaveAsync`, the CLI, the csproj+driver, `RunPageUnderNode`).

- [x] **Build the form through the designer's OWN commands:** `vm.PlaceControl("MenuStrip", 0, 0)` (⚠ the designer mints `MenuStrip1` — PascalCase, `NextId` seeds `kind + "1"` like `Button1`/`Timer1`; every strip-id assertion below uses `MenuStrip1`/`ToolStrip1`/`StatusStrip1`, and HTML ids are case-sensitive) → `BeginTypeHere(strip)` → `CommitTypeHere("&File")` → `BeginTypeHere(fileToolStripMenuItem)` → `CommitTypeHere("&Open...")`, `CommitTypeHere("-")`, `CommitTypeHere("E&xit")`; `PlaceControl("ToolStrip", 0, 0)` → `BeginTypeHere(toolStrip)` → `CommitTypeHere("Open")`; `PlaceControl("StatusStrip", 0, 0)` → `BeginTypeHere(statusStrip)` → `CommitTypeHere("Ready")` (⛔ every strip needs its own `BeginTypeHere`: `PlaceControl` selects the new strip, and the Task 23 leave-rule cancels the editor because the new strip is not inside the previous host — a commit without a Begin places nothing); `await vm.ActivateControlCommand.ExecuteAsync(openToolStripMenuItem)` (⛔ AWAIT it — it is an async relay command and all three existing callers await it, e.g. `FormComponentAcceptanceTests.cs:90`; `.Execute` fires-and-forgets and the stub may not exist yet), insert `Console.WriteLine("CLICK")` into the stub exactly as the Timer test inserts its line; `await vm.SaveAsync()`.
- [x] **WinForms:** CLI `--target=csharp`; the C# contains `openToolStripMenuItem = new ToolStripMenuItem()` and `fileToolStripMenuItem.DropDownItems.Add(openToolStripMenuItem)`; driver (namespace `GeneratedCode`, `new MenuForm()`, `Show()`, then reflection-free: `form.MainMenuStrip.Items` captions joined → prints `MENU &File`; `((ToolStripMenuItem)form.MainMenuStrip.Items[0]).DropDownItems` → prints `DROP ToolStripMenuItem:&Open...,ToolStripSeparator:,ToolStripMenuItem:E&xit`; `PerformClick()` on item 0 → `CLICK`; the StatusStrip's first item text → `STATUS Ready`; `DONE`). Assert all five, in order. (Click target corrected from the plan's literal wording: `File`'s `DropDownItems[0]`, not the menu bar's `Items[0]` — see spec §10.)
- [x] **Web:** RUNS since the master merge `8bcd631b` (2026-09-25). Before it, `basiclang build` of a web project died with `ERROR_USER_MAPPED_FILE` on a fresh `App.js` on this machine; master's write-once/rename JS-emitter fix cleared it, as predicted. Measured on the merge: `FormMenuAcceptanceTests` + the three other acceptance fixtures 14/14, none skipped.
- [x] Both must PASS, not be skipped: read the totals. Mutants: reverse the host-verb loop → `DROP` order fails; drop `MainMenuStrip` → the driver NREs (`form.MainMenuStrip` null) — pinned; strip the `<ul>` wrapper → the HTML assertion. (WinForms half only — see the Web bullet above.)

### Task 29: Records

- [x] Spec: extend "§10 — What building it changed" (opened in 24c's Task 19 with the four-field layout entry — do not write that row twice): the Type Here gate asserts AXAML bindings only (no code-behind handler exists to name); anything else a task above changed.
- [x] `docs/form-designer-followups.md`: filed as **28–33**, not 22–26 as written below — those numbers were already taken by entries filed during 24c/24d (the shared-property-field gate, the web-event case bug, the dead accelerator regex). **28** `ShortcutKeys` (M6/M7: only `CType(n, Keys)`; needs a Keys editor + name table), **29** array-literal common-base widening for BasicLang classes (M1/M3: cannot serve WinForms types), **30** the C++ capability checker's missing `IRArrayAlloc` arm, **31** `RejectImpossibleConversion`'s sibling-file hole (spec Decision 14; chip `task_0b7436a5` already filed — id recorded), **32** a bare literal (any side-effect-free expression) as a STATEMENT parses silently — `ParseAssignmentOrExpressionStatement` accepts any expression, so `New Integer()` with its `{1, 2}` on the NEXT line is a constructor call plus a vanished initializer with no diagnostic (measured while doing Task 1; chip `task_2e1de6b3`). **33** (new, not in this plan's original list): designer captions still show `&` literally and clip below 1:1 zoom, no captions at all below zoom ≈0.42.
- [x] `CLAUDE.md` form-designer section: one ⛔ bullet — a strip is `Place == Docked` (geometry-less, `Dock` PROPERTY, a band on the canvas, page chrome on the web), an item is `Place == Item` under its host and is added by the HOST row's verb in DOCUMENT order (reversing it is the silent failure), `FormCatalogShapes.Canonical` is the one fixture shape for every catalog gate, and the compiler now has `New T() {…}` (parens required, three policies, the JS array arms). Plus the compiler section: the typed literal and its three policies in one line.
- [x] `docs/HANDOFF.md`: task table (24 done), a Task 24e section (measured facts, the five commits, gates), the gates table rows.
- [x] Memory: `MEMORY.md` START HERE block (the session's topic file `form-24d.md` holds the history).
- [x] Tick every box in this plan.

### Task 30: Gate and commit 24e, then the IDE drop

- [x] Build; fast subset; `FormRetargetTests`; `FormMenuAcceptanceTests`; `FormDesignerAcceptanceTests`; `FormComponentAcceptanceTests`; then the FULL SUITE on the final binaries (both streams logged), failure NAMES vs the 8-row baseline, zero new. — Done on the MERGE with master rather than on 24e alone (the merge is what ships); see `docs/HANDOFF.md` "Task 24e → the master merge" for the four merges, their gates and every failure name.
- [x] Commit `feat(designer): Task 24e …` — `ec9356f4`, pushed and SHA-verified.
- [ ] IDE drop: `robocopy VisualGameStudio.Shell\bin\Release\net8.0 IDE /E` (never `/MIR`); verify `IDE\BasicLang.exe --help` and `new --list` exit 0 (run `new --list` to a file, never through `Select-Object -First`), `IDE\lib\js\dom-core.bli` present, `IDE\BasicLang.exe` starts with `MZ`; commit `chore(ide): refresh the IDE drop with menus, toolbars and status bars`; push; SHA-verify.
