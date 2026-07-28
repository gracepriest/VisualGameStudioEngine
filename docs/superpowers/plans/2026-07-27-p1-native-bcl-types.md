# P1: Native BCL Types Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the P1 spec (`docs/superpowers/specs/2026-07-27-p1-native-bcl-types-design.md`): native C++ DateTime/TimeSpan/Guid/StringBuilder/Decimal/DateTimeOffset (+ SByte as a Bridged primitive), the shared front-end operator/literal work (spec 6.1), the single-source `NativeBclSurface` member table, and the five-layer test strategy ending in a cross-backend parity oracle.

**Architecture:** Four phases, ordered so EVERY commit keeps the whole suite green. Phase A (Tasks 1–5) is the shared front end — it makes Decimal/SByte/DateTime/TimeSpan arithmetic pass semantic analysis and work END-TO-END on the C# backend immediately, while the C++ backend keeps rejecting the types (registry untouched). Phase B (Tasks 6–8) writes the native C++ runtime headers with their own native-only tests (no compiler wiring). Phase C (Tasks 9–10) wires codegen INERTLY first (machinery keyed off registry categories that are still Rejected), then flips the registry + checker + test churn in one commit. Phase D (Tasks 11–14) is the BL end-to-end, stdlib, parity-oracle, and closeout layers.

**Tech Stack:** C# (net8.0 compiler), C++20 header-only runtime (string constants), NUnit, `CppCompile.FindRunCompiler()` probe, `CompileToCppOptimized` + CLI validation per repo law.

**Read first:** the spec (sections cited per task as "spec §N"). Skills: @superpowers:test-driven-development, @superpowers:verification-before-completion.

**Conventions that prevent real mistakes in this repo:**
- Never round-trip repo files through PowerShell `Get-Content`/`Set-Content` (mojibake). Use Read/Edit/Write tools.
- Run tests with output redirected to a file (`> test-run.txt 2>&1`) — the suite exceeds tool output truncation. Fast subset: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"`.
- Commit messages single-line via `git commit -m "..."`.
- Line numbers cited below were verified during spec review (commits `daa6fb8`–`b30fc4e`) but code moves — ALWAYS Read the cited region first and match on content, not line number.
- The fast-subset baseline at plan start: **Passed 3394 / Failed 0 / Skipped 1** (post-boundary-contract). Task 14 compares against baseline + the new fast tests added by this plan.

**Mechanism decisions this plan adds beyond the spec** (flagged for transparency):
1. **Decimal engine code granularity.** Tasks 6–7's C++ headers are complete verbatim code in this plan. Task 8's Decimal engine specifies the complete public surface, representation, and exact per-operation algorithms with an exhaustive locked vector battery; the internal 96/192-bit limb helper BODIES are implemented by the executor against those vectors (writing ~800 lines of untested bignum verbatim in a plan produces bugs the TDD loop must fix anyway — the vectors, not prose, are the contract).
2. **Inert-first wiring.** Task 9 lands all codegen machinery keyed off `BoundaryTypeRegistry.Categorize(...) == NativeOwned` while those types are still Rejected — provably dead code paths, suite stays green — so Task 10's flip is a small, reviewable commit.
3. **Surface-table shape.** `NativeBclSurface` entries: `(TypeName, MemberName, MemberKind, ParamCounts int[], ReturnTypeName)` with `MemberKind ∈ {InstanceMethod, Property, StaticMethod, StaticProperty, Constructor, Operator}`. Operator entries use MemberName = the BL operator token (`+`, `-`, `*`, `/`, `Mod`, `=`, `<>`, `<`, `<=`, `>`, `>=`, `unary-`) and ReturnTypeName = result type; cross-type operand rows (dt−dt→TimeSpan) are keyed on (LeftType, Op, RightType).

---

## File Structure

| File | Responsibility |
|---|---|
| Modify `BasicLang/BasicLangLexer.cs` | (Task 1) numeric literal lexeme already on token — verify only |
| Modify `BasicLang/ASTNodes.cs` | (Task 1) `LiteralExpressionNode.Text` carries the lexeme |
| Modify `BasicLang/Parser.cs` | (Task 1) both literal construction sites copy token text |
| Modify `BasicLang/SymbolTable.cs` | (Tasks 2–3) IsNumeric/IsIntegral/IsSigned/IsUnsigned membership; GetCommonType Decimal branch |
| Modify `BasicLang/TypeMapper.cs` | (Task 2) SByte→int8_t added, Byte→uint8_t fixed (Cpp map); dormant CSharpTypeMapper Byte fixed |
| Modify `BasicLang/CSharpBackend.cs` | (Tasks 2,4,5) ConvertMethodForType Byte/SByte; Decimal `m` constants; `(int)` casts for DayOfWeek/Kind; CType(x, Decimal) |
| Modify `BasicLang/SemanticAnalyzer.cs` | (Tasks 3–5) operator/compound/unary gates; Decimal-context literal conversion; surface-backed member typing; stdlib date registrations; DateTimeOffset in CommonNetTypes |
| Modify `BasicLang/IRNodes.cs` / `BasicLang/IRBuilder.cs` | (Tasks 4–5) System.Decimal constant values; KnownNetStaticTypes + DateTimeOffset |
| Create `BasicLang/NativeBclSurface.cs` | (Task 5) the single-source member/operator table (spec §4) |
| Create `BasicLang/Compiler/CodeGen/CPlusPlus/CppBclRuntime.cs` | (Tasks 6–7) DateTime/TimeSpan/Guid/DateTimeOffset/StringBuilder C++ header text |
| Create `BasicLang/Compiler/CodeGen/CPlusPlus/CppDecimalRuntime.cs` | (Task 8) the 96-bit Decimal engine header text |
| Modify `BasicLang/CppCodeGenerator.cs` (+`.Split.cs`) | (Tasks 9–12) MapType NativeOwned branch; dispatch; splice; shim dismantling; EmitStdLibCall dates |
| Modify `BasicLang/CppCapabilityChecker.cs` | (Task 10) NativeOwned accept + member-surface pass |
| Modify `BasicLang/BoundaryTypeRegistry.cs` | (Task 10) category moves + doc comment |
| Modify `BasicLang/Compiler/CodeGen/CPlusPlus/CppRuntimeSources.cs` | (Task 10) remove Now/FormatTime shim helpers |
| Modify `BasicLang/StdLib/CppStdLib.cs` | (Task 12) support-matrix date category |
| Create `VisualGameStudio.Tests/Compiler/NativeBclFrontEndTests.cs` | (Tasks 1–5) fast front-end + C#-backend end-to-end tests |
| Create `VisualGameStudio.Tests/Compiler/CppBclRuntimeTests.cs` | (Tasks 6–8) Integration native-only runtime tests (CppCompile pattern) |
| Create `VisualGameStudio.Tests/Compiler/CppBclEndToEndTests.cs` | (Tasks 11–12) Integration BL→C++ end-to-end + member diagnostics |
| Create `VisualGameStudio.Tests/Compiler/BclBackendParityTests.cs` | (Task 13) Integration cross-backend parity oracle |
| Modify existing tests | (Tasks 2,10,11) churn ledger in spec §12 |
| Modify `docs/superpowers/specs/2026-07-26-dotnet-native-boundary-contract-design.md` | (Task 10) C1 example rows: SByte → Bridged |

---

### Task 1: Literal lexeme plumbing (inert)

The Decimal literal rule (spec §6.1) needs the literal's TEXT downstream of the parser. Today `BasicLangLexer.ScanNumber` puts the lexeme on the token (`AddToken(TokenType.DoubleLiteral, sb.ToString(), value, ...)`, ~line 1077) but BOTH `LiteralExpressionNode` construction sites in `Parser.cs` (~3049 and ~4035–4037) copy only `token.Value`. This task carries the text with NO behavior change.

**Files:**
- Modify: `BasicLang/ASTNodes.cs` (~line 1400, `LiteralExpressionNode`)
- Modify: `BasicLang/Parser.cs` (both construction sites)
- Test: create `VisualGameStudio.Tests/Compiler/NativeBclFrontEndTests.cs`

- [ ] **Step 1: Write the failing test** (new file; follow the compile-helper pattern of `CppCollectionTests.CompileToCpp` for the front-end pipeline — Lexer→Parser only here):

```csharp
using BasicLang;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class NativeBclFrontEndTests
{
    private static ProgramNode Parse(string source)
    {
        var lexer = new BasicLangLexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    [Test]
    public void NumericLiteral_CarriesLexemeText()
    {
        var ast = Parse("Module M\n Sub Main()\n Dim d As Double = 1.50\n End Sub\nEnd Module");
        var lit = FindFirstLiteral(ast);          // small recursive AST walker, write locally
        Assert.That(lit, Is.Not.Null);
        Assert.That(lit!.Text, Is.EqualTo("1.50"), "the literal's source text must survive parsing (scale would be lost from the double 1.5)");
    }
}
```

(Adapt the `Parse` helper to the actual lexer/parser constructor signatures — Read how existing tests in `VisualGameStudio.Tests/Compiler/` construct them and copy that idiom. `FindFirstLiteral` walks the AST for the first `LiteralExpressionNode`. Verified real names: the lexer class is `Lexer` (`new Lexer(source).Tokenize()`), and the token's lexeme property is `Lexeme` — so Step 3 sets `Text = token.Lexeme`.)

- [ ] **Step 2: Run to verify red** — `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~NativeBclFrontEndTests" > test-run.txt 2>&1`. Expected: build FAILS (`Text` not defined). Read test-run.txt to confirm the reason.
- [ ] **Step 3: Implement** — add to `LiteralExpressionNode`: `public string? Text { get; set; }` (nullable; null means "not captured", callers must handle). At BOTH Parser construction sites, set `Text = token.Lexeme` (verified property name; the lexer stores it as the second `AddToken` argument). Verify the token type used for numbers with a fractional part (recon: `TokenType.DoubleLiteral`) and that integer literals also carry text (they do — same AddToken shape).
- [ ] **Step 4: Green + fast subset** — task filter green; then the full fast subset (no behavior change: expect baseline 3394 + 1 = 3395 passed, 0 failed).
- [ ] **Step 5: Commit** — `git add BasicLang/ASTNodes.cs BasicLang/Parser.cs VisualGameStudio.Tests/Compiler/NativeBclFrontEndTests.cs` then `git commit -m "feat(p1): literal lexeme carried onto LiteralExpressionNode (inert; Decimal literals need the text)"`

---

### Task 2: SByte first-class + Byte signedness reconciliation (all channels)

Spec §2 + §6.1. SByte joins the numeric machinery and the Bridged story; Byte's signedness is fixed to unsigned EVERYWHERE. Registry does NOT move yet (SByte stays Rejected on C++ until Task 10 — this task only prepares the machinery; the C++ mapper entry addition would break the MapperInvariant test if it landed before the registry move, so the `CppTypeMapper` edit is DEFERRED to Task 10; this task touches the analyzer + C#-side + the generator's own map).

**Files:**
- Modify: `BasicLang/SymbolTable.cs` (~47–68: IsNumeric/IsIntegral/IsSigned/IsUnsigned)
- Modify: `BasicLang/CSharpBackend.cs` (~3317 `ConvertMethodForType`)
- Modify: `BasicLang/TypeMapper.cs` (~103 dormant `CSharpTypeMapper` Byte→sbyte → `byte` ONLY — do NOT touch the Cpp map yet)
- Test: append to `NativeBclFrontEndTests.cs`

- [ ] **Step 1: Failing tests** — append:

```csharp
[Test]
public void SByte_Arithmetic_PassesAnalysis_And_RunsOnCSharp()
{
    // Full pipeline through the C# backend: compile + run, assert stdout.
    var src = @"Module M
 Sub Main()
  Dim a As SByte = 5
  Dim b As SByte = -3
  Console.WriteLine(a + b)
 End Sub
End Module";
    Assert.That(CompileRunCSharp(src), Is.EqualTo("2"));
}

[Test]
public void Byte_IsUnsigned_SByte_IsSigned_InAnalyzerHelpers()
{
    Assert.That(new TypeInfo("SByte", TypeKind.Primitive).IsSigned(), Is.True);
    Assert.That(new TypeInfo("SByte", TypeKind.Primitive).IsNumeric(), Is.True);
    Assert.That(new TypeInfo("SByte", TypeKind.Primitive).IsIntegral(), Is.True);
    Assert.That(new TypeInfo("Byte", TypeKind.Primitive).IsUnsigned(), Is.True);
    Assert.That(new TypeInfo("Byte", TypeKind.Primitive).IsSigned(), Is.False);
}
```

`CompileRunCSharp`: write a fixture helper that runs the FULL C# path — find the existing pattern (Grep the test project for how C#-backend end-to-end tests compile+run; there are existing helpers driving `dotnet build`/`csc` or the CLI `IDE/BasicLang.exe file.bas --target=csharp`; if none runs the produced exe, drive the CLI and run the output). Mark the fixture `[Category("Integration")]` ONLY if it spawns processes — the TypeInfo test stays fast (split fixtures if needed: analyzer-helper tests fast, run-tests Integration).

- [ ] **Step 2: Red** (TypeInfo asserts fail: SByte not in the lists; the SByte program fails analysis with "requires numeric operands").
- [ ] **Step 3: Implement** — `SymbolTable.cs`: add `"SByte"` to the IsNumeric/IsIntegral/IsSigned name lists; move `"Byte"` from IsSigned to IsUnsigned. `CSharpBackend.ConvertMethodForType`: `"Byte" → "ToByte"`, add `"SByte" → "ToSByte"`. `TypeMapper.cs` CSharpTypeMapper: `Byte → "byte"` (dormant channel, fixed in passing; add `SByte → "sbyte"` while there). Read each site first; match content.
- [ ] **Step 4: Green + fast subset** — expect NO existing-test breakage (spec §14.1's sweep: Grep tests for `requires numeric operands` expectations involving SByte/Byte — none are known; if the sweep finds pins, update them deliberately and note in the commit).
- [ ] **Step 5: Commit** — `git commit -m "feat(p1): SByte joins numeric machinery; Byte signedness reconciled (analyzer + C# channels)"`

---

### Task 3: Decimal front end — analyzer gates (no literals yet)

Spec §6.1. Decimal joins `IsNumeric()`; `GetCommonType` gets the Decimal branch BEFORE the Double/Single/Long rungs; the `Decimal op Single/Double` error is raised in `Visit(BinaryExpressionNode)` with a hint naming `CType(x, Decimal)`; compound assignment and unary paths covered. After this task `Dim c As Decimal = a + b` analyzes (literal init still routes via the existing `IsNumericLiteralAssignable` carve-out once Decimal is numeric); `d + 0.5` (Double literal) still errors until Task 4 adds Decimal-context literal conversion — assert the ERROR message + hint here, flip the assertion in Task 4.

**Files:**
- Modify: `BasicLang/SymbolTable.cs` (IsNumeric + `GetCommonType` ~560–575)
- Modify: `BasicLang/SemanticAnalyzer.cs` (`Visit(BinaryExpressionNode)` ~4847–4918; compound gate ~4827–4835; unary ~4973–4998)
- Test: append to `NativeBclFrontEndTests.cs`

- [ ] **Step 1: Failing tests** (analyzer-level: run full analysis, assert error list — use/adapt the fixture's analysis helper):

```csharp
[TestCase("Dim c As Decimal = a + b", "")]                       // Decimal+Decimal OK
[TestCase("Dim c As Decimal = a + 1", "")]                       // integral widens (symmetric: also test 1 + a)
[TestCase("Dim c As Decimal = 1 + a", "")]
[TestCase("Dim ok As Boolean = a < b", "")]                      // comparisons OK
[TestCase("Dim c As Decimal = -a", "")]                          // unary minus OK
[TestCase("a += 1", "")]                                         // compound OK
public void Decimal_OperatorGates(string stmt, string _)
    => AssertAnalyzesClean($"Dim a As Decimal = 1\nDim b As Decimal = 2\n{stmt}");

[Test]
public void Decimal_Op_Double_Errors_WithCTypeHint()
    => AssertAnalysisError("Dim a As Decimal = 1\nDim x As Double = 0.5\nDim c = a + x",
        expectContains: new[] { "Decimal", "CType" });

[Test]
public void GetCommonType_DecimalBeforeLadder()
{
    // Decimal + Long must be Decimal, NOT Long; Decimal + Double must not silently type Double.
    // Direct unit test against TypeManager.GetCommonType if accessible; otherwise assert via
    // the declared-type of an analysis result (AssertAnalyzesClean with Dim c As Decimal = a + 1L).
}
```

(Write `AssertAnalyzesClean`/`AssertAnalysisError` helpers running Lexer→Parser→SemanticAnalyzer and inspecting the error list — copy the idiom from existing analyzer tests, Grep `SemanticAnalyzer` in the test project.)

- [ ] **Step 2: Red** — every case fails with "requires numeric operands" (or the wrong common type).
- [ ] **Step 3: Implement** — `SymbolTable.cs`: `"Decimal"` into IsNumeric; in `GetCommonType`, FIRST rung: if either side is Decimal → if the other is Decimal or integral → Decimal; if the other is Single/Double → return a sentinel the caller treats as invalid (GetCommonType has no diagnostics channel — spec §6.1 caveat; simplest: return null and have the call site at ~4880 raise the error with the CType hint). `SemanticAnalyzer.cs`: binary visit — where the IsNumeric guard passes now, ensure the Decimal/floating pair goes to the new error with hint text naming `CType(x, Decimal)`; compound path (~4827) — same validation via the shared helper; unary — no change needed beyond IsNumeric membership (verify `-a` types Decimal).
- [ ] **Step 4: Green + fast subset** (Grep for tests pinning "requires numeric operands" on Decimal — the spec §14.1 sweep; update deliberately if found).
- [ ] **Step 5: Commit** — `git commit -m "feat(p1): Decimal joins the numeric front end - promotion, gates, CType hint (spec 6.1)"`

---

### Task 4: Decimal literals + IR constants + C# emission + CType

Spec §6.1 pinned plumbing. Decimal-context literals convert from TEXT at compile time; IR constants carry `System.Decimal`; the IROptimizer's `is double` folds skip them; the C# backend emits `m`-suffixed invariant text and lowers `CType(x, Decimal)` to `(decimal)x`. C++ CType lowering waits for Task 9 (no native Decimal type exists yet).

**Files:**
- Modify: `BasicLang/SemanticAnalyzer.cs` (Decimal-context detection per spec §6.1's context list)
- Modify: `BasicLang/IRBuilder.cs` (constant construction honors the analyzed Decimal type: `decimal.Parse(lit.Text, CultureInfo.InvariantCulture)` into `IRConstant.Value`)
- Modify: `BasicLang/CSharpBackend.cs` (`EmitConstant` ~3624: `if (constant.Value is decimal d) return d.ToString(CultureInfo.InvariantCulture) + "m";` FIRST; CType lowering site: Grep `ConvertMethodForType` call sites / how `CType` lowers today, add the Decimal target)
- Test: append to `NativeBclFrontEndTests.cs`

- [ ] **Step 1: Failing tests**:

```csharp
[Test] public void DecimalLiteral_InOperandPosition_Works()          // d * 1.08 (the money pattern)
    => Assert.That(CompileRunCSharp(MoneyProgram()), Is.EqualTo("21.5892"));
    // MoneyProgram: Dim d As Decimal = 19.99 : Console.WriteLine(d * 1.08) — expected output pinned
    // by REAL .NET (verified): 19.99m*1.08m = 21.5892 (scale 2+2=4); WriteLine prints "21.5892".
    // Rule: NEVER hand-round an expectation — real .NET output is the oracle.

[Test] public void DecimalLiteral_ArgumentReturnAndForStepContexts_Work()
    // covers the remaining §6.1 context list: literal as argument to a Decimal parameter,
    // Return 1.5 from a Decimal function, and For d As Decimal = 0 To 1 Step 0.25 (loop count 5).
    // One program printing all three results; expectations verified against real .NET first.

[Test] public void DecimalLiteral_ScalePreserved()                   // "1.50" stays scale-2
    => Assert.That(CompileRunCSharp("...Console.WriteLine(CStr(1.50))..." /* Dim x As Decimal = 1.50 : WriteLine(x) */), Is.EqualTo("1.50"));

[Test] public void Optimizer_DoesNotFoldDecimalInDoubleSpace()
{
    // Build IR for: Dim a As Decimal = 0.1 : Dim b As Decimal = 0.2 : Dim c As Decimal = a + b
    // Run OptimizationPipeline (AddStandardPasses) — assert no IRConstant with a double value 0.30000000000000004
    // appears; the fold must skip (Value is decimal, 'is double' patterns miss). Assert c's defining op survives
    // OR the folded constant is EXACTLY 0.3m as a decimal.
}

[Test] public void CType_DoubleToDecimal_LowersOnCSharp()
    => Assert.That(CompileRunCSharp("...Dim x As Double = 1.5 : Dim d As Decimal = CType(x, Decimal) : Console.WriteLine(d)..."), Is.EqualTo("1.5"));
```

IMPORTANT: before writing the money test, RUN the real .NET expression in a scratch C# program to pin the exact expected string (spec §14.2 — the parity rule is "match real .NET", never hand-compute).

- [ ] **Step 2: Red** (literal contexts not detected → analysis errors or wrong output "1.5" for scale test).
- [ ] **Step 3: Implement.** Decimal-context detection in the analyzer per the spec §6.1 list (Dim init / assignment / operand-with-Decimal / argument / Return / For-Step). Mechanically: when a literal expression's inferred Double/Integer type meets a Decimal context, RETYPE the literal node to Decimal (the analyzer records node types — find where literal types are assigned and add the context-sensitive override; the Text is available from Task 1; store the parsed decimal on the node or convert in IRBuilder from Text when the node's type says Decimal). In IRBuilder: when building the constant for a Decimal-typed literal, `decimal.Parse(node.Text!, CultureInfo.InvariantCulture)`. Null-Text fallback (defensive): convert the double value — but add a Debug.Assert; Task 1 guarantees Text for numeric literals.
- [ ] **Step 4: Green + fast subset. Also verify the optimizer path explicitly**: the fold test IS the guard; additionally Read `IROptimizer.cs` fold sites (~294–451) to confirm the `is double` patterns and note in the commit message that decimal constants skip.
- [ ] **Step 5: Commit** — `git commit -m "feat(p1): Decimal literals from source text; System.Decimal IR constants; m-suffix C# emission; CType(x, Decimal)"`

---

### Task 5: NativeBclSurface + DateTime/TimeSpan operator rows + member typing + stdlib registrations

Spec §4 + §6.1 table + §7 item 2. Creates the single-source table and wires: member typing (LookupNetTypeMember FIRST for the seven P1 names), the DateTime/TimeSpan/DateTimeOffset operator rows in the binary/compound gates, DateTimeOffset into `CommonNetTypes` (~SemanticAnalyzer.cs:67–96) and `KnownNetStaticTypes` (~IRBuilder.cs:3619–3639), and the VB date-function analyzer registrations (typing only — C# emissions already exist; C++ emissions come in Task 12). End-to-end on the C# backend.

**Files:**
- Create: `BasicLang/NativeBclSurface.cs` — the table per spec §5's per-type lists EXACTLY (including: DayOfWeek/Kind typed `Integer`; NO TryParse anywhere; NO `ToByteArray` on Guid (spec §5: not BL-callable in v1 — the native out-param form is tests/P2-only); StringBuilder `= <>`; the §6.1 operator rows incl. `(unary) -` on TimeSpan and Decimal). Shape per plan header note 3. Include a doc comment: "single source (spec §4); consumed by analyzer typing, operator gates, CppCapabilityChecker member pass (Task 10), codegen dispatch (Task 9), drift tests."
- Modify: `BasicLang/SemanticAnalyzer.cs`, `BasicLang/IRBuilder.cs`, `BasicLang/CSharpBackend.cs` (the `(int)` cast for DayOfWeek/Kind per spec §5)
- Test: append to `NativeBclFrontEndTests.cs`

- [ ] **Step 1: Failing tests** (representative — write all of these):

```csharp
// dt2 - dt1 → TimeSpan; dt + ts → DateTime; compound dt += ts; comparisons
[Test] public void DateTime_CrossTypeOperators_TypeAndRun()
    => Assert.That(CompileRunCSharp(@"
Module M
 Sub Main()
  Dim d1 As New DateTime(2026, 1, 1)
  Dim d2 As New DateTime(2026, 1, 31)
  Dim ts As TimeSpan = d2 - d1
  Console.WriteLine(ts.Days)
  Dim d3 As DateTime = d1 + ts
  Console.WriteLine(d3.Day)
  d1 += ts
  Console.WriteLine(d1.Day)
  Console.WriteLine(d1 < d2)
 End Sub
End Module"), Is.EqualTo("30\n31\n31\nFalse").Or.EqualTo("30\r\n31\r\n31\r\nFalse"));
// Normalize newlines in the helper instead of Or-chains.

[Test] public void MemberChain_TypesOnCompilePath()  // d.AddDays(1).Year : Integer, not Object
    => AssertAnalyzesClean("Dim d As New DateTime(2026,1,1)\nDim y As Integer = d.AddDays(1).Year");

[Test] public void UnknownMember_StillPermissive_OnCSharp()  // surface misses ≠ analyzer error on C# (csc-late model unchanged)
    => AssertAnalyzesClean("Dim d As New DateTime(2026,1,1)\nDim x = d.ToBinary()");

[Test] public void DayOfWeek_TypesInteger_And_PrintsNumber_OnCSharp()
    => Assert.That(CompileRunCSharp("... Console.WriteLine(New DateTime(2026, 7, 26).DayOfWeek) ..."), Is.EqualTo("0")); // Sunday=0; requires the (int) cast in C# emission

[Test] public void StdlibDateFunctions_TypeAsDateTime()
    => AssertAnalyzesClean("Dim d As DateTime = Now()\nDim y As Integer = Year(d)");

[Test] public void Kind_TypesInteger_And_PrintsNumber_OnCSharp()   // the second (int)-cast member
    => Assert.That(CompileRunCSharp("... Console.WriteLine(DateTime.UtcNow.Kind) ..."), Is.EqualTo("1")); // Utc=1

[Test] public void GuidStringBuilder_EqualityTypes_OrderingStaysError()
{
    AssertAnalyzesClean("Dim g1 As Guid = Guid.NewGuid()\nDim g2 As Guid = g1\nDim eq As Boolean = g1 = g2");
    AssertAnalysisError("Dim g1 As Guid = Guid.NewGuid()\nDim g2 As Guid = g1\nDim x = g1 < g2", expectContains: new[] { "operator" });
    AssertAnalyzesClean("Dim sb1 As New StringBuilder()\nDim sb2 As StringBuilder = sb1\nDim eq As Boolean = sb1 = sb2");
    AssertAnalysisError("Dim sb1 As New StringBuilder()\nDim sb2 As StringBuilder = sb1\nDim x = sb1 < sb2", expectContains: new[] { "operator" });
}

[Test] public void SurfaceRegistryCoherence()  // drift test, both directions (spec §4.5)
{
    // every NativeOwned name in BoundaryTypeRegistry has ≥1 surface entry and vice versa.
    // NOTE: NativeOwned is EMPTY until Task 10 — assert instead against the surface table's own
    // declared type list {DateTime, TimeSpan, Guid, StringBuilder, Decimal, DateTimeOffset} and
    // leave a TODO(Task 10) to re-point at the registry; Task 10 flips this assertion.
}
```

- [ ] **Step 2: Red.** (Operators fail "requires numeric operands"; chain types Object so the `As Integer` assignment errors or the C# temp breaks; DayOfWeek prints "Sunday".)
- [ ] **Step 3: Implement** — table first; then binary/compound gate consults the (LeftType, Op, RightType) rows before the numeric path; `LookupNetTypeMember` consults the surface FIRST for the seven names (return typed results; MISSES fall through to existing behavior — the C# backend stays permissive, spec's clean-diagnostic enforcement is C++-only via Task 10's checker pass); `GetCommonMethodReturnType` untouched (StringBuilder rows already there — keep, the surface supersedes for the seven names but must AGREE: copy the StringBuilder rows into the surface identically). CSharpBackend: when emitting a member access whose surface-declared type diverges from real .NET (exactly DayOfWeek/Kind — drive it off a `RequiresCSharpIntCast` flag on those two surface entries, not name matching), wrap in `(int)(...)`. Stdlib registrations: mirror `CSharpStdLib.cs` (~95–106) signatures into `RegisterStdLibFunction` calls — READ the C# table and copy return types VERBATIM (verified: `DateDiff` returns **Integer** in the repo table, not VB.NET's Long; the repo table wins).
- [ ] **Step 4: Green + fast subset** (member-typing changes can shift C#-backend temp types Object→concrete — the suite is the blast-radius gate; investigate ANY failure, don't paper). If a SHIM-ERA C++ pin fails here (`Cpp_ConsoleTemplateSurface_LowersToValidCpp` — it consumes the same analyzed types), the typing change reached the live shim path: reconcile deliberately (prefer keeping the shim IR-shape-keyed until Task 10); do NOT silently pull Task 10's re-pin forward.
- [ ] **Step 5: Commit** — `git commit -m "feat(p1): NativeBclSurface single-source table; DateTime/TimeSpan operator gates; surface-backed member typing; stdlib date typing"`

---

### Task 6: CppBclRuntime — DateTime + TimeSpan headers (native-only tests)

Spec §3/§9/§12 layer 2. New runtime-source class holding the C++ text; NOT wired into codegen yet (Task 9). Tests compile the constants directly via `CppCompile.CompileAndRun` with the header passed as `extraFiles` — the `BlnetNativeRuntimeTests` pattern (Read that fixture first and copy its `Run` helper shape; markers are value-independent comparisons, `[Category("Integration")]`).

**Files:**
- Create: `BasicLang/Compiler/CodeGen/CPlusPlus/CppBclRuntime.cs` — `public static string BclHeader => ...` (whole-file property, BlnetRuntimeSources pattern), header name `bl_bcltypes.hpp`
- Test: create `VisualGameStudio.Tests/Compiler/CppBclRuntimeTests.cs`

**The header's DateTime/TimeSpan design (write exactly this; the TDD loop fixes transcription errors):**

```cpp
/* bl_bcltypes.hpp — native BCL value types (P1). Header-only C++20.
   SOURCE OF TRUTH: BasicLang CppBclRuntime.cs — do not edit the emitted copy. */
#pragma once
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <ctime>
#include <chrono>
#include <functional>
#include <memory>
#include <ostream>
#include <stdexcept>
#include <string>

namespace BasicLang {

/* ---- TimeSpan: one int64 ticks (100ns). Spec §3. ---- */
struct TimeSpan {
    int64_t ticks_ = 0;
    static constexpr int64_t TicksPerMillisecond = 10'000;
    static constexpr int64_t TicksPerSecond = 10'000'000;
    static constexpr int64_t TicksPerMinute = TicksPerSecond * 60;
    static constexpr int64_t TicksPerHour   = TicksPerMinute * 60;
    static constexpr int64_t TicksPerDay    = TicksPerHour * 24;

    TimeSpan() = default;
    explicit TimeSpan(int64_t ticks) : ticks_(ticks) {}
    TimeSpan(int32_t h, int32_t m, int32_t s) : ticks_(((int64_t)h*3600 + (int64_t)m*60 + s) * TicksPerSecond) {}
    TimeSpan(int32_t d, int32_t h, int32_t m, int32_t s)
        : ticks_((int64_t)d*TicksPerDay + ((int64_t)h*3600 + (int64_t)m*60 + s) * TicksPerSecond) {}

    static TimeSpan FromTicks(int64_t t) { return TimeSpan(t); }
    /* double-based factories round to the NEAREST MILLISECOND (.NET rule, spec §5) */
    static TimeSpan FromDays(double v)         { return Interval(v, TicksPerDay); }
    static TimeSpan FromHours(double v)        { return Interval(v, TicksPerHour); }
    static TimeSpan FromMinutes(double v)      { return Interval(v, TicksPerMinute); }
    static TimeSpan FromSeconds(double v)      { return Interval(v, TicksPerSecond); }
    static TimeSpan FromMilliseconds(double v) { return Interval(v, TicksPerMillisecond); }
    static TimeSpan Zero() { return TimeSpan(0); }
    static TimeSpan MinValue() { return TimeSpan(INT64_MIN); }
    static TimeSpan MaxValue() { return TimeSpan(INT64_MAX); }
    static TimeSpan Parse(const std::string& s);   /* "c" format: [-][d.]hh:mm:ss[.fffffff] */

    int64_t Ticks() const { return ticks_; }
    int32_t Days() const    { return (int32_t)(ticks_ / TicksPerDay); }
    int32_t Hours() const   { return (int32_t)((ticks_ / TicksPerHour) % 24); }
    int32_t Minutes() const { return (int32_t)((ticks_ / TicksPerMinute) % 60); }
    int32_t Seconds() const { return (int32_t)((ticks_ / TicksPerSecond) % 60); }
    int32_t Milliseconds() const { return (int32_t)((ticks_ / TicksPerMillisecond) % 1000); }
    double TotalDays() const    { return (double)ticks_ / TicksPerDay; }
    double TotalHours() const   { return (double)ticks_ / TicksPerHour; }
    double TotalMinutes() const { return (double)ticks_ / TicksPerMinute; }
    double TotalSeconds() const { return (double)ticks_ / TicksPerSecond; }
    double TotalMilliseconds() const { return (double)ticks_ / TicksPerMillisecond; }

    TimeSpan Add(const TimeSpan& o) const { return CheckedAdd(ticks_, o.ticks_); }
    TimeSpan Subtract(const TimeSpan& o) const { return CheckedAdd(ticks_, -o.ticks_); }
    TimeSpan Negate() const { if (ticks_ == INT64_MIN) throw std::runtime_error("TimeSpan overflow: negating MinValue"); return TimeSpan(-ticks_); }
    TimeSpan Duration() const { return ticks_ < 0 ? Negate() : *this; }
    int32_t CompareTo(const TimeSpan& o) const { return ticks_ < o.ticks_ ? -1 : (ticks_ > o.ticks_ ? 1 : 0); }
    std::string ToString() const;                  /* "c" invariant format */

    TimeSpan operator+(const TimeSpan& o) const { return Add(o); }
    TimeSpan operator-(const TimeSpan& o) const { return Subtract(o); }
    TimeSpan operator-() const { return Negate(); }
    bool operator==(const TimeSpan& o) const = default;
    auto operator<=>(const TimeSpan& o) const = default;

private:
    static TimeSpan Interval(double v, int64_t scaleTicks);   /* v*scale rounded to nearest ms; NaN/overflow throw */
    static TimeSpan CheckedAdd(int64_t a, int64_t b);         /* overflow -> throw std::runtime_error */
};

/* ---- DateTime: uint64 = ticks (low 62) | kind (top 2). Spec §3. ---- */
struct DateTime {
    uint64_t dateData_ = 0;
    static constexpr uint64_t TicksMask = 0x3FFFFFFFFFFFFFFFULL;
    static constexpr int32_t KindUnspecified = 0, KindUtc = 1, KindLocal = 2;
    static constexpr int64_t MaxTicks = 3155378975999999999LL;   /* 9999-12-31T23:59:59.9999999 */

    DateTime() = default;
    DateTime(int32_t y, int32_t mo, int32_t d) { Init(y, mo, d, 0, 0, 0); }
    DateTime(int32_t y, int32_t mo, int32_t d, int32_t h, int32_t mi, int32_t s) { Init(y, mo, d, h, mi, s); }
    static DateTime FromTicksAndKind(int64_t ticks, int32_t kind);   /* range-checks ticks */

    int64_t Ticks() const { return (int64_t)(dateData_ & TicksMask); }
    int32_t Kind() const { return (int32_t)(dateData_ >> 62); }

    static DateTime Now();      /* local wall clock, KindLocal (OS-backed, spec §9) */
    static DateTime UtcNow();   /* KindUtc */
    static DateTime Today();    /* Now() date component, KindLocal */
    static DateTime MinValue() { return DateTime(); }
    static DateTime MaxValue() { return FromTicksAndKind(MaxTicks, KindUnspecified); }
    static bool IsLeapYear(int32_t y) { if (y < 1 || y > 9999) throw std::runtime_error("year out of range"); return (y % 4 == 0 && y % 100 != 0) || y % 400 == 0; }
    static int32_t DaysInMonth(int32_t y, int32_t m);
    static DateTime Parse(const std::string& s);   /* invariant O / s / G / yyyy-MM-dd (spec §9) */

    int32_t Year() const;  int32_t Month() const;  int32_t Day() const;
    int32_t Hour() const   { return (int32_t)((Ticks() / TimeSpan::TicksPerHour) % 24); }
    int32_t Minute() const { return (int32_t)((Ticks() / TimeSpan::TicksPerMinute) % 60); }
    int32_t Second() const { return (int32_t)((Ticks() / TimeSpan::TicksPerSecond) % 60); }
    int32_t Millisecond() const { return (int32_t)((Ticks() / TimeSpan::TicksPerMillisecond) % 1000); }
    int32_t DayOfWeek() const { return (int32_t)((Ticks() / TimeSpan::TicksPerDay + 1) % 7); } /* 0001-01-01 was Monday; Sunday=0 */
    int32_t DayOfYear() const;
    DateTime Date() const { return FromTicksAndKind(Ticks() - Ticks() % TimeSpan::TicksPerDay, Kind()); }

    DateTime AddTicks(int64_t t) const { return FromTicksAndKind(CheckedTicks(Ticks() + t), Kind()); }
    DateTime AddMilliseconds(double v) const { return AddScaled(v, TimeSpan::TicksPerMillisecond); }
    DateTime AddSeconds(double v) const { return AddScaled(v, TimeSpan::TicksPerSecond); }
    DateTime AddMinutes(double v) const { return AddScaled(v, TimeSpan::TicksPerMinute); }
    DateTime AddHours(double v) const   { return AddScaled(v, TimeSpan::TicksPerHour); }
    DateTime AddDays(double v) const    { return AddScaled(v, TimeSpan::TicksPerDay); }
    DateTime AddMonths(int32_t m) const;   /* calendar op, day CLAMPED (Jan 31 + 1mo = Feb 28/29), spec §3 */
    DateTime AddYears(int32_t y) const { return AddMonths(y * 12); }
    DateTime Add(const TimeSpan& ts) const { return AddTicks(ts.Ticks()); }
    TimeSpan Subtract(const DateTime& o) const { return TimeSpan(Ticks() - o.Ticks()); }
    DateTime Subtract(const TimeSpan& ts) const { return AddTicks(-ts.Ticks()); }
    DateTime ToUniversalTime() const;   /* OS-backed; KindUtc treated as already-UTC; Unspecified assumed local (.NET rule) */
    DateTime ToLocalTime() const;       /* KindLocal already-local; Unspecified assumed UTC (.NET rule) */
    int32_t CompareTo(const DateTime& o) const { auto a = Ticks(), b = o.Ticks(); return a < b ? -1 : (a > b ? 1 : 0); }
    std::string ToString() const;                     /* invariant G: MM/dd/yyyy HH:mm:ss (spec §9) */
    std::string ToString(const std::string& fmt) const; /* token formatter: yyyy MM dd HH mm ss fff fffffff + literals; O/o/s shortcuts */

    /* ticks-only comparison; Kind is metadata (spec §3) */
    DateTime operator+(const TimeSpan& ts) const { return Add(ts); }
    DateTime operator-(const TimeSpan& ts) const { return Subtract(ts); }
    TimeSpan operator-(const DateTime& o) const { return Subtract(o); }
    bool operator==(const DateTime& o) const { return Ticks() == o.Ticks(); }
    bool operator!=(const DateTime& o) const { return Ticks() != o.Ticks(); }
    bool operator<(const DateTime& o) const  { return Ticks() < o.Ticks(); }
    bool operator<=(const DateTime& o) const { return Ticks() <= o.Ticks(); }
    bool operator>(const DateTime& o) const  { return Ticks() > o.Ticks(); }
    bool operator>=(const DateTime& o) const { return Ticks() >= o.Ticks(); }

private:
    void Init(int32_t y, int32_t mo, int32_t d, int32_t h, int32_t mi, int32_t s);  /* validates; throws on month 13 etc. */
    static int64_t CheckedTicks(int64_t t);        /* 0..MaxTicks or throw */
    DateTime AddScaled(double v, int64_t scale) const;  /* rounds to nearest ms like .NET Add(double) */
    /* civil-date math: days_from_civil / civil_from_days (Howard Hinnant algorithms, public domain);
       days since 0001-01-01 = days_from_civil(y,m,d) - days_from_civil(1,1,1) */
};

inline std::ostream& operator<<(std::ostream& os, const TimeSpan& v) { return os << v.ToString(); }
inline std::ostream& operator<<(std::ostream& os, const DateTime& v) { return os << v.ToString(); }

} /* namespace BasicLang */

template<> struct std::hash<BasicLang::TimeSpan> {
    size_t operator()(const BasicLang::TimeSpan& v) const noexcept { return std::hash<int64_t>{}(v.ticks_); }
};
template<> struct std::hash<BasicLang::DateTime> {   /* ticks only — Kind excluded, matches equality (spec §6.2) */
    size_t operator()(const BasicLang::DateTime& v) const noexcept { return std::hash<int64_t>{}(v.Ticks()); }
};
```

Method bodies not shown inline above (`Init`, `Year/Month/Day/DayOfYear` via civil_from_days, `AddMonths` with clamp, `Now/UtcNow/Today/ToLocalTime/ToUniversalTime` via `time()`/`localtime_s`/`gmtime`/`mktime` — with out-of-range instants THROWING per spec §9, `ToString`/`Parse`, `Interval`, `CheckedAdd`) are written by the executor against the Step 1 vectors; the civil-date algorithms are the standard Hinnant `days_from_civil`/`civil_from_days` (~15 lines each — transcribe from the well-known form). **ODR rule:** define bodies in-class, or mark out-of-class header definitions `inline` — the header is included by MULTIPLE TUs in split emission; a missing `inline` stays green in Task 6's single-TU tests and explodes as duplicate-symbol link errors only at Task 9's split smoke.

- [ ] **Step 1: Write the failing native tests.** Fixture `CppBclRuntimeTests` (`[Category("Integration")]`), `Run(mainBody)` helper compiling `#include "bl_bcltypes.hpp"` + body with `extraFiles = { ["bl_bcltypes.hpp"] = CppBclRuntime.BclHeader }`. Marker-style programs (value-independent comparisons). Vectors, one test each:
  - **Calendar**: `DateTime(2024,2,29)` OK; `DateTime(2023,2,29)` throws; `DaysInMonth(2024,2)==29`; `IsLeapYear(1900)==false`, `(2000)==true`; `DateTime(2026,7,26).DayOfWeek()==0` (Sunday — verified real .NET); `DateTime(2026,1,1).DayOfYear()==1`; `DateTime(2026,12,31).DayOfYear()==365`.
  - **Round-trip**: `FromTicksAndKind(dt.Ticks(), k)` reproduces Y/M/D/H/M/S for a spread of dates incl. 0001-01-01, 9999-12-31, 1970-01-01, 2000-02-29.
  - **AddMonths clamp**: `DateTime(2026,1,31).AddMonths(1)` → Feb 28 2026; `(2024,1,31).AddMonths(1)` → Feb 29 2024.
  - **Arithmetic**: `(d2-d1).Days()==30` for Jan 1→31; `d1 + TimeSpan::FromDays(1.0)` → next day; comparisons ticks-only across Kind.
  - **TimeSpan components vs totals**: `FromSeconds(90).Minutes()==1`, `.Seconds()==30`, `.TotalMinutes()==1.5`; sign: `FromSeconds(-90).Minutes()==-1`.
  - **FromMilliseconds rounding**: `FromSeconds(0.0001).Ticks()==0` (rounds to ms — the documented .NET trap).
  - **ToString/Parse**: `DateTime(2026,7,26,13,5,9).ToString()` == `"07/26/2026 13:05:09"`; `ToString("yyyy-MM-dd")` == `"2026-07-26"`; `Parse` of both round-trips; `Parse` also accepts the "O" round-trip (`"2026-07-26T13:05:09.0000000"`) and "s" sortable (`"2026-07-26T13:05:09"`) forms (spec §9); TimeSpan `"c"`: `TimeSpan(1,2,3).ToString()=="01:02:03"`, `TimeSpan(1,1,2,3).ToString()=="1.01:02:03"`, negative gets `-`.
  - **ostream + hash**: `cout << dt` equals `dt.ToString()`; `std::hash<DateTime>` equal for equal ticks with different Kind.
  - **Overflow/exceptions**: `MaxValue().AddDays(1.0)` throws; `TimeSpan::MinValue().Negate()` throws; caught as `std::runtime_error` (spec §11).
  - **Local time**: `Now().Kind()==KindLocal`; `UtcNow().Kind()==KindUtc`; `ToUniversalTime(ToLocalTime(x))` tick-stable for a contemporary date; a pre-1970 `ToLocalTime` THROWS (spec §9) — assert the throw, message contains "range".
- [ ] **Step 2: Red** (type missing) → create `CppBclRuntime.cs` with the header text; iterate compile+behavior to green, one vector-test at a time.
- [ ] **Step 3: Fast content pins** (spec §12 layer 1) — a FAST fixture section (no Category) asserting `CppBclRuntime.BclHeader` `Does.Contain` the load-bearing markers: the SOURCE-OF-TRUTH banner, `struct TimeSpan`, `struct DateTime`, `std::hash<BasicLang::DateTime>`, the ostream inserters. (Task 8 adds the same for the Decimal header; Task 9 adds the both-modes-emission pin.)
- [ ] **Step 4: Full fixture green.** — `dotnet test ... --filter "FullyQualifiedName~CppBclRuntimeTests" > t.txt 2>&1` (timeout 600000).
- [ ] **Step 5: Commit** — `git commit -m "feat(p1): native DateTime+TimeSpan (bl_bcltypes.hpp) with calendar/arithmetic/format vector tests"`

---

### Task 7: CppBclRuntime — Guid, DateTimeOffset, StringBuilder

Extend `bl_bcltypes.hpp` (same file/constant) per spec §3/§5.

> **Riders from Task 6's reviews (same file, do here):** (1) `TimeSpan::Parse`
> day-magnitude guard — 8 day digits can overflow `days * TicksPerDay` (UB, not
> the §11 runtime_error): guard `days <= 10675199` and route the accumulation
> through `CheckedAdd`; ALSO make `TimeSpan::MinValue().ToString()` round-trip
> through Parse (the positive accumulation overflows by one tick before
> negation) — add both as vectors. (2) `Interval`'s upper bound is exclusive
> where .NET's is inclusive (`millis == 922337203685477` must be accepted) —
> one-value fix + vector. (3) Drop `<chrono>`/`<memory>` from the includes if
> Task 7 doesn't consume them.

**Design (write exactly this, bodies per vectors):**

```cpp
/* ---- Guid: 16 bytes, .NET field layout. Spec §3. ---- */
struct Guid {
    int32_t a_ = 0; int16_t b_ = 0, c_ = 0; uint8_t d_[8] = {};
    Guid() = default;
    explicit Guid(const std::string& s) { *this = Parse(s); }  /* spec §5 ctor (String); New Guid("...") lowers here */
    static Guid NewGuid();                    /* v4 from OS CSPRNG: BCryptGenRandom (Windows) /
                                                 getrandom//dev/urandom (else). NEVER rand()/mt19937.
                                                 version nibble := 4, variant := 10xx (RFC 4122). */
    static Guid Empty() { return Guid(); }
    static Guid Parse(const std::string& s);  /* accepts D/N/B/P; throws on bad input */
    std::string ToString() const;             /* "D": lowercase 8-4-4-4-12 */
    std::string ToString(const std::string& fmt) const;   /* D N B P */
    /* NATIVE-ONLY (not on the BL surface, spec §5): tests + the §8 conversion pair use it */
    void ToByteArray(uint8_t out[16]) const;  /* .NET order: a,b,c little-endian then d_ verbatim (spec §8) */
    int32_t CompareTo(const Guid& o) const;   /* field-by-field a,b,c then bytes — NOT memcmp of ToByteArray */
    bool operator==(const Guid& o) const = default;
};

/* ---- DateTimeOffset: UTC DateTime + offset minutes. Spec §3. ---- */
struct DateTimeOffset {
    DateTime utc_;               /* stores the UTC instant, KindUnspecified */
    int16_t offsetMinutes_ = 0;  /* ±14h, whole minutes; ctor validates */
    DateTimeOffset() = default;
    explicit DateTimeOffset(const DateTime& dt);                 /* .NET Kind rules: Utc→offset 0; Local/Unspecified→local zone offset */
    DateTimeOffset(const DateTime& clockTime, const TimeSpan& offset);  /* validates offset; Kind rules per spec §3 sources (.NET: Utc+nonzero throws) */
    static DateTimeOffset Now();  static DateTimeOffset UtcNow();
    static DateTimeOffset FromUnixTimeSeconds(int64_t s);
    static DateTimeOffset FromUnixTimeMilliseconds(int64_t ms);
    DateTime UtcDateTime() const { return utc_; }
    DateTime LocalDateTime() const;
    DateTime ClockDateTime() const;           /* surfaced to BL as the 'DateTime' property */
    TimeSpan Offset() const { return TimeSpan((int64_t)offsetMinutes_ * TimeSpan::TicksPerMinute); }
    int64_t TicksValue() const { return ClockDateTime().Ticks(); }  /* BL 'Ticks' property = clock ticks (.NET) */
    DateTimeOffset ToOffset(const TimeSpan& o) const;
    DateTimeOffset ToUniversalTime() const { return DateTimeOffset(utc_, TimeSpan::Zero()); }
    DateTimeOffset ToLocalTime() const;
    int64_t ToUnixTimeSeconds() const;  int64_t ToUnixTimeMilliseconds() const;
    std::string ToString() const;             /* invariant: MM/dd/yyyy HH:mm:ss zzz (+HH:mm) */
    int32_t CompareTo(const DateTimeOffset& o) const { return utc_.CompareTo(o.utc_); }
    /* equality/ordering compare the UTC instant (spec §3) */
    bool operator==(const DateTimeOffset& o) const { return utc_ == o.utc_; }
    bool operator!=(const DateTimeOffset& o) const { return !(*this == o); }
    bool operator<(const DateTimeOffset& o) const  { return utc_ < o.utc_; }
    bool operator<=(const DateTimeOffset& o) const { return utc_ <= o.utc_; }
    bool operator>(const DateTimeOffset& o) const  { return utc_ > o.utc_; }
    bool operator>=(const DateTimeOffset& o) const { return utc_ >= o.utc_; }
};

/* ---- StringBuilder: the ONE reference type. Spec §3. UTF-8 byte semantics (spec §9). ---- */
class StringBuilder : public std::enable_shared_from_this<StringBuilder> {
    std::string buf_;
public:
    StringBuilder() = default;
    explicit StringBuilder(const std::string& s) : buf_(s) {}
    /* Append family returns shared_from_this() so chains emit uniformly with -> (spec §3).
       NB: requires the object to be OWNED by a shared_ptr — codegen always constructs via
       make_shared (Task 9), and the runtime tests must too. */
    std::shared_ptr<StringBuilder> Append(const std::string& s) { buf_ += s; return shared_from_this(); }
    std::shared_ptr<StringBuilder> Append(int32_t v) { buf_ += std::to_string(v); return shared_from_this(); }  /* REQUIRED: without it, Append(Integer) is ambiguous (int32->int64 and int32->double are both rank Conversion) */
    std::shared_ptr<StringBuilder> Append(int64_t v) { buf_ += std::to_string(v); return shared_from_this(); }
    std::shared_ptr<StringBuilder> Append(bool v) { buf_ += (v ? "True" : "False"); return shared_from_this(); } /* else bool promotes to int and prints 1/0 vs .NET True/False */
    std::shared_ptr<StringBuilder> Append(double v);   /* invariant formatting, matches the backend's existing double->string style */
    std::shared_ptr<StringBuilder> AppendLine(const std::string& s = "") { buf_ += s; buf_ += "\n"; return shared_from_this(); }
    std::shared_ptr<StringBuilder> AppendFormat(const std::string& fmt, const std::string& a0); /* {0} only, v1 */
    std::shared_ptr<StringBuilder> Insert(int32_t index, const std::string& s);   /* byte index; range-checked throw */
    std::shared_ptr<StringBuilder> Remove(int32_t start, int32_t len);            /* range-checked */
    std::shared_ptr<StringBuilder> Replace(const std::string& oldV, const std::string& newV);
    std::shared_ptr<StringBuilder> Clear() { buf_.clear(); return shared_from_this(); }
    std::string ToString() const { return buf_; }
    int32_t Length() const { return (int32_t)buf_.size(); }   /* UTF-8 BYTES, documented divergence */
    int32_t Capacity() const { return (int32_t)buf_.capacity(); }
};
inline std::ostream& operator<<(std::ostream& os, const std::shared_ptr<StringBuilder>& sb) {
    return os << (sb ? sb->ToString() : std::string());
}
/* spec §6.2 requires inserters for ALL FIVE value structs — Guid and DateTimeOffset too: */
inline std::ostream& operator<<(std::ostream& os, const Guid& v) { return os << v.ToString(); }
inline std::ostream& operator<<(std::ostream& os, const DateTimeOffset& v) { return os << v.ToString(); }

} /* namespace BasicLang (close before the hash specializations) */

template<> struct std::hash<BasicLang::Guid> {
    size_t operator()(const BasicLang::Guid& v) const noexcept {
        uint8_t b[16]; v.ToByteArray(b);
        size_t h = 1469598103934665603ULL;                    /* FNV-1a over all 16 bytes */
        for (uint8_t x : b) { h ^= x; h *= 1099511628211ULL; }
        return h;
    }
};
template<> struct std::hash<BasicLang::DateTimeOffset> {      /* UTC instant — matches equality (spec §6.2) */
    size_t operator()(const BasicLang::DateTimeOffset& v) const noexcept {
        return std::hash<int64_t>{}(v.UtcDateTime().Ticks());
    }
};
```

- [ ] **Step 1: Failing native tests** (append to `CppBclRuntimeTests`):
  - **Guid**: `NewGuid()` twice → unequal; version nibble '4' and variant in {8,9,a,b} at the pinned string positions; `Parse(g.ToString()) == g`; `Guid("...")` string-ctor equals `Parse("...")`; `ToString()` lowercase D-format shape (regex via C++ manual check or length+dash positions); `ToByteArray` of `Parse("00112233-4455-6677-8899-aabbccddeeff")` == the .NET byte order `33 22 11 00 55 44 77 66 88 99 aa bb cc dd ee ff` (spec §8 pin); `Empty().ToString()=="00000000-0000-0000-0000-000000000000"`; CompareTo consistent with field order; `cout << g` equals `g.ToString()`; `std::hash<Guid>` equal for equal guids.
  - **DateTimeOffset**: `DateTimeOffset(DateTime(2026,1,1,10,0,0), TimeSpan::FromHours(2.0)) == DateTimeOffset(DateTime(2026,1,1,9,0,0), TimeSpan::FromHours(1.0))` (UTC-instant equality — the spec's own example); `FromUnixTimeSeconds(0).UtcDateTime()` == 1970-01-01; `ToUnixTimeSeconds` round-trip; offset > 14h throws; `ToOffset` preserves the instant; `cout << dto` equals `dto.ToString()`; `std::hash<DateTimeOffset>` equal for equal instants at different offsets.
  - **StringBuilder**: chaining `make_shared<StringBuilder>()->Append("a")->Append("b")->AppendLine("c")` → ToString `"abc\n"`; ALIASING: two `shared_ptr` to one builder observe each other's Append (the reference-semantics proof); `Insert/Remove` byte-index behavior + out-of-range throws (`std::runtime_error`); `Replace` all occurrences; `Length()` counts bytes for a `\xC3\xA9` payload (==2 for "é").
- [ ] **Step 2: Red → implement → green** (same loop; full fixture re-run green).
- [ ] **Step 3: Commit** — `git commit -m "feat(p1): native Guid+DateTimeOffset+StringBuilder in bl_bcltypes.hpp with vector tests"`

---

### Task 8: CppDecimalRuntime — the faithful 96-bit engine

Spec §10. Own file/constant (`bl_decimal.hpp`), included BY `bl_bcltypes.hpp`? NO — keep independent: `CppDecimalRuntime.DecimalHeader` is a sibling header; Task 9 splices both. This is the plan's flagged mechanism decision 1: complete public surface + representation + exact algorithms here; limb-helper bodies are executor-implemented against the LOCKED vectors below. **Verify every nontrivial vector against real .NET first** (write a scratch C# program printing each expression; spec §14.2) — the vectors below were review-checked but re-verification is cheap insurance.

**Representation** (exact): `struct Decimal { uint32_t lo_, mid_, hi_, flags_; }` — flags bits 16–23 = scale (0–28), bit 31 = sign, all others zero. `GetBits` order `{lo, mid, hi, flags}` (spec §8).

**Public surface** (all members from spec §5's Decimal list — explicitly including `static Compare(a, b)` returning −1/0/1): ctors from `int32_t/int64_t` (exact, scale 0), `Decimal(double)` explicit converting ctor (the CType path — .NET's observable rounding: format the double with shortest round-trip `%.17g`-then-trim or `std::to_chars`, parse the text; pin exact behavior by parity vectors), **`double ToDouble() const`** (the REVERSE direction spec §10 pins with `↔` — Task 9 lowers `CType(d, Double)` to it via Visit(IRCast), and `CDbl(d)`/`CSng(d)` to it via their EmitStdLibCall intrinsic arms — the C* intrinsics do NOT pass through IRCast (Task 9 review rider); without it those conversions pass analysis and die as `static_cast` on a struct), `static Decimal FromParts(uint32_t lo, uint32_t mid, uint32_t hi, bool neg, uint8_t scale)` (the literal-emission entry Task 9 uses), `Parse`, `ToString`, `Round(d[,digits])` (banker's), `Truncate/Floor/Ceiling`, `CompareTo`, operators `+ - * / %` and comparisons, unary `-`, `++/--` (±1), `MinValue/MaxValue/Zero/One`, ostream `<<` (ToString), `std::hash` over the SCALE-NORMALIZED form (divide out trailing zeros first — `1.0` and `1.00` hash equal; simplest: hash the ToString of the normalized value or normalize the limbs).

**Algorithms** (exact contracts; internal 96/192-bit helpers over `uint32_t[3]`/`uint32_t[6]` limbs or `uint64_t` pairs, executor's choice):
- Compare: align scales by scaling the LOWER-scale side up ×10^diff into a 192-bit temp; compare sign then magnitude.
- Add/Sub: align scales (192-bit intermediates); add/sub magnitudes with sign logic; if result needs >96 bits, divide by 10 with round-half-even until it fits, decrementing scale — throw `std::runtime_error("Decimal overflow")` if scale would go below 0.
- Mul: 96×96→192; scale = s1+s2; while result >96 bits OR scale > 28: divide by 10 with round-half-even, scale−−; throw on overflow at scale 0.
- Div: throw on zero divisor; long division scaling the dividend up by 10 while it fits 192 bits to produce up to 28–29 significant digits; final digit round-half-even; result scale capped 28.
- Mod: `a - Truncate(a/b) * b` computed exactly (sign of dividend; max-scale rule).
- Round(d, digits): if scale ≤ digits, unchanged; else drop `scale−digits` digits with round-half-even.
- ToString: digits of the 96-bit magnitude (repeated div-10), insert '.' per scale (scale-preserving: trailing zeros KEPT), '-' prefix; no thousands separators.
- Parse: optional sign, digits, optional '.', digits; scale = fractional digit count; >28 fractional digits: round-half-even to 28; >96-bit magnitude → throw. Invariant only.

- [ ] **Step 1: Failing vector battery** (append fixture section to `CppBclRuntimeTests` or a dedicated `CppDecimalTests` fixture in the same file — one native program per GROUP, markers per case):
  - Construction/ToString: `FromParts` of 1.50 prints `"1.50"`; `Decimal(5)` prints `"5"`; MaxValue prints `"79228162514264337593543950335"`.
  - Add/scale: `1.1 + 2.25 == 3.35` and prints `"3.35"`; `0.1 + 0.2 == 0.3` TRUE; `1.0 + 1.00` prints `"2.00"` (max-scale).
  - Mul: `12.0 * 10.0` prints `"120.00"`; `12.0 * 10` prints `"120.0"`; `19.99 * 100` prints `"1999.00"`.
  - Money loop: summing `0.10` a thousand times == `100.00` EXACTLY and prints `"100.00"`.
  - Div: `1 / 3` prints 28 threes (`"0.3333333333333333333333333333"`); `10 / 4` prints `"2.5"`; div-by-zero throws.
  - Mod: `3.5 Mod 1 == 0.5`; `-3.5 Mod 1 == -0.5` (dividend sign).
  - Equality/hash: `1.0 == 1.00` TRUE and hashes equal; CompareTo consistent; `Compare(1.0, 1.00)==0`, `Compare(1, 2)==-1` (static form).
  - ToDouble: `Decimal from 1.5 → ToDouble() == 1.5`; `ToDouble(Parse("0.1"))` round-trips through `Decimal(double)` back to `0.1`.
  - Round: `Round(2.5)==2`, `Round(3.5)==4` (banker's); `Round(2.675, 2)` prints `"2.68"` (exact decimal, unlike double); `Truncate(-3.7)==-3`; `Floor(-3.7)==-4`; `Ceiling(-3.7)==-3`.
  - Unary/incr: `-(1.5)` prints `"-1.5"`; `++` adds exactly 1 preserving scale (`1.50`→`2.50`).
  - Round-trip: `Parse(x.ToString()) == x` INCLUDING scale for a value table incl. `0.000...1` (28 frac digits) and MaxValue.
  - Overflow: `MaxValue + 1` throws; `MaxValue * 10` throws — as `std::runtime_error`.
- [ ] **Step 2: Red → implement `CppDecimalRuntime.cs` → iterate to green.** This is the plan's longest single loop; group-at-a-time. If a vector disagrees with real .NET when you scratch-check it, FIX THE VECTOR (with a note) — .NET is the oracle, this plan is not.
- [ ] **Step 3: Full `CppBclRuntimeTests` + fast subset green.**
- [ ] **Step 4: Commit** — `git commit -m "feat(p1): faithful 96-bit Decimal engine (bl_decimal.hpp) - locked vector battery green"`

---

### Task 9: Inert codegen wiring (machinery keyed off NativeOwned; registry still rejects)

Spec §6.2. Everything lands DEAD (Categorize() still returns Rejected for the six, so no path activates) EXCEPT the header splice, which makes the new headers part of every generated program (they must compile everywhere — Tasks 6–8 proved they do standalone; this task proves them inside `BasicLangRuntime.g.h`).

**Files:** `BasicLang/CppCodeGenerator.cs` + `CppCodeGenerator.Split.cs`; test additions to `CppBclEndToEndTests.cs` (create) for the splice smoke.

- [ ] **Step 1: Header splice, BOTH modes.** Read `CppCodeGenerator.cs` `EmitRuntimePreamble`/`EmitDotNetSurfaceHelpers` (~358–391, `SpliceRuntimeSource` ~398) and `Split.cs` `EmitRuntimeHeader` (~347–391). Splice `CppBclRuntime.BclHeader` + `CppDecimalRuntime.DecimalHeader` UNCONDITIONALLY in both modes (spec §12; open item §14.4 measures cost later). **The established mechanism** (verified): spliced consts are INCLUDE-FREE bodies starting at `namespace BasicLang {` (like `CppCollectionsRuntime.Source`); std headers are GENERATOR-OWNED in two hardcoded include sets — combined mode (~CppCodeGenerator.cs:277, merged with the always-on `_headerIncludes` {iostream, vector, string, memory}) and split mode (~Split.cs:354). So: structure the new runtime classes as (IncludesList, Body) pieces — the Body is what gets spliced (no `#pragma once`, no `#include` lines, opens its own `namespace BasicLang`); the standalone-testable whole-file properties (`BclHeader`/`DecimalHeader`) are `Includes + Body` concatenations for Tasks 6–8's extraFiles tests. Add the new std includes (`<chrono>`, `<cstdio>`, `<cstring>`, `<cstdint>`, `<functional>`, `<ostream>`) UNCONDITIONALLY to BOTH generator include sets, and promote `<stdexcept>` to unconditional in combined mode (it is currently gated on usesCollections). `<memory>` is already unconditional — don't duplicate. Mirror the collections splice's namespace/indentation handling, **NOT any usage-conditional guard around it** — the BCL headers are unconditional in both modes.
  Smoke test (Integration): compile ANY trivial BL program via `CompileToCppOptimized` + `CppCompile` — the generated TU now contains the new headers and must compile+run. Also one split-mode smoke via the existing split-emission test helpers (Grep `CppSplitCompileTests` and mirror). PLUS a FAST both-modes pin (spec §12 layer 1): generate combined output and the split `BasicLangRuntime.g.h` string for a trivial program and assert both `Does.Contain("struct DateTime")` and `Does.Contain("struct Decimal")` — the cheap presence guard that doesn't need a compiler.
- [ ] **Step 2: MapType branch (dead)** — in `CppCodeGenerator.MapType` (~477–558), BEFORE the `_typeMap.ContainsKey` early return (~541–545): `if (BoundaryTypeRegistry.Categorize(type.Name) == BoundaryTypeCategory.NativeOwned) return type.Name.Equals("StringBuilder", OrdinalIgnoreCase) ? "std::shared_ptr<BasicLang::StringBuilder>" : "BasicLang::" + CanonicalName(type.Name);` (CanonicalName fixes case: DateTime, TimeSpan, Guid, Decimal, DateTimeOffset). REMOVE `_typeMap["Decimal"] = "long double"` from `InitializeTypeMap` (~1765) — Grep first for anything depending on it (expect nothing; Decimal is Rejected so no path reaches it). Add MapTypeName entries (~738–758) for the five value names + `stringbuilder`; fix the stale `datetime → std::time_t` LATER (Task 10 dismantles the shim — leave it for now, it's live). `GetDefaultValue` (~3389): verified its `{}` fallback zero-inits all five value structs to exactly the spec §6.2 pinned defaults (MinValue/Zero/Empty/0D) and gives a null shared_ptr for StringBuilder — NO entry needed; add a one-line comment there noting the P1 reliance.
- [ ] **Step 3: Construction/access/dispatch (dead)** — `Visit(IRNewObject)` (~2740–2762): NativeOwned + not StringBuilder → value construction `result = BasicLang::X(args);`; StringBuilder → `std::make_shared<BasicLang::StringBuilder>(args)`. `MemberAccessOp` (~714–736): NativeOwned value types → `.`; StringBuilder → `->`. Property bridge (~2962–2973): extend the field-access rewrite to consult `NativeBclSurface` Property entries for NativeOwned receivers (`.Year` → `.Year()`; DateTimeOffset's BL `DateTime` property → `ClockDateTime()`, `Ticks` → `TicksValue()` — add a per-entry optional CppName to the surface shape for exactly these renames). Static dispatch: where static-name field/call accesses resolve (the `IsDateTimeNowAccess` neighborhood ~1386 and `EmitStdLibCall`'s dotted-name handling ~2101–2152): NativeOwned type-name receiver + surface StaticMethod/StaticProperty → `BasicLang::X::Member(args)` / member-call form. `EmitToStringShim` (~2857–2886): NativeOwned receivers → emit the native `ToString(...)` member call (route BEFORE the existing `datetime` case). CType lowering: the conversion-emission site (`Visit(IRCast)` ~2616 emits `static_cast` — Read it) gains BOTH Decimal directions: target Decimal → EXACT `BasicLang::Decimal(static_cast<int64_t>(x))` for integral sources (`uint64_t` for ULong; spec §10 pins integer→Decimal exact — the double ctor's 15-digit rule would silently truncate a Long ≥ ~1e15), the `BasicLang::Decimal(static_cast<double>(x))` converting ctor ONLY for Single/Double sources; SOURCE Decimal with target Double/Single → `(x).ToDouble()` (without this, `CType(d, Double)` — valid on C# via real .NET's explicit operator — emits `static_cast<double>` on a struct, a raw C++ error). NOTE (review rider): `CDbl(d)`/`CSng(d)` do NOT pass through IRCast — they lower via EmitStdLibCall's intrinsic arms, which gain the same Decimal-argument → `ToDouble()` routing. Decimal literal constants: `Visit(IRConstant)`-equivalent site emits `BasicLang::Decimal::FromParts(lo, mid, hi, neg, scale)` from `decimal.GetBits` when `Value is decimal`.
  **NOTE — the Byte/SByte WriteLine numeric cast does NOT land here**: it changes LIVE Byte output (verified: `cout << uint8_t` prints a character today) and would violate this task's inertness claim. It lands in Task 10 (where behavior change is expected and pinned).
- [ ] **Step 4: Green** — the full fast subset AND the splice smoke (Integration) AND the fast both-modes pin. All new machinery is dead (the ONE deliberate live change is the header splice itself); any OTHER behavior change = a bug in the "inert" claim — investigate.
- [ ] **Step 5: Commit** — `git commit -m "feat(p1): inert codegen wiring - header splice both modes, NativeOwned MapType/dispatch/constants (registry still rejects)"`

---

### Task 10: The flip — registry moves + checker + member pass + shim dismantling + test churn

Spec §2 + §4.1 + §6.2. ONE commit activates everything Task 9 staged. This is the largest coordinated change; the test churn is enumerated — do it test-first (update the pins to the POST-flip expectations, watch them fail, flip, watch green).

**Files:** `BoundaryTypeRegistry.cs`, `CppCapabilityChecker.cs`, `TypeMapper.cs` (CppTypeMapper SByte entry NOW), `CppCodeGenerator.cs` (shim removal, MapTypeName `datetime` entry), `CppRuntimeSources.cs` (DotNetSurfaceHelpers Now/FormatTime removal — KEEP the format-token conversion logic by moving it into `bl_bcltypes.hpp`'s `DateTime::ToString(fmt)` if Task 6 didn't already reimplement it), the parent contract spec doc, and the churn tests.

> **Task 9 review riders (recorded 2026-07-28 — address during Task 10/11):**
> - **Decimal→integral casts are unguarded on BOTH lowering routes**: `CType(d, Integer)` (Visit(IRCast)) and `CInt(d)`/`CLng(d)` (the EmitStdLibCall intrinsic route) would emit `static_cast<int32_t>` on the struct — invalid C++. Verify what the analyzer permits and either lower via `(d).ToDouble()` + cast with DOCUMENTED truncation semantics (real .NET truncates toward zero for `(int)decimal`) or reject cleanly in the member/capability pass.
> - **`BoundaryTypeRegistry.Categorize` does not normalize a `System.` prefix** (`NativeBclSurface.Normalize` does): `System.DateTime` categorizes Unknown — a pre-existing reject-net hole for qualified spellings today, and post-flip it would bypass ALL the NativeOwned codegen machinery (which keys off Categorize). Mirror the normalization into the registry and pin it.
> - **User-defined shadows of the six names** (`Class Guid`, `Structure DateTime`, …) need an explicit diagnostic answer: today they are name-rejected by the capability checker; post-flip MapType would SILENTLY remap them to the native runtime types. Decide (clean diagnostic vs. user-type-wins scope rule) and pin with a test.

- [ ] **Step 1: Update the pinned tests FIRST** (all in spec §12's ledger; red until the flip):
  - `BlnetContractTests.cs` `BoundaryTypeRegistryTests`: `TodaysRejectList_IsRejected` keeps ONLY Object/Regex/Uri/Stream/FileInfo/DirectoryInfo; new `P1Types_AreNativeOwned` TestCases for the six; `SByte`/`Byte` → new `SByte_IsBridged` case; `CategorizeIsCaseInsensitive` re-targets `"regex"`; `NativeOwnedAndManagedOwned_StartEmpty_PreP1` → `NativeOwned_ContainsExactlyTheP1Six` + `ManagedOwned_StillEmpty`; `MapperInvariant` formula UNCHANGED (Bridged grows by SByte — the CppTypeMapper edit this task makes keeps it green).
  - `NativeBclFrontEndTests.SurfaceRegistryCoherence`: re-point at the registry per its Task 5 TODO.
  - `CppCollectionTests`: the 5 `*_StillRejected` swap DateTime→`Regex` / Decimal→`Stream` (keep the scenario shapes; they now pin the REMAINING reject list).
  - `CppBackendTests`: `Cpp_InterfaceReturn_FuncOfUnmappedArg_ThrowsCapabilityError` re-targets Regex; `Cpp_ConsoleTemplateSurface_LowersToValidCpp` re-pins to the native lowering (`BasicLang::DateTime::Now()`, native `ToString`; delete the `Does.Not.Contain("DateTime->")`-era assertions that enforced the shim).
- [ ] **Step 2: The flip.** `BoundaryTypeRegistry`: move the six to NativeOwned, SByte to Bridged; rewrite the doc comment (post-P1 invariant: `_typeMap` keys == Bridged + Object; NativeOwned handled by name in the generator; surface table coherence test named). `CppTypeMapper` (TypeMapper.cs ~208–222): add `SByte → int8_t`, fix `Byte → uint8_t`. First run the spec §14.5 sweep: Grep the test project for C++ expectations pinning Byte to `int8_t` (pre-verified result: NONE — only one non-output `As Byte` use in CompilationTests.cs; record the sweep result for Task 14's report). `CppCapabilityChecker.CheckType`: `if (category == BoundaryTypeCategory.NativeOwned) return;` immediately after the Bridged early-return. **Byte/SByte WriteLine numeric print lands HERE** (moved from Task 9 — it changes live Byte output from character to number, matching .NET/C#): wrap `int8_t/uint8_t`-typed WriteLine/Write args in `static_cast<int32_t>(...)` at the cout lowering (~2115), and add a pin test in this commit (`Dim b As Byte = 65 : Console.WriteLine(b)` → `"65"` on C++, Integration).
- [ ] **Step 3: Member-surface capability pass** (spec §4.1). New checker walk over each function's instructions: for `IRInstanceMethodCall`/`IRFieldAccess` whose RECEIVER type name is NativeOwned, and `IRNewObject` whose class name is NativeOwned, consult `NativeBclSurface`; unknown member/arity → `diags.Add($"'{Type}' has no native member '{Member}' on the C++ backend ({where})")`; unknown ctor arity → similar. ALSO: extend the walk to expression-position temporaries for REJECTED types (`IRNewObject` of a Rejected class name → the existing "no C++ mapping" diagnostic) — closing the pre-existing leak (spec §4.1). Read the checker's existing instruction loop (~50–135) and mirror its structure; keep the hand-mirrored-walk sync comment updated.
- [ ] **Step 4: Shim dismantling.** Remove: `_dateTimeValues` (+ its population ~1317–1326 and temp retyping ~1434), `IsDateTimeNowAccess` (~1386) + its rewrite site (~2939–2944), `EmitToStringShim`'s `datetime` case (~2868–2871), `MapTypeName["datetime"] → std::time_t` (→ `BasicLang::DateTime`), `CppRuntimeSources.DotNetSurfaceHelpers`' `Now`/`FormatTime` (verify the const's remaining content and its splice sites still balance; if the const becomes empty, remove its splice calls too). `DateTime.Now` now flows: static dispatch (Task 9) → `BasicLang::DateTime::Now()`.
- [ ] **Step 5: Contract doc sync** — edit `2026-07-26-dotnet-native-boundary-contract-design.md` C1 example rows: SByte out of the NativeOwned examples (→ Bridged mention), per spec §2.
- [ ] **Step 6: Green sweep** — fast subset (all churn green, no new failures) + `CppBclRuntimeTests` + the Task 9 splice smokes. Expect the console-template test to need iteration (the re-pinned lowering must match what codegen actually emits — fix TEST or CODEGEN toward the spec, never weaken to Does.Not-nothing).
- [ ] **Step 7: Commit** — `git commit -m "feat(p1): the flip - six types NativeOwned, SByte Bridged, member-surface capability pass, DateTime shim dismantled"`

---

### Task 11: BL end-to-end on the C++ backend + member diagnostics

Spec §12 layers 3–4. BL programs per type through `CompileToCppOptimized` + compile-and-run (exact stdout), plus the CLI (`IDE/BasicLang.exe file.bas --target=cpp`) at least once per type family, plus the clean-diagnostic tests.

**Files:** `VisualGameStudio.Tests/Compiler/CppBclEndToEndTests.cs` (extend from Task 9's smoke), `[Category("Integration")]`.

- [ ] **Step 1: Failing tests, one per bullet** (copy the `CompileToCppOptimized`+`CompileRun` idiom from `CppCollectionTests` ~103–132/243–270; expected outputs verified against the C# backend run of the SAME program where possible — pre-parity sanity):
  - DateTime: declare/construct/Now-into-local (`Dim d = DateTime.Now` — the case the old shim couldn't do), components, AddDays/AddMonths clamp, dt2−dt1 → TimeSpan.Days, dt+ts, comparisons, `ToString("yyyy-MM-dd")`, Parse round-trip, `Console.WriteLine(d)` (ostream inserter path).
  - TimeSpan: FromX factories, components vs totals, compound `ts += ts2`.
  - Guid: NewGuid structural (two differ; ToString shape), Parse round-trip, `New Guid("...")` string-ctor, `Console.WriteLine(g)` (inserter path), `Dictionary(Of Guid, String)` add/lookup (the hash story, spec §6.2).
  - StringBuilder: chaining program printing the built string incl. `Append(anInteger)` (the overload-ambiguity pin); aliasing program (two variables, one builder); `List(Of ...)`? NO — keep v1 surface only.
  - Decimal: THE money program (`19.99 * 1.08` etc.), scale-preserving prints, `0.1+0.2=0.3` as BL, For-loop accumulation, `CType(x, Decimal)`, Mod with negative dividend, `d += 1`, unary minus, `++`.
  - DateTimeOffset: construct with offset, UTC-instant equality program, ToUnixTimeSeconds, `Console.WriteLine(dto)`.
  - Try/Catch over a P1 runtime throw: BL program catching Decimal divide-by-zero and printing the caught marker — proves the spec §11 runtime_error→BL-catch flow end-to-end.
  - `CType(d, Double)` / `CDbl(d)` on C++ (the ToDouble path).
  - SByte on C++: arithmetic + WriteLine prints NUMBER (the §14.6 pin).
  - MEMBER DIAGNOSTICS (fast, not Integration — these only run the checker): unknown member per type (`d.ToBinary()`) asserts `CppCapabilityException` message contains type + member; `g.ToByteArray()` cleanly rejected (not on the BL surface — spec §5); unknown ctor arity; Rejected-type-in-expression-position (`Console.WriteLine(New Regex("x"))`-shaped) now cleanly rejected — the leak-closure proof.
- [ ] **Step 2: Iterate to green** — failures here are REAL integration bugs (dispatch, bridge, splice); fix at the responsible layer, never by weakening a vector. If a fix requires changing behavior pinned by Tasks 6–8 native tests, STOP and reconcile deliberately.
- [ ] **Step 3: CLI leg** — one representative program per type family via `IDE/BasicLang.exe prog.bas --target=cpp` (repo law: both entry points). Assert exit 0 + expected stdout of the produced exe.
- [ ] **Step 4: Commit** — `git commit -m "test(p1): BL-to-C++ end-to-end per type (optimizer+CLI) + member-diagnostic coverage"`

---

### Task 12: VB stdlib date category on the C++ backend

Spec §7. Emissions in `CppCodeGenerator.EmitStdLibCall` (the LIVE switch, ~2101–2152); `StdLib/CppStdLib.cs` support-matrix entries; analyzer registrations landed in Task 5.

- [ ] **Step 1: Failing Integration tests** (append to `CppBclEndToEndTests`): `Now()` into a DateTime local; `Year(d)/Month(d)/Day(d)` on a literal date; `DateAdd(d, "d", 5)` (REPO argument order: date, interval, number); `DateDiff(d1, d2, "d")`; `FormatDate(d, "yyyy-MM-dd")`; `NewGuid()` returns a parseable String. Cross-check each expected value by running the SAME program on the C# backend first (they must agree — this is pre-parity).
- [ ] **Step 2: Implement** — lowercase-name cases in `EmitStdLibCall` mapping onto the native types (`"now"` → `BasicLang::DateTime::Now()`, `"year"` → `(arg).Year()`, `"dateadd"` → interval-string dispatch onto AddDays/AddMonths/etc. — READ `CSharpStdLib.EmitDateAdd` (~573) for the accepted interval strings (spec §14.3) and mirror EXACTLY; unknown interval string → same behavior class as C# (runtime throw)). `CppStdLib.cs`: date-category entries so the `--stdlib` matrix reports supported.
- [ ] **Step 3: Green + commit** — `git commit -m "feat(p1): VB date stdlib + NewGuid on the C++ backend (EmitStdLibCall live path)"`

---

### Task 13: Cross-backend parity oracle

Spec §12.5 with the discipline paragraph: fixed literal dates/guids, invariant-safe format strings, C#-side runs under forced invariant culture, Now/NewGuid structural-only.

**Files:** `VisualGameStudio.Tests/Compiler/BclBackendParityTests.cs` (`[Category("Integration")]`).

- [ ] **Step 1: The harness.** `RunBothBackends(blSource) → (csOut, cppOut)`: compile+run via the C# path and the C++ path (reuse Task 2's `CompileRunCSharp` + Task 11's C++ helper). Culture forcing: the C# leg must run under invariant culture — inject `System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;` as the first emitted statement of Main FOR THE PARITY HARNESS ONLY (mechanism: check whether the C# backend has a Main-preamble hook; if not, wrap: the harness compiles the generated .cs with a tiny wrapper Main that sets culture then calls the generated entry — pick the least invasive mechanism and document it in the fixture header; do NOT change production emission).
  Normalize `\r\n`→`\n`; assert `csOut == cppOut` with both outputs in the failure message.
- [ ] **Step 2: Parity programs** (one test each; ~12 programs): the Decimal money battery as BL (arithmetic, loop accumulation, Round/Mod, scale prints); `CType(d, Double)` and `CType(x, Decimal)` round-trips; DateTime literal-date arithmetic + `ToString("yyyy-MM-dd HH:mm:ss")` + DayOfWeek-as-number + `WriteLine(d.Kind)`; an UNINITIALIZED `Dim d As DateTime` printed (default-value parity: `01/01/0001 00:00:00`); TimeSpan components/totals; Guid `Parse(fixed).ToString()` round-trip; StringBuilder chain incl. `Append(Integer)` and `Append(Boolean)`; DateTimeOffset fixed-offset equality prints; SByte/Byte arithmetic + WriteLine (numeric print parity); stdlib `DateAdd/DateDiff/FormatDate` on fixed dates.
- [ ] **Step 3: Iterate.** A diff = a real semantic divergence: fix the C++ side to match .NET unless the spec documents the divergence (§9's list) — in which case the PROGRAM was undisciplined, fix the program (e.g. avoid default `ToString()` only if it proves culture-fragile even under forced invariant — it shouldn't be).
- [ ] **Step 4: Commit** — `git commit -m "test(p1): cross-backend parity oracle - identical stdout C# vs C++ for the BCL battery"`

---

### Task 14: Full verification + closeout

- [ ] **Step 1: Fast subset** — expect Passed == 3394 (pre-plan baseline) + ALL new fast tests added by Tasks 1–5, 10, 11(diagnostic subset), 0 failed. Count and RECORD the number with a per-task accounting (each task's report noted its additions).
- [ ] **Step 2: Full Integration sweep** — `--filter "FullyQualifiedName~CppBclRuntimeTests|FullyQualifiedName~CppBclEndToEndTests|FullyQualifiedName~BclBackendParityTests|FullyQualifiedName~BlnetConformanceTests|FullyQualifiedName~BlnetShimPublishTests|FullyQualifiedName~BlnetNativeRuntimeTests"` → all green (the Blnet suite proves P1 didn't disturb the boundary contract).
- [ ] **Step 3: Also run the pre-existing C++ Integration fixtures** touched by churn (`CppCollectionTests`, `CppBackendTests`) green.
- [ ] **Step 4: Spec status line** — `2026-07-27-p1-native-bcl-types-design.md`: `**Status:** Draft, pre-review` → `**Status:** Implemented — see VisualGameStudio.Tests/Compiler/{CppBclRuntimeTests,CppBclEndToEndTests,BclBackendParityTests}.cs`.
- [ ] **Step 5: Commit** — `git commit -m "docs(p1): mark native BCL types implemented; parity oracle green"`
- [ ] **Step 6:** @superpowers:verification-before-completion. Report: pass counts per gate, any spec-relevant adjustment discovered during implementation (a CONTRACT/spec semantic change — not a bug fix — must stop and surface for spec amendment first), the §14 open-item resolutions (each must be resolved and noted: 14.1 blast radius, 14.2 double↔Decimal rule as pinned by parity, 14.3 interval strings, 14.4 splice cost decision, 14.5 Byte pins found, 14.6 Byte/SByte printing).

**Known risks the executor should expect:**
- Task 5's member-typing change (Object → concrete) is the widest fast-subset blast radius; any C#-backend test relying on Object-typed temps will surface there.
- Task 9's splice is the classic both-modes drift trap; the split-mode smoke is not optional.
- Task 10's console-template re-pin will take iteration; the OLD test asserts shim-era strings that must be deliberately replaced.
- The Decimal engine (Task 8) is the longest loop; if a locked vector contradicts real .NET on scratch-check, the vector is wrong — fix it with a note (real .NET is the oracle).
- `enable_shared_from_this` UB if a StringBuilder is ever constructed OUTSIDE a shared_ptr — Task 9's IRNewObject path must ALWAYS make_shared; the native tests construct via make_shared only.
- MSVC vs clang/g++ divergences usually surface as `<=>`/aggregate-init nits in the headers — keep C++20 conservative (the Task 6–8 loop catches these on the probed compiler; the parity/E2E runs use the same probe).
