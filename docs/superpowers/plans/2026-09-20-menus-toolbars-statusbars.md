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
- Modify `BasicLang/JavaScriptBackend.cs` — `Visit(IRArrayAlloc)` / `Visit(IRArrayStore)` (`:2379-2380`), `Expr` default arm (`:747-748`).
- Modify `BasicLang/ASTPrettyPrinter.cs` — `Visit(CollectionInitializerNode)` (`:827`).
- Create `VisualGameStudio.Tests/Compiler/TypedArrayLiteralTests.cs` (parser + analyzer + C# emission + untyped pins), `VisualGameStudio.Tests/Compiler/TypedArrayLiteralExecutionTests.cs` (`[Category("Integration")]`: C# run, C++ run, JS run, the M4/M3/two-file CLI rows).

**24b — the shape, no rows**
- Modify `BasicLang/Forms/FormControlCatalog.cs` — `FormPlace` enum, `FormItemRule` record, `FormControlDef.Place`/`Items`/`FormProperty`/`HtmlChildrenWrapper`/`HtmlRole`/`WebCss`, `IsComponent` derived; seven `FormSchematic` values.
- Create `VisualGameStudio.Tests/Compiler/FormCatalogShapes.cs` — `Canonical`, `Locate` (test helper, `internal static`).
- Modify the gates: `WinFormsCatalogSweepTests.cs` (three builders), `FormCanvasRenderTests.cs` (fixture + Item exclusion), `FormRetargetTests.cs` (sweep), `FormPropertyGridTests.cs:552-568`, `FormToolboxGlyphTests.cs:75-89`, `FormDocumentTests.cs:80-92`.
- Modify `VisualGameStudio.Shell/Controls/FormCanvasControl.cs` — extract `DrawSchematic(context, schematic, bounds, label, face, client, ink)` seam from `DrawControl` (~`:1420-1890`); `FormToolboxViewModel.cs` — `GlyphFor` arms, `Rebuild` excludes `Place == Item`.
- Create `VisualGameStudio.Tests/Shell/FormSchematicPinTests.cs` — enum-driven pairwise frame hash + glyph pins.

**24c — rows, format, emission, bands**
- Modify `FormControlCatalog.cs` (seven rows), `DesignDiagnostic.cs` (BL8030), `Serialization/FormDocumentReader.cs`, `Serialization/FormDocumentWriter.cs`, `FormDocument.cs` (`FormClipboard`, `RenumberTabIndexes`), `RegionWriter.cs`, `FormAssetEmitter.cs`, `Recognizer/WinFormsDialect.cs`; `FormCanvasTransform.cs` (`Layout(document, selected)`, bands), `FormCanvasControl.cs` (band arms), `FormPlacement.cs` (Docked branch, `PlaceItem`), `FormToolboxViewModel.cs` (category), `FormPropertyGridViewModel.cs`, `CodeEditorDocumentViewModel.cs` (`TrayDrop`/canvas drop refusal of an Item kind).
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

- [ ] **Step 1: Write the failing parser tests**

```csharp
using BasicLang.Compiler;
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

    [TestCase("Dim items() As Integer = New Integer {1, 2}", "New Integer() {")]
    [TestCase("Dim items() As Integer = New Integer(2) {1, 2, 3}", "the initializer sets the size")]
    [TestCase("Dim items As New Integer() {1, 2}", "Dim items() As Integer = New Integer() {")]
    [TestCase("Dim items As New Integer {1, 2}", "Dim items() As Integer = New Integer() {")]
    public void TheThreeRefusals_NameTheFix(string body, string expectedInMessage)
    {
        Parse(body, out var parser);
        Assert.That(parser.Errors, Is.Not.Empty, "expected a parse refusal");
        Assert.That(string.Join("\n", parser.Errors.Select(e => e.Message)), Does.Contain(expectedInMessage));
    }
}
```

⚠ Check the real AST node names first: `Grep "class ProgramNode|class SubroutineNode|class VariableDeclarationNode" BasicLang/ASTNodes.cs` — the test uses whatever the tree's actual root/sub/declaration types are; adjust the two casts, not the assertions. ⚠ Check how `Parser.Errors` exposes messages (`ParserErrorTests.cs:30-31` uses `parser.Errors[0].Message`).

- [ ] **Step 2: Run to verify it fails**

`--filter "FullyQualifiedName~TypedArrayLiteralTests"` → the first two fail (a `NewExpressionNode` comes back / errors present), the refusal cases fail on the message text.

- [ ] **Step 3: Implement the expression path** in `Parser.cs` inside `if (Match(TokenType.New))` (`:4280`). After the `if (Match(TokenType.LeftParen)) { … }` block and before `return newExpr;`:

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
                            $"{newExpr.Type.Name}() {{…}}`", Peek(), "Add `()` after the type name.");
                    }

                    if (newExpr.Arguments.Count > 0)
                    {
                        throw new ParseException(
                            "An array creation with an initializer takes its size from the initializer: " +
                            $"write `New {newExpr.Type.Name}() {{…}}` (VB's `New T(n) {{…}}` names an upper " +
                            "bound, and this compiler's sizes are element counts)", Peek(),
                            "Remove the size.");
                    }

                    return ParseTypedCollectionInitializer(newExpr.Type);
                }

                return newExpr;
```

where `sawParens` is a `bool` set to true inside the existing `if (Match(TokenType.LeftParen))` block (declare `var sawParens = false;` before it), and:

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

⚠ Look at how the existing `New` branch reports errors (`throw new ParseException(...)` at `:4464` is the shape in `ParsePrimary`) and match the constructor arity you find.

- [ ] **Step 4: Implement the `Dim … As New` refusal** at `:2504-2525`: AFTER the closing brace of `if (Match(TokenType.LeftParen)) { … }` (so it fires with or without parentheses) and before `node.Type = newExpr.Type;`:

```csharp
                    // Task 24a: VB has no `Dim x As New T() {…}`; the array form is `Dim x() As T = New T() {…}`.
                    if (Check(TokenType.LeftBrace))
                    {
                        throw new ParseException(
                            $"`Dim {node.Name} As New {newExpr.Type.Name}() {{…}}` is not a form: write " +
                            $"`Dim {node.Name}() As {newExpr.Type.Name} = New {newExpr.Type.Name}() {{…}}`",
                            Peek(), "Move the initializer after `=`.");
                    }
```

- [ ] **Step 5: Run** the fixture → all green. Run `--filter "FullyQualifiedName~ParserErrorTests|FullyQualifiedName~CompilationTests"` → still green (the bare-brace path at `:4446` is untouched).

### Task 2: The analyzer — typing a typed literal

**Files:** Modify `BasicLang/SemanticAnalyzer.cs` `Visit(CollectionInitializerNode)` (`:6637-6667`); Test: `TypedArrayLiteralTests.cs`.

- [ ] **Step 1: Write the failing analyzer tests** (append to the fixture). Helper:

```csharp
    private static (bool ok, List<string> errors, List<string> warnings, TypeInfo? type) Analyze(string body, string prelude = "")
    {
        var source = prelude + "\nSub Main()\n" + body + "\nEnd Sub";
        var parser = new Parser(new Lexer(source).Tokenize());
        var program = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(program);
        var init = FirstInitializer(program);   // adapt: last statement's initializer for multi-line bodies
        return (ok, analyzer.Errors.Select(e => e.Message).ToList(), analyzer.Warnings.Select(w => w.Message).ToList(),
                analyzer.GetNodeType(init) as TypeInfo);
    }
```

⚠ Confirm `analyzer.Warnings` exists (grep `public .*Warnings` in SemanticAnalyzer.cs) — else assert on the diagnostics collection it does expose.

Tests (one `[Test]` each, names as given):
- `TypedLiteral_OfTwoUnresolvableNetTypes_IsTypedByT_WithNoWarning`: prelude `Using System.Windows.Forms`, body `Dim a As ToolStripMenuItem = Nothing\nDim s As ToolStripSeparator = Nothing\nDim items() As ToolStripItem = New ToolStripItem() {a, s}` → `ok`, no warning containing "mixed types", type name `ToolStripItem[]`, `ElementType.Name == "ToolStripItem"`.
- `TypedLiteral_OverBasicLangClasses_Widens`: prelude `Class Shape\nEnd Class\nClass Circle\nInherits Shape\nEnd Class\nClass Square\nInherits Shape\nEnd Class`, body `Dim c As New Circle()\nDim s As New Square()\nDim all() As Shape = New Shape() {c, s}` → ok.
- `TypedLiteral_RefusesAnUnrelatedElement`: same prelude, body `Dim x As String = "a"\nDim all() As Shape = New Shape() {x}` → error containing `cannot put a 'String' in a 'Shape()'`.
- `TypedLiteral_RefusesNarrowing_ForNonLiterals`: `Dim d As Double = 1.5\nDim l As Long = 1\nDim a() As Integer = New Integer() {d}` → error `'Double' in an 'Integer()'`; and `{l}` → `'Long'`.
- `TypedLiteral_NumericLiteralRule`: admitted — `New Byte() {65}`, `New Long() {1}`, `New Double() {1}`, `New Decimal() {1.5}`, `New Single() {1.5}`; refused — `New Integer() {1.5}` (error names Double→Integer), `New Byte() {300}` (message contains `not representable` — the BC30439 text `CheckConstantFitsNumericTarget` emits; read its message at `:1633-1665` and assert a stable fragment).
- `TypedLiteral_Nothing`: `New String() {"a", Nothing}` and (prelude Shape) `New Shape() {c, Nothing}` admitted; `New Integer() {Nothing}` refused with `Nothing has no value of type 'Integer'`.
- `TypedLiteral_SyntheticGenericElement_IsAccepted`: prelude Shape; body `Dim l As New List(Of Integer)()\nDim all() As Shape = New Shape() {l}` → ok (csc decides).
- `UntypedLiteral_IsUnchanged`: `Dim a() As Integer = {1, 2, 3}` → type `Integer[]`; `Dim o() As Object = {1, "a"}` → type `Object[]` AND a warning containing `mixed types`.

- [ ] **Step 2: Run** → all fail (the typed cases come back `Object[]`/warned/wrongly refused).

- [ ] **Step 3: Implement.** Replace the body of `Visit(CollectionInitializerNode)`:

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
                    Error($"Nothing has no value of type '{target.Name}'; write a value", element.Line, element.Column);
                }
                return;
            }

            if (element is LiteralExpressionNode literal && elementType != null && elementType.IsNumeric() && target.IsNumeric())
            {
                if (target.Name == "Decimal" && TryRetypeLiteralToDecimal(element, target))
                {
                    return;
                }

                if (elementType.IsFloatingPoint() && target.IsIntegral())
                {
                    Error($"cannot put a '{elementType.Name}' in a '{target.Name}()' — a floating literal never " +
                          "narrows into an integral array; write an integer or change the element type",
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

            Error($"cannot put a '{elementType.Name}' in a '{target.Name}()'", element.Line, element.Column);
        }

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

- [ ] **Step 4: Run** the fixture → green. Run `--filter "FullyQualifiedName~CompilationTests|FullyQualifiedName~CppCollectionTests|FullyQualifiedName~ReturnCoercionTests"` (bare-literal users) → green.

### Task 3: The IR builder — coerce each element

**Files:** Modify `BasicLang/IRBuilder.cs:1893-1922`.

- [ ] **Step 1: Write the failing test** in `TypedArrayLiteralTests` — `TypedLiteral_Lowering_RetypesALiteral_AndCastsANonLiteral`: build the IR for `Dim i As Integer = 1\nDim a() As Double = New Double() {1, i}` (`new IRBuilder(analyzer).Build(program, "T")`, as `BclE2E.CompileToCppOptimized` does at `CppBclEndToEndTests.cs:49-54`), find the `IRArrayAlloc` in Main's blocks, assert `ElementType.Name == "Double"`; find the two `IRArrayStore`s: the first's `Value` is an `IRConstant` whose `Type.Name == "Double"` (re-typed in place), the second's `Value` is an `IRCast` (a non-literal is wrapped). ⚠ Discover the block/instruction accessors from `IRPrettyPrinter.cs` or an existing IR test (grep `IRArrayStore` in the Tests tree).

- [ ] **Step 2: Run** → fails (both stores are raw).

- [ ] **Step 3: Implement.** In `Visit(CollectionInitializerNode)` (`:1902-1906`), replace the element loop:

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

- [ ] **Step 4: Run** → green.

### Task 4: The JavaScript backend — the missing array arms

**Files:** Modify `BasicLang/JavaScriptBackend.cs:2379-2380` and the `Expr` switch (`:747-748`); Test: `TypedArrayLiteralExecutionTests.cs` (new, `[Category("Integration")]`).

- [ ] **Step 1: Write the failing tests**

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
    private const string SumDouble = "Sub Main()\nDim i As Integer = 2\nDim a() As Double = New Double() {1, i}\nDim total As Double = 0\nFor Each x As Double In a\ntotal = total + x\nNext\nConsole.WriteLine(\"SUM \" & total)\nEnd Sub";

    [TestCase(SumTyped, "SUM 6"), TestCase(SumBare, "SUM 6"), TestCase(SumDouble, "SUM 3")]
    public void JavaScript_RunsTheLiteral(string source, string expected)
    {
        RequireNode();
        Assert.That(JavaScriptExecutionTests.RunJs(source), Is.EqualTo(expected));
    }

    [TestCase(SumTyped, "SUM 6"), TestCase(SumDouble, "SUM 3")]
    public void CSharp_RunsTheLiteral(string source, string expected)
    {
        var csharp = WinFormsCatalogSweepTests.CompileToCSharp(source);   // the real CLI, optimizer on
        Assert.That(VisualGameStudio.Tests.Native.CSharpRun.CompileAndRun(csharp).Trim(), Is.EqualTo(expected));
    }

    [TestCase(SumTyped, "SUM 6"), TestCase(SumDouble, "SUM 3")]
    public void Cpp_RunsTheLiteral(string source, string expected)
    {
        Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source)).Trim(), Is.EqualTo(expected));
    }
}
```

⚠ `WinFormsCatalogSweepTests.CompileToCSharp` writes a `SweepForm.bas` and compiles it; its output file is `SweepForm.cs` — fine for a Module-less `Sub Main`; if the CLI needs a class, wrap in `Public Class Prog … Public Shared Sub Main()` as the JS scratch measurement did. ⚠ `JavaScriptExecutionTests.RunJs` is `internal static` and `Assert.Ignore`s without node — `RequireNode()` runs first so this fixture fails instead. ⚠ `BclE2E.CompileRun` ignores without a C++ compiler; this box has MSVC.

- [ ] **Step 2: Run** the JS rows → `NotSupportedException … IRArrayAlloc` (the arm that has never run). The C#/C++ rows should already pass (the lowering is shared) — if the C# row fails on `New Double()`, Task 3's coercion is wrong.

- [ ] **Step 3: Implement** in `JavaScriptBackend.cs`. Replace the two `NotYet` visitors (`:2379-2380`):

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

⚠ `Bind(IRValue, string)` exists at `:1513`; it declares a `const` when the name is not a declared local, or assigns when it is (`:1516-1522`) — which is what makes `Dim a() As Integer = {…}` work whether or not `TryRenameToVariable` renamed the alloc temp to `a`. If the JS row still fails, print the generated JS (`JsTestSupport.Compile(source)`) and read it before touching anything else.

- [ ] **Step 4: Run** the fixture → green on all three backends. Run `--filter "FullyQualifiedName~JavaScriptExecutionTests|FullyQualifiedName~JavaScriptArrayTests"` → still green.

### Task 5: The M4 / M3 / two-file rows through the real CLI and csc

**Files:** `TypedArrayLiteralExecutionTests.cs`.

- [ ] **Step 1: Write the failing tests**

- `WinForms_TheVsIdiom_BuildsUnderCsc_AndRuns`: the M4 program (`scratchpad t24/M4/F.bas`, class renamed `MenuForm`, plus a `Public Sub Poke()` printing `menuStrip1.Items.Count`) → `WinFormsCatalogSweepTests.CompileToCSharp(source)` (assert the C# contains `new ToolStripItem[2]`) → `WinFormsCompile.AssertCompiles(csharp, …)`; then a dotnet-build + run exactly as `FormComponentAcceptanceTests.cs:146-204` does (copy its csproj/driver text; `new MenuForm()`, `form.Poke()`), asserting the run prints `ITEMS 2`.
- `WinForms_TheDeclaredArrayShape_BuildsUnderCsc`: the M3 shape `Dim items() As ToolStripItem = New ToolStripItem() {mnuFile, sep}` then `AddRange(items)` → `CompileToCSharp` + `WinFormsCompile.AssertCompiles`.
- `TwoFiles_ASiblingClass_IsNotExemptedAsANetType`: write `Shape.bas` (`Public Class Shape\nEnd Class`) and `Main.bas` (`Public Class Prog\nPublic Shared Sub Main()\nDim s As String = "a"\nDim all() As Shape = New Shape() {s}\nEnd Sub\nEnd Class`) into a temp dir, run `CliTestHarness.RunProcess(CliTestHarness.CliPath(), new[] { "Main.bas", "Shape.bas", "--target=csharp" }, dir, 120_000)` (⚠ check how the CLI takes several files — `Program.cs` usage text, or a `.blproj`; `CliTestHarness.RunCli` accepts params args) → exit ≠ 0 and stdout contains `cannot put a 'String' in a 'Shape()'`.

- [ ] **Step 2: Run** → the first two fail today only if Tasks 1–3 are incomplete; the two-file row fails if the predicate was spelled without `IsUserDefinedTypeName`. All three must be green at the end.

- [ ] **Step 3: Mutants (kill each, revert each):** (a) parser: drop the `Arguments.Count > 0` refusal → `TheThreeRefusals` fails; (b) analyzer: replace `WidensTo` with `target.IsAssignableFrom` → `RefusesNarrowing` fails; (c) analyzer: drop `!IsUserDefinedTypeName` from the predicate → the two-file row fails; (d) IR: drop the coercion → the lowering test fails; (e) JS: make the `Expr` arm always render `new Array(n)` → the C++/C# rows stay green and `JavaScript_RunsTheLiteral(SumTyped)` prints `SUM 0` — this is the "second empty array" trap; (f) JS: remove the `IRArrayStore` arm → `SumBare` throws.

### Task 6: Pretty printer, gate, commit 24a

- [ ] **Step 1:** `ASTPrettyPrinter.Visit(CollectionInitializerNode)` (`:827`): when `node.ElementType != null`, write `CollectionInitializer New {ElementType.Name}() ({n} elements):`. Pin with one test in `TypedArrayLiteralTests` if a pretty-printer test fixture exists (grep `ASTPrettyPrinter` in Tests); otherwise leave it to the untyped format and skip.
- [ ] **Step 2: Gate.** Build; fast subset; `TypedArrayLiteralExecutionTests`; then the FULL SUITE (`dotnet test … -c Release --no-build --logger "console;verbosity=normal" > $env:TEMP\bl-t24a-full.log 2>&1`, both streams), compare failure NAMES with the 8-row baseline (2 `SearchSnippets`, `Cli_Build_CppProject_ProjectReference_WarnsAndStillSucceeds`, 4 game-template rows, `NonEx_variants_marshal_and_are_screen_size_dependent`). Zero new, or stop.
- [ ] **Step 3: Commit** `feat(compiler): New T() { … } array creation with initializer` — message via a scratch file: the measured facts (M1–M4 and the JS "successful, no output" measurement), the three policies, the JS arms, the gates with totals, the trailer.

---

# Commit 24b — the shape, and every gate learns it (spec §1, §2 "Gates" row, Decision 12)

### Task 7: `FormPlace`, `FormItemRule`, and the derived `IsComponent`

**Files:** Modify `BasicLang/Forms/FormControlCatalog.cs` (`FormControlDef` `:405-455`, `FormSchematic` `:274-370`); Test: `VisualGameStudio.Tests/Compiler/FormCatalogCoverageTests.cs`.

- [ ] **Step 1: Failing tests** (append to `FormCatalogCoverageTests`):
  - `EveryRow_HasAPlace_AndIsComponentIsDerived`: for every row, `def.IsComponent == (def.Place == FormPlace.Tray)`; the four tray kinds are `Tray`; every other row today is `Positioned`.
  - `FormSchematic_HasTheSevenStripValues`: `Enum.GetValues<FormSchematic>()` contains `MenuBar, ToolBar, StatusBar, MenuItem, Separator, ToolButton, StatusLabel` and no row uses them yet (`FormControlCatalog.All.All(d => !new[]{…}.Contains(d.Schematic))`).
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

Then on `FormControlDef`: remove the positional `bool IsComponent = false` parameter (all four rows pass it BY NAME — `:757/:779/:789/:797` — so change those four to `Place: FormPlace.Tray`), add after `WebScript`:

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
        PixelGeometry? geometry = null)
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
                    Geometry = geometry ?? new PixelGeometry { X = 96, Y = 80, Width = 120, Height = 24, Anchor = "Top" }
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

⚠ Keep the property sweep's `Anchor = "Top"` in the default geometry (spec Decision 12 note). ⚠ For a `.blwebform` document the Positioned shape needs a `GridGeometry`; add a `target` check: `document.Target == FormTarget.Web ? new GridGeometry { Col = 0, Row = 0 } : …`.

- [ ] **Step 2: Move the gates onto it**, one at a time, running each after:
  - `WinFormsCatalogSweepTests.EveryProperty_OfEveryControl_…` (`:143-162`): replace the hand-built control with `var control = FormCatalogShapes.Canonical(form, definition, "ctl");` then the property loop. Same in `TheDefaultEvent_OfEveryControl_…` (`:194-201`). The enum sweep (`:182-218`): `Canonical(form, definition, $"ctl{i}", hostId: $"ctl{i}Host")` per value.
  - `FormCanvasRenderTests.DocumentWith(kind)`: `FormCatalogShapes.Canonical(document, FormControlCatalog.Find(kind)!, SharedId, hostId: SharedId, geometry: new PixelGeometry { X = 20, Y = 20, Width = 140, Height = 40 })`; `EveryControlKindRendersDistinctly` filter becomes `d.SupportsTarget(WinForms) && d.Place != FormPlace.Tray && d.Place != FormPlace.Item` (items leave the hash — spec §7).
  - `FormRetargetTests.EveryCatalogKind_Retargets_…`: build with `Canonical(source, definition, "c")`, locate the crossed control with `FormCatalogShapes.Locate(result.Document, definition)`, and scope `reportedLost` to messages containing `'c.`, i.e. `.Where(m => m.Contains("'c."))` before the `Single` lookup.
- [ ] **Step 3: Run** the three fixtures (`WinFormsCatalogSweepTests` is Integration: run it explicitly) → green with today's rows (no behaviour change yet).

### Task 9: The toolbox membership pins and the structural-name pin learn `Place`

**Files:** Modify `FormPropertyGridTests.cs:552-568`, `FormToolboxGlyphTests.cs:75-89`, `FormDocumentTests.cs:80-92`, `FormToolboxViewModel.cs:53-56`.

- [ ] **Step 1:** In both toolbox pins, the expected set becomes `FormControlCatalog.For(target).Where(c => c.Place != FormPlace.Item)`; in `FormToolboxViewModel.Rebuild`, `.Where(c => c.Place != FormPlace.Item)` before the `OrderBy`, with a comment: items are created from Type Here (spec Decision 7). In `Catalog_NoPropertyCollidesWithAStructuralAttribute`, skip rows with `def.Place != FormPlace.Positioned` and say why in a comment: the write-twice hazard exists only where geometry is written; a Docked row's `Dock` is a PROPERTY.
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

`RenderSchematicForTest` is an `internal static` helper on `FormCanvasControl` that creates a headless `Window` hosting a `FormCanvasControl` with a `SchematicOverride`, renders, and hashes the frame (reuse `FormCanvasRenderTests.RenderHash`'s body; make `VisualGameStudio.Shell` internals visible to the tests if not already — check `InternalsVisibleTo` in the Shell csproj / `FormToolboxViewModel.GlyphFor` is already `internal` and reachable, so it is).

- [ ] **Step 2: Run** → the glyph test fails (seven `?`), the paint test fails (seven schematics fall to the default arm and hash alike).
- [ ] **Step 3: Implement.** Extract from `DrawControl` everything after the label/colour setup into `private void DrawSchematic(DrawingContext context, FormSchematic schematic, Rect bounds, string label, IBrush face, IBrush client, IBrush ink)` — the existing `switch (schematic)` verbatim — and have `DrawControl` call it. Add the seven arms with distinct shapes: `MenuBar` (a flat band with a 1px bottom rule), `ToolBar` (a band with a left grip of two vertical lines), `StatusBar` (a band with a 1px top rule and a sizing-grip triangle bottom-right), `MenuItem` (the caption, with a 2px left pad, no box), `Separator` (a 1px vertical line, or horizontal when `bounds.Width > bounds.Height`), `ToolButton` (a small raised box with the caption), `StatusLabel` (caption at left). ⛔ Bands draw NO caption (spec §6). Add the seven `GlyphFor` arms: `MenuBar => "≡_"`, `ToolBar => "[▸]"`, `StatusBar => "_≡"`, `MenuItem => "≡"`, `Separator => "—"`, `ToolButton => "[▸"`, `StatusLabel => "_A"` — distinct from every existing mark (run the glyph test). Add `internal static string RenderSchematicForTest(FormSchematic, Rect, string)` and a `SchematicOverride` used only when non-null.
- [ ] **Step 4: Run** `FormSchematicPinTests`, `FormCanvasRenderTests`, `FormToolboxGlyphTests` → green.

### Task 11: Gate and commit 24b

- [ ] Build (the Shell: `dotnet clean` not needed — no AXAML changed). Fast subset; `WinFormsCatalogSweepTests`; `FormCanvasRenderTests`; `FormSchematicPinTests`. Mutants: (a) remove one `GlyphFor` arm → the glyph pin; (b) make `Separator` draw like `MenuItem` → the paint pin; (c) `Canonical`'s Item branch pick `All.First(d => d.Items?.Accepts(…))` (any host) → no test yet (rows absent) — record that it is pinned in 24c's coverage test.
- [ ] Commit `feat(designer): Task 24b — the row SHAPE (Place, FormItemRule) and every gate learns it before a strip exists`.

---

# Commit 24c — rows, format, emission, bands, placement (spec §3–§5, §2)

### Task 12: The seven rows, BL8030, coverage

**Files:** `FormControlCatalog.cs` (after the tray rows), `DesignDiagnostic.cs` (band comment `:54-75`, a constant), `FormCatalogCoverageTests.cs`.

- [ ] **Step 1: Failing coverage tests:** `ExpectedWinFormsKinds` gains the seven names; `TheStripRows_HaveTheShapeTheSpecStates`: MenuStrip/ToolStrip/StatusStrip are `Docked`, `IsContainer == false`, have `Items` with `Kinds[0]` = `ToolStripMenuItem`/`ToolStripButton`/`ToolStripStatusLabel` and `Add` containing `Items.Add`; ToolStripMenuItem is `Item` with `Items` (`DropDownItems.Add`); the other three are `Item` with `Items == null`; every strip and item `SupportsTarget` both; `MenuStrip.FormProperty == "MainMenuStrip"`; `HtmlChildrenWrapper == "ul"` on MenuStrip and ToolStripMenuItem; `ToolStripButton.HtmlTag == "input"` with `HtmlInputType == "button"`; `ToolTipText` has `HtmlAttributeName == "title"`; `Enabled` is WinForms-only on every strip/item row EXCEPT ToolStripButton; `Checked/CheckOnClick/DisplayStyle/Spring/GripStyle/SizingGrip` are WinForms-only; every item kind's canonical host is Docked (`FormCatalogShapes` picks one). Also `DesignCodes.StripMisplaced == "BL8030"`.
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

- [ ] **Step 4: Run** `FormCatalogCoverageTests`, `FormToolboxGlyphTests`, `FormPropertyGridTests`, `FormSchematicPinTests`, `FormCanvasRenderTests` (Docked rows draw bands via `DrawSchematic` — they will be laid out only after Task 17; if the render gate is red for the three strips because `Layout` skips null geometry, do Task 17 BEFORE running it — see the ordering note in Task 19).

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

### Task 14: The writer and the clipboard mirror

**Files:** `Serialization/FormDocumentWriter.cs` (`ApplyControl` `:388-454`, `ControlElement` `:530-600`, `ApplyControlList`, `Create`), `FormDocument.cs` (`FormClipboard.ToElement` `:342-404`, `FromElement` `:415-490`, `RenumberTabIndexes` `:216-223`), `FormPlacement.NextTabIndex` (`:202-206`); Tests: `FormStripDocumentTests`, `FormDesignerCommandTests`.

- [ ] **Step 1: Failing tests:** `Create_WritesNoZerosAndNoTabIndex_OnAStripOrItem` (`FormDocumentWriter.Create(model)` of the fixture: the `<MenuStrip` element has attributes `Id` and `Dock` only; items have no `TabIndex`); `Apply_NeverWritesTabIndexOnAnItem` (renumber, then `Write` → the item element still has no `TabIndex`); `RenumberTabIndexes_SkipsStripsAndItems` (the Button after a strip is `TabIndex 0`); `Clipboard_ASerializedStrip_ComesBackGeometryLess_WithDockKept_AndNoTabIndex` (`FormClipboard.SerializeSubtree` → `DeserializeSubtree`); `Clipboard_AnItemRoot_KeepsItsChildren`.
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement:** replace every `isComponent` bool in the writer with a `FormPlace place = control.Definition?.Place ?? FormPlace.Positioned` derived per control (the `Components` list still passes `isComponent: true` for the reader-parity check — keep the parameter but compute `place` from the row): `TabIndex` and geometry written only when `place == Positioned`; children written when `place != Tray`. Same in `FormClipboard.ToElement` (`:348`) / `FromElement` (`:425-434`, `:439-441`: the attribute skip is `place == Positioned ? IsStructural(name, target) : name == "Id"`). `RenumberTabIndexes` and `NextTabIndex`: iterate `AllControls().Where(c => c.Definition?.Place == FormPlace.Positioned)` (⚠ a control with no row is Positioned).
- [ ] **Step 4: Run** the fixtures + `FormDesignerCommandTests` + `FormComponentDocumentTests` → green.

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

### Task 16: The web emitter — chrome, wrappers, roles, no tabindex, `&`

**Files:** `FormAssetEmitter.cs` (`Html` `:100-136`, `AppendControl` `:138-272`, `Css` `:282-331`); Tests: `FormStripEmissionTests`.

- [ ] **Step 1: Failing tests:** `Web_StripsArePageChrome`: the HTML has `<nav id="menuStrip1"` and `<menu id="toolStrip1"` BEFORE `<div class="vgs-form">`, `<footer id="statusStrip1"` AFTER `</div>`, `data-form` on `<body>`, none of the strip ids inside the div; `Web_TwoBottomStrips_AreEmittedInReverseDocumentOrder`; `Web_MenuNesting`: `<nav id="menuStrip1" class="vgs-MenuStrip"><ul><li id="mnuFile" class="vgs-ToolStripMenuItem">File<ul><li id="mnuOpen"` (the `&` stripped: `&amp;File` is NOT present), `<li id="sep1" class="vgs-ToolStripSeparator" role="separator">`, `<menu id="toolStrip1" class="vgs-ToolStrip" role="toolbar"><input id="tsbOpen" class="vgs-ToolStripButton" type="button" value="Open">`, `<footer … role="status"><span id="lblStatus" …>Ready</span>`; no `tabindex` on any strip or item element; `Web_ToolTipTextBecomesTitle`; `Web_TheStylesheetCarriesEachPresentStripsCss_Once`.
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement** in `Html`: partition `form.Controls` into `top = Docked with Dock (property or default) == "Top"`, `bottom = Docked … "Bottom"`, `rest`; emit `top` in document order before the div, `rest` inside, `bottom` in REVERSE document order after `</div>`. In `AppendControl`: `tabindex` only when `definition.Place == FormPlace.Positioned`; after `class`, `if (definition.HtmlRole != null) sb.Append($" role=\"{definition.HtmlRole}\"")`; the text for an `Item` row strips `&` (`text.Replace("&", "")`, once — a literal `&&` in a WinForms caption means one `&`; handle `&&` → `&` first); children wrapped: `if (definition.HtmlChildrenWrapper is { } w) sb.Append($"<{w}>")` before the child loop and the close after. In `Css`: after the per-control rules, `foreach (var css in form.AllControls().Select(c => c.Definition?.WebCss).Where(s => s != null).Distinct()) sb.Append(css).Append('\n')`.
- [ ] **Step 4: Run** the fixture + `FormAssetEmitterTests` (or whatever the existing emitter fixture is called — grep `FormAssetEmitter.Html(` in Tests) → green.

### Task 17: The canvas bands — `Layout(document, selected)` and the band arms

**Files:** `FormCanvasTransform.cs` (`Layout` `:320-376`, `HitTest` `:89-126`, `ContainerAt` `:259-294`, `ControlsIn` `:143-162`), `FormCanvasControl.cs` (`Render` `:1108`, `FormBoundsOf` `:993`, `OnPointerPressed` `:583/:617`, `OnCanvasDoubleTapped` `:498`); Create `VisualGameStudio.Tests/Shell/FormStripLayoutTests.cs`; the transform tests that deconstruct 2-tuples (`FormCanvasTransformTests.cs`, `FormCanvasRenderTests.cs:123-124`).

- [ ] **Step 1: Failing tests** (pure transform tests, no rendering):
  - `Layout_YieldsATopStripAsABandAcrossTheSurface`: MenuStrip → entry with `Bounds == new Rect(0, 0, surface.Width, 24)`, `Role == FormLayoutRole.Band`.
  - `Layout_StacksTopStrips_InDocumentOrder_AndBottomStripsFromTheEdge`: MenuStrip then ToolStrip → y 0 then 24; StatusStrip at `surface.Height - 22`; a second Bottom strip above it.
  - `Layout_NeverYieldsAZeroRect_ForAStrip`; `Layout_YieldsBandsOnAWebPage_RegardlessOfLayoutKind` (a Flow page with a MenuStrip still yields the band).
  - `HitTest_ReturnsTheStrip_OnTheBand_OnBothTargets`; `ContainerAt_NeverReturnsAStrip`; `ControlsIn_NeverReturnsAStripOrAnItem`.
  - Items yield NOTHING yet (24d adds cells): `Layout_DoesNotYieldItemsYet` — assert no entry whose control is an Item (this test is replaced in 24d).
- [ ] **Step 2: Run** → compile errors (`Role`, the overload).
- [ ] **Step 3: Implement.** New `public enum FormLayoutRole { Control, Band, Cell, TypeHere }` and `public readonly record struct FormLayoutEntry(FormControl? Control, Rect Bounds, FormLayoutRole Role);`. `Layout(FormDocument document, FormControl? selected = null)` returns `IEnumerable<FormLayoutEntry>`: the existing positioned walk (skipping `Docked` roots — they are yielded as bands, never through `BoundsOf`), then `Bands(document)`:

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

`WebLayout` yields its cells only for a Grid layout but ALWAYS appends `Bands`. `HitTest(document, canvasPoint, selected = null)` reads `Layout(document, selected)` on BOTH targets (`.Where(e => e.Control != null && e.Bounds.Contains(formPoint)).Select(e => e.Control).LastOrDefault()`), deleting the recursive `BoundsOf` walk; `ControlsIn` filters `e.Control != null && e.Control.Definition?.Place is not (FormPlace.Item or FormPlace.Docked)`; `ContainerAt` skips `Docked` roots. Update the consumers: `Render` (`foreach (var entry in FormCanvasTransform.Layout(document, SelectedControl))` → `if (entry.Control != null) DrawControl(context, entry.Control, _transform.ToCanvas(entry.Bounds));`), `FormBoundsOf`, `FormCanvasRenderTests.cs:123-124`, the `FormCanvasTransformTests` rows, `FormZOrderTests`. ⚠ The overflow edge (spec §2 Layout row): add `FormStripLayoutTests.HitTest_FindsAChildWhereItIsPainted_EvenOutsideItsPanel` and pin whichever the new walk does.
- [ ] **Step 4: Run** `FormStripLayoutTests`, `FormCanvasTransformTests`, `FormZOrderTests`, `FormCanvasMultiSelectTests`, `FormCanvasRenderTests` (the three strips now hash as bands, distinct), `FormTrayViewTests` → green. Add `FormCanvasRenderTests.AStripWithAButton_DrawsABandAtTheTop_AndLeavesTheButtonAlone` (frame with MenuStrip+Button differs from Button alone; the Button's own region — crop or compare a sub-rectangle hash — is unchanged).

### Task 18: Placement, toolbox category, grid, drop refusal, recognizer

**Files:** `FormPlacement.cs` (`Place` `:37-106`, new `PlaceItem`), `FormToolboxViewModel.cs` (category), `FormPropertyGridViewModel.cs` (`:176-183`), `CodeEditorDocumentViewModel.cs` (`TrayDrop` `:566-584`, `PlaceDroppedControl` `:545-557`), `Recognizer/WinFormsDialect.cs` (`:161`, `:302-315`); Tests: `FormPlacementTests`, `FormToolboxGlyphTests`/`FormPropertyGridTests`, `FormTrayTests`-style VM tests in a new `FormStripEditorTests.cs` (VM level), `FormStripRecognizerTests.cs`.

- [ ] **Step 1: Failing tests:** `Place_ADockedKind_LandsTopLevel_GeometryLess_WithTheRowsDock_IgnoringThePoint`; `Place_AnItemKind_IsRefused_NamingTypeHere` (message contains "Type Here"); `PlaceItem_AppendsToTheHost_WithTheCaptionAsId` (`PlaceItem(doc, menuStrip, "ToolStripMenuItem", "&Open...")` → id `openToolStripMenuItem`, `Text == "&Open..."`, last child of the host; a second `Open` → `openToolStripMenuItem1`; `"-"` → a `ToolStripSeparator` `toolStripSeparator1`; `"123"` → `toolStripMenuItem1` fallback; a kind the host does not accept → refusal); toolbox: the three strips appear under category `Menus & Toolbars`, no item kind is offered; grid: `Rows_ForAStrip_HaveDockAndNoTabIndexOrGeometry`, `Rows_ForAnItem_HaveNoTabIndex`; VM: `TrayDropCommand.Execute("ToolStripMenuItem")` and `PlaceDroppedControl` of an Item kind publish BL8019 whose message contains "Type Here" and place nothing; recognizer: `design --check` on a `.bas` with `menuStrip1.Items.Add(mnuFile)` and `mnuFile.DropDownItems.Add(mnuOpen)` reports NO BL8006 for `mnuFile`/`mnuOpen` (build the source through `DesignCheck.CheckSource`).
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement.** `FormPlacement.Place`: after the Tray branch, `if (definition.Place == FormPlace.Item) return new FormPlacementResult(null, $"'{definition.Kind}' is created from its menu's Type Here slot, not dropped.");` and `if (definition.Place == FormPlace.Docked) { var strip = new FormControl { Kind, Id = NextId(…) }; var dock = definition.Property("Dock"); if (dock?.Default != null) strip.Properties["Dock"] = dock.Default; document.Controls.Add(strip); return new(strip, null); }`. New:

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

    /// <summary>VS's id: the caption camel-cased and sanitised + the kind; `-` → `toolStripSeparator1`; unusable → kind + N.</summary>
    internal static string ItemId(FormDocument document, FormControlDef definition, string text)
    {
        var kindPart = char.ToLowerInvariant(definition.Kind[0]) + definition.Kind[1..];
        var words = new string(text.Replace("&&", "").Replace("&", "").Replace("", "&").Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var caption = string.Concat(words.Select((w, i) => i == 0 ? char.ToLowerInvariant(w[0]) + w[1..] : char.ToUpperInvariant(w[0]) + w[1..]));
        var stem = caption.Length > 0 && char.IsLetter(caption[0]) && definition.Kind != "ToolStripSeparator"
            ? caption + definition.Kind
            : kindPart;
        return FormDocument.IsLegalControlId(stem) ? MakeUnique(document, stem) : MakeUnique(document, kindPart);
    }
```

(`MakeUnique`: `stem`, then `stem1`, `stem2`… over `document.FindById`; if `FormDocument.MakeUniqueId` exists, use it.) Toolbox: category `Place == Docked ? "Menus & Toolbars"` sorted after Containers (`OrderBy(c => c.Place switch { Tray => 3, Docked => 2, _ when c.IsContainer => 1, _ => 0 })`). Grid: `if (control.Definition?.Place is null or FormPlace.Positioned)` around the TabIndex row. VM: `TrayDrop`'s refusal text for a non-component already names "drop it on the form"; add an `Item` check first with the Type Here message; `PlaceDroppedControl` gets the refusal from `Place` automatically. Recognizer: at `WinFormsDialect.cs:161` also match `CheckName(2, "Items") && CheckName(4, "Add")` and `CheckName(2, "DropDownItems") && CheckName(4, "Add")` → `MarkParented`.
- [ ] **Step 4: Run** all named fixtures → green.

### Task 19: Gate and commit 24c

- [ ] Ordering inside this commit (spec Decision 12): Tasks 12→13→14 (format), 15→16 (emission; run the csc sweep after 15), 17 (bands; run the render gate after), 18 (surface entry points). If the render gate must run before 17 for Task 12's Step 4, run it after 17 instead and say so in the commit message.
- [ ] `dotnet clean` is NOT needed (no AXAML yet). Build; fast subset; `WinFormsCatalogSweepTests`; `FormCanvasRenderTests`; `FormSchematicPinTests`; `FormRetargetTests` (the sweep builds canonical shapes — items nest, strips have no geometry; if `Place` (web→pixels) throws on the Docked canonical shape here, do 24e's Task 26 rule NOW and move it into this commit).
- [ ] Mutants: reverse the host-verb loop → `InDocumentOrder` fails; emit `Controls.Add` for items → csc sweep fails (CS1503) AND the emission test; drop the `<ul>` wrapper → `Web_MenuNesting`; forward-order the Bottom chrome → `TwoBottomStrips`; write TabIndex on an item in `Create` → `Create_WritesNoZeros…`; let the reader accept a Button under a MenuStrip → `Read_RefusesAControlInsideAHost`; `Bands` yields a 0-height rect → `Layout_NeverYieldsAZeroRect`.
- [ ] Commit `feat(designer): Task 24c — menus, toolbars and status bars: rows, format, emission on both targets, bands`.

---

# Commit 24d — the editing surface (spec §6)

### Task 20: Cells, dropdowns and Type Here slots in `Layout`

**Files:** `FormCanvasTransform.cs`; Tests: `FormStripLayoutTests`.

- [ ] **Step 1: Failing tests:** with the §3 document and `selected: menuStrip1` → after the band, cells for `mnuFile` (`Role == Cell`, `Bounds.X == 0`, width `8 + 7*len("&File") + 8` — the schematic width rule, `&` counted as written), then ONE `TypeHere` entry (`Control == null`) at `x = cell.Right` in the band; with `selected: mnuOpen` → `mnuFile`'s dropdown cells `[mnuOpen, sep1, mnuExit]` stacked below `mnuFile`'s cell (each 22 high; a separator 6 high) + `mnuFile`'s slot below them + `mnuOpen`'s own dropdown (empty) to the RIGHT of `mnuOpen`'s cell at the same y with its slot; the dropdown entries come LAST in the sequence; with `selected: null` → no `TypeHere` entry at all; `TypeHereAt(document, point, selected)` returns the host for a point in the slot and null elsewhere; `HitTest` on a dropdown cell returns the nested item; `ControlsIn` still excludes items.
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement** `Cells(document, selected)`: for each band, lay its top-level items left→right (`x += CellWidth(item)`; `CellWidth = item is Separator ? 6 : 8 + 7 * (Text ?? Id).Length + 8`); the ACTIVE strip (selected strip, or the selected item's root strip via a parent map built from `AllControls()`) gets a `TypeHere` entry after its cells; then the EXPANSION PATH: walk from the selected item up to the strip collecting item ancestors; expanded = ancestors ∪ {selected item if `IsHost`}; for each, outermost first: a dropdown at `(cell.X, cell.Bottom)` for a band cell or `(cell.Right, cell.Y)` for a nested cell, rows stacked, then the slot; yield all dropdown entries after every band/cell entry. `TypeHereAt(document, canvasPoint, selected)` = the last `TypeHere` entry containing the form point → its host (carry the host in the entry: make `FormLayoutEntry` `(Control, Bounds, Role, Host)` or return the host as `Control` with `Role == TypeHere` and let consumers skip `Role == TypeHere` — choose the latter and update the Task 17 consumers' filter from `Control != null` to `Role != TypeHere`).
- [ ] **Step 4: Run** the fixture (+ Task 17's) → green.

### Task 21: The canvas — `TypeHereHost`, `TypeHereBounds`, `BeginTypeHereCommand`, cell/slot drawing, presses

**Files:** `FormCanvasControl.cs` (styled properties block `:34-215`, `OnPointerPressed` `:563-632`, `Render` `:1085-1160`, `DrawSchematic`); Create `VisualGameStudio.Tests/Shell/FormStripCanvasTests.cs`.

- [ ] **Step 1: Failing headless tests** (the `FormCanvasMultiSelectTests` rig: a `Window` hosting the canvas, `MouseDown/MouseUp` at canvas points computed from `FormCanvasControl.Fit` — read `FormCanvasMultiSelectTests.cs:47-89` for the exact helper): a press on `mnuFile`'s cell → `Selection.Primary == mnuFile` (give the canvas a `Selection`); a press on a dropdown cell (after selecting `mnuFile`) → the nested item; a double-click on a dropdown cell → `ActivateControlCommand` executed with it; a press on the Type Here slot (with the strip selected) → a `Recorder` bound to `BeginTypeHereCommand` fired with the host; with `TypeHereHost = menuStrip1`, after a render `TypeHereBounds` equals the canvas rect of the slot entry (compare against `Fit(...).ToCanvas(slot.Bounds)`); the frame with `mnuFile` selected differs from the frame with nothing selected (the dropdown paints) and the frame with `TypeHereHost` set differs again (the slot is highlighted).
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement:** styled properties `TypeHereHost` (`FormControl?`, in `AffectsRender`), `TypeHereBounds` (`Rect`, NOT in `AffectsRender`, set at the end of `Render` from the `TypeHere` entry whose host is `TypeHereHost`, else `default`), `BeginTypeHereCommand` (`ICommand?`). `Render`: iterate `Layout(document, SelectedControl)`; `Band`/`Control` → `DrawControl`; `Cell` → `DrawSchematic(item schematic)`; `TypeHere` → draw a greyed italic "Type Here" box (highlighted when `Control == TypeHereHost`). `OnPointerPressed`, left button, BEFORE `HandleUnder`: `var slotHost = _transform.TypeHereAt(document, point, SelectedControl); if (slotHost != null) { BeginTypeHereCommand?.Execute(slotHost); e.Handled = true; return; }`; a hit on a `Cell` entry selects through `ApplyClickSelection` and arms NO drag (guard `SelectedControl?.Geometry is PixelGeometry` before the move branch — items have none, so the existing early-returns already hold; pin it). Pass `SelectedControl` to all three `HitTest` calls (`:498`, `:583`, `:617`).
- [ ] **Step 4: Run** the fixture + every `FormCanvas*Tests` + `FormTrayViewTests` → green.

### Task 22: `FormTypeHereEditor` — the overlay control

**Files:** Create `VisualGameStudio.Shell/Controls/FormTypeHereEditor.cs`; Create `VisualGameStudio.Tests/Shell/FormTypeHereEditorTests.cs`.

- [ ] **Step 1: Failing tests:** a `Window` hosting the editor; set `SlotBounds = new Rect(30, 40, 120, 22)`, `IsActive = true` → the inner `TextBox.Bounds` (after layout, `window.UpdateLayout()`/a render) equals that rect and the box `IsFocused`; `window.KeyTextInput("Open")` then `window.KeyPress(Key.Enter, RawInputModifiers.None)` → a `Recorder` on `CommitCommand` fired once with `"Open"` and `Text` is cleared; `KeyPress(Escape)` → `CancelCommand` fired; set `Host` to another object while `IsActive` → the box is focused again (the focus-on-every-Begin rule); `IsActive = false` → the editor is collapsed/hidden.
- [ ] **Step 2: Run** → compile error.
- [ ] **Step 3: Implement** a `TemplatedControl`-free `Panel` subclass (a `Canvas` with one `TextBox` child): styled `IsActive` (bool), `Host` (object?), `Text` (string, TwoWay to the box), `SlotBounds` (Rect), `CommitCommand`/`CancelCommand` (ICommand?). `OnPropertyChanged`: `IsActive` or `Host` changed while active → `IsVisible = IsActive; if (IsActive) Dispatcher.UIThread.Post(() => _box.Focus())`; `SlotBounds` changed → `Canvas.SetLeft/SetTop(_box, …)`, `_box.Width/Height`. `_box.KeyDown`: `Enter` → `CommitCommand?.Execute(_box.Text ?? "")`, `_box.Text = ""`, handled; `Escape` → `CancelCommand?.Execute(null)`, handled. `IsHitTestVisible` false when inactive so the canvas below receives clicks.
- [ ] **Step 4: Run** → green.

### Task 23: The view model — `StripEditor`, Begin/Commit/Cancel, the selection rule, paste into a host

**Files:** Create `VisualGameStudio.Shell/ViewModels/Designer/FormStripEditorViewModel.cs`; Modify `CodeEditorDocumentViewModel.cs` (ctor, `PasteControls` `:429-468`, `SelectInDesigner`); Create `VisualGameStudio.Tests/Compiler/FormStripEditorTests.cs` (VM-level, the `FormTrayTests` rig).

- [ ] **Step 1: Failing tests:** open a `.blform` with a MenuStrip (the §3 document without items, `MenuForm`); `vm.BeginTypeHere(menuStrip)` → `vm.StripEditor.IsActive`, `Host == menuStrip`, `Selection.Primary == menuStrip`; `vm.CommitTypeHere("&File")` → `fileToolStripMenuItem` under the strip with `Text == "&File"`, selected through `Selection` (and the grid), the document text contains it (undoable), editor still active with `Host == menuStrip`; `BeginTypeHere(fileToolStripMenuItem)` then `CommitTypeHere("&Open...")` → `openToolStripMenuItem` under it; `CommitTypeHere("-")` → `toolStripSeparator1`, a `ToolStripSeparator`; `CommitTypeHere("E&xit")` → `exitToolStripMenuItem`; the order is Open, sep, Exit; `FormCanvasTransform.Layout(doc, selected: exitToolStripMenuItem)` yields `fileToolStripMenuItem`'s dropdown cells, its slot, and `exitToolStripMenuItem`'s own slot to the right; `CommitTypeHere("-")` on a StatusStrip → refused via a `DesignerDiagnosticsEvent` BL8019 with "separator" in the message and nothing placed; `CancelTypeHere()` → inactive; with the editor active on the strip, `SelectInDesigner(theButton)` → the editor is cancelled; `Selection.Set(fileToolStripMenuItem)` (inside the host) → still active; undo after a commit → the item is gone from `DesignDocument` and the editor is cancelled (the model was rebuilt); paste: copy `fileToolStripMenuItem`, select `menuStrip`, paste → a renamed item appended to the strip; select the Button, paste an item → refused-and-reported, nothing added.
- [ ] **Step 2: Run** → red.
- [ ] **Step 3: Implement.** `FormStripEditorViewModel : ObservableObject` with `[ObservableProperty] bool _isActive; FormControl? _host; string _text = ""`. On the document VM:

```csharp
    public FormStripEditorViewModel StripEditor { get; } = new();

    [RelayCommand]
    private void BeginTypeHere(BasicLang.Forms.FormControl? host)
    {
        if (host?.Definition?.Items == null) return;
        SelectInDesigner(host);                 // the slot exists only for the selected strip/item's path
        StripEditor.Host = host;
        StripEditor.Text = "";
        StripEditor.IsActive = true;
    }

    [RelayCommand]
    private void CommitTypeHere(string? text)
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
    private void CancelTypeHere() { StripEditor.IsActive = false; StripEditor.Host = null; StripEditor.Text = ""; }
```

In the ctor's `Selection.Changed` handler, after the grid update: `if (StripEditor.IsActive && StripEditor.Host is { } h && !(Selection.Primary == h || Selection.Primary?.SelfAndAncestors(file.Model).Contains(h) == true)) CancelTypeHere();` (write an ancestor walk over `AllControls()` — `FormDocument` has no parent pointers; a small `ParentOf(document, control)` exists in `FormGeometryEdit.cs:262` — reuse it). Cancel in `OnDesignModelRevisionChanged` when the host is no longer in the document (undo). `PasteControls`: for a pasted root whose row is `Item`: `var host = Selection.Primary; if (host?.Definition?.Items?.Accepts(control.Kind) == true) host.Children.Add(control); else { ReportPlacementRefusal(...); continue; }`; a pasted `Docked` root → `Controls`, no offset.
- [ ] **Step 4: Run** the fixture + `FormTrayTests` + `FormDesignerCommandTests` → green.

### Task 24: Wire the view — AXAML, gate

**Files:** `Views/Documents/CodeEditorDocumentView.axaml` (`:223-235`), `.axaml.cs`; Tests: `FormTrayViewTests`-style AXAML gate in `FormStripCanvasTests` (or a new `FormStripViewTests`).

- [ ] **Step 1: Failing gate:** parse the AXAML; the `FormCanvasControl` element has `TypeHereHost="{Binding StripEditor.Host}"` and `BeginTypeHereCommand="{Binding BeginTypeHereCommand}"`; a `designer:FormTypeHereEditor` element in the SAME `Grid.Row="0"` cell (after the canvas, so it paints above) with `x:Name="TypeHereEditor"`, `IsActive="{Binding StripEditor.IsActive}"`, `Host="{Binding StripEditor.Host}"`, `Text="{Binding StripEditor.Text, Mode=TwoWay}"`, `SlotBounds="{Binding #DesignCanvas.TypeHereBounds}"`, `CommitCommand="{Binding CommitTypeHereCommand}"`, `CancelCommand="{Binding CancelTypeHereCommand}"` — assert each by attribute; the canvas element gains `x:Name="DesignCanvas"`.
- [ ] **Step 2: Run** → red. **Step 3:** edit the AXAML as asserted (namespace `designer:` already maps to `VisualGameStudio.Shell.Controls`). **Step 4:** `dotnet clean VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release` then build; run the gate → green. Launch the IDE once from `VisualGameStudio.Shell\bin\Release\net8.0\VisualGameStudio.exe`, open a `.blform`, drop a MenuStrip, type `&File` Enter `&Open...` Enter — record what you saw in the commit message (this is the owner's acceptance surface; the tests above are the gate).

### Task 25: Per-item render pin, gate, commit 24d

- [ ] `FormCanvasRenderTests.EveryItemKind_ChangesItsHostsFrame`: for each `Place == Item` row, `RenderHash(Canonical(host with item, SharedId, hostId: SharedId, item Text "X"))` ≠ `RenderHash(host alone)`, WITH `SelectedControl = item` so cells are laid out (cells are unconditional per band in Task 20 — verify; if cells only appear on the band regardless of selection, no selection is needed). Mutant: blank the `MenuItem` arm in `DrawSchematic` → the pin fails.
- [ ] Build (after `dotnet clean` Shell); fast subset; `FormCanvasRenderTests`; `FormSchematicPinTests`; `FormTypeHereEditorTests`; `FormStripCanvasTests`; `FormStripEditorTests`. Mutants: `BeginTypeHere` not selecting the host → the slot test; commit selecting nothing → the "selected through Selection" assertion; the cancel-on-leave handler removed → its test; the editor not re-focusing on `Host` change → the editor test; `TypeHereBounds` computed from the wrong entry → the bounds test; the slot press placed after the marquee branch → the slot press test (a marquee starts instead).
- [ ] Commit `feat(designer): Task 24d — the Type Here strip: cells, dropdowns, the in-place editor`.

---

# Commit 24e — retarget, acceptance, records

### Task 26: The retarget rule

**Files:** `FormRetarget.cs` (`Place` `:584-676`), `FormRetargetTests.cs`.

- [ ] **Step 1: Failing tests:** `ToWinForms_AStripAndItsItems_CrossGeometryLess_WithDock` (a `.blwebform` with a MenuStrip+items and a Button in a cell → the strip has `Geometry == null`, `Properties["Dock"] == "Top"`, items nested with null geometry, the Button placed in pixels, no BL8025 naming the strip or an item); `ToWinForms_APageWhoseTopLevelIsAStripAlone_DoesNotThrow`; `ToWeb_AStripCrosses_AndTheRetargetedPageIsChrome` (through `ConvertToPair`, the HTML has `<nav` before the div); the sweep (`Canonical` for every Docked/Item row, both directions) → `Locate` finds the crossed control, geometry null, no BL8025 for it.
- [ ] **Step 2: Run** → the strip-alone page throws `InvalidOperationException` (`Max` on empty).
- [ ] **Step 3: Implement** in `Place`: `var positioned = siblings.Where(c => c.Definition?.Place is null or FormPlace.Positioned).ToList(); if (positioned.Count == 0) return (0, 0);` and use `positioned` everywhere `siblings` was used (sizes, pitch, the placement loop); never recurse into a Docked/Item control's children.
- [ ] **Step 4: Run** `FormRetargetTests`, `DesignRetargetCliTests`, `SolutionExplorerRetargetTests` → green.

### Task 27: The node harness learns a form name and a target element

**Files:** `FormDesignerAcceptanceTests.cs:270-336`; callers `FormComponentAcceptanceTests.cs:250`, `FormRetargetPairTests.cs:293`.

- [ ] `internal static string? RunPageUnderNode(string outDir, string formName = "LoginForm", string? clickId = null)`: `body.setAttribute("data-form", "{{formName}}")`; the click loop becomes `clickId == null ? every element : els.get("{{clickId}}")?.click()`. Existing callers unchanged (defaults). Run `FormDesignerAcceptanceTests`, `FormComponentAcceptanceTests`, `FormRetargetPairTests` → green.

### Task 28: Acceptance — a menu built by the designer RUNS on both targets

**Files:** Create `VisualGameStudio.Tests/Compiler/FormMenuAcceptanceTests.cs` (`[Category("Integration")]`, `[NonParallelizable]`; copy `FormComponentAcceptanceTests`' scaffolding: temp dir, `FormScaffolder.Create("MenuForm", target)`, the VM `Open`, `SaveAsync`, the CLI, the csproj+driver, `RunPageUnderNode`).

- [ ] **Build the form through the designer's OWN commands:** `vm.PlaceControl("MenuStrip", 0, 0)` → `BeginTypeHere(strip)` → `CommitTypeHere("&File")` → `BeginTypeHere(fileToolStripMenuItem)` → `CommitTypeHere("&Open...")`, `CommitTypeHere("-")`, `CommitTypeHere("E&xit")`; `PlaceControl("ToolStrip", 0, 0)` → `BeginTypeHere` → `CommitTypeHere("Open")`; `PlaceControl("StatusStrip", 0, 0)` → `CommitTypeHere("Ready")`; `ActivateControlCommand.Execute(openToolStripMenuItem)` (the stub), insert `Console.WriteLine("CLICK")` into it exactly as the Timer test inserts its line; `SaveAsync`.
- [ ] **WinForms:** CLI `--target=csharp`; the C# contains `openToolStripMenuItem = new ToolStripMenuItem()` and `fileToolStripMenuItem.DropDownItems.Add(openToolStripMenuItem)`; driver (namespace `GeneratedCode`, `new MenuForm()`, `Show()`, then reflection-free: `form.MainMenuStrip.Items` captions joined → prints `MENU &File`; `((ToolStripMenuItem)form.MainMenuStrip.Items[0]).DropDownItems` → prints `DROP ToolStripMenuItem:&Open...,ToolStripSeparator:,ToolStripMenuItem:E&xit`; `PerformClick()` on item 0 → `CLICK`; the StatusStrip's first item text → `STATUS Ready`; `DONE`). Assert all five, in order.
- [ ] **Web:** `basiclang build` of the project (as the Timer test does), the HTML text contains `<nav id="menuStrip1"` before `<div class="vgs-form">`, `<ul><li id="fileToolStripMenuItem"`, nested `<ul><li id="openToolStripMenuItem"`, `<li id="toolStripSeparator1" … role="separator">`, `File` not `&amp;File`; `RunPageUnderNode(outDir, "MenuForm", "openToolStripMenuItem")` output contains `CLICK` exactly once and no `LOAD ERROR`.
- [ ] Both must PASS, not be skipped: read the totals. Mutants: reverse the host-verb loop → `DROP` order fails; drop `MainMenuStrip` → the driver NREs (`form.MainMenuStrip` null) — pinned; strip the `<ul>` wrapper → the HTML assertion.

### Task 29: Records

- [ ] Spec: append "§10 — What building it changed" only if any decision above had to change; otherwise nothing.
- [ ] `docs/form-designer-followups.md`: **22** `ShortcutKeys` (M6/M7: only `CType(n, Keys)`; needs a Keys editor + name table), **23** array-literal common-base widening for BasicLang classes (M1/M3: cannot serve WinForms types), **24** the C++ capability checker's missing `IRArrayAlloc` arm.
- [ ] `CLAUDE.md` form-designer section: one ⛔ bullet — a strip is `Place == Docked` (geometry-less, `Dock` PROPERTY, a band on the canvas, page chrome on the web), an item is `Place == Item` under its host and is added by the HOST row's verb in DOCUMENT order (reversing it is the silent failure), `FormCatalogShapes.Canonical` is the one fixture shape for every catalog gate, and the compiler now has `New T() {…}` (parens required, three policies, the JS array arms). Plus the compiler section: the typed literal and its three policies in one line.
- [ ] `docs/HANDOFF.md`: task table (24 done), a Task 24 section (measured facts, the five commits, gates), the gates table rows.
- [ ] Memory: `form-designer-sep18.md` + `MEMORY.md` — Task 24 done, SHAs, next = Task 28.
- [ ] Tick every box in this plan.

### Task 30: Gate and commit 24e, then the IDE drop

- [ ] Build; fast subset; `FormRetargetTests`; `FormMenuAcceptanceTests`; `FormDesignerAcceptanceTests`; `FormComponentAcceptanceTests`; then the FULL SUITE on the final binaries (both streams logged), failure NAMES vs the 8-row baseline, zero new.
- [ ] Commit `feat(designer): Task 24e — a menu from the designer RUNS on both targets; retarget; records`. Push after a fetch check; SHA-verify.
- [ ] IDE drop: `robocopy VisualGameStudio.Shell\bin\Release\net8.0 IDE /E` (never `/MIR`); verify `IDE\BasicLang.exe --help` and `new --list` exit 0 (run `new --list` to a file, never through `Select-Object -First`), `IDE\lib\js\dom-core.bli` present, `IDE\BasicLang.exe` starts with `MZ`; commit `chore(ide): refresh the IDE drop with menus, toolbars and status bars`; push; SHA-verify.
