title: Compiler pipeline
lede: Preprocess, lex, parse, analyse, lower to IR, optimise, emit — and the files that own each step.
---
<div class="pipe">
<div class="pipe-step"><span class="n">01</span><span class="s">Preprocess</span><span class="f">Preprocessor.cs</span></div>
<div class="pipe-step"><span class="n">02</span><span class="s">Lex</span><span class="f">BasicLangLexer.cs</span></div>
<div class="pipe-step"><span class="n">03</span><span class="s">Parse</span><span class="f">Parser.cs</span></div>
<div class="pipe-step"><span class="n">04</span><span class="s">Analyse</span><span class="f">SemanticAnalyzer.cs</span></div>
<div class="pipe-step"><span class="n">05</span><span class="s">Lower</span><span class="f">IRBuilder.cs</span></div>
<div class="pipe-step"><span class="n">06</span><span class="s">Optimise</span><span class="f">IROptimizer.cs</span></div>
<div class="pipe-step"><span class="n">07</span><span class="s">Emit</span><span class="f">*Backend.cs</span></div>
</div>

## 1. Preprocess

`Preprocessor.cs` resolves conditional compilation (`#IfDef`, `#IfNDef`, `#Else`,
`#EndIf`), `#Define`, and the three include/interop forms — `#Include`, which splices a
BasicLang file, `#CppInclude`, which records a C++ header for the native backend to
emit, and `#JsImport`, which becomes a real ES `import` on the JavaScript backend. `#If`,
`#ElseIf`, `#Const`, `#Region` and `#End Region` never reach the preprocessor at all: the lexer
gives them their own token types (`TokenType.PreprocessorIf`, `PreprocessorRegion`, …) but
`Parser.cs` names none of them and no `Preprocessor*Node` is ever constructed anywhere in the
tree, so the `Visit` overloads for them in `SemanticAnalyzer.cs` and `IRBuilder.cs` are dead
code. Preprocessor errors carry **no BL code**: a `PreprocessorError` holds a line, a column
and a message and nothing else, and `Compiler.cs` turns each one into a plain `SemanticError`.
`BL1xxx` is the lexer's range, and `BL1009_InvalidPreprocessorDirective` /
`BL1010_NestedPreprocessorMismatch` sit in the `ErrorCode` enum with no code path emitting
them. The one BL code `Preprocessor.cs` does emit is `BL7010` — a JavaScript `#JsImport` name
collision.

## 2. Lex

`BasicLangLexer.cs` (~1,470 lines) produces the token stream. It handles the
VB-flavoured literal forms — `&H`/`&O`/`&B` prefixes, `"A"c` char literals,
interpolated strings — line continuations (`_`), and both comment forms (`'` and `Rem`).

## 3. Parse

`Parser.cs` (~5,200 lines) is a recursive-descent parser producing the AST declared in
`ASTNodes.cs`. `ASTPrettyPrinter.cs` renders an AST back to source, which is how several
tests assert structure without string-matching generated code.

## 4. Semantic analysis

`SemanticAnalyzer.cs` is the largest file in the compiler at ~9,620 lines, and it is
where most language behaviour actually lives: symbol binding, type checking, overload
resolution, generic constraint checking, accessibility, inheritance and interface
conformance, pattern exhaustiveness, and LINQ desugaring.

Supporting cast:

| File | Role |
|---|---|
| `SymbolTable.cs` / `ProjectSymbolTable.cs` | Scope and cross-file symbol resolution |
| `TypeRegistry.cs` | Known types, including synthesised generic instantiations |
| `TypeMapper.cs` | BasicLang type ⇄ target-language type mapping |
| `ModuleResolver.cs` | Resolves `Import` / `Using` — **shared with the LSP** |
| `ModuleTypeWalker.cs` | Type walking — **shared with the C++ backend and capability checkers** |
| `DependencyGraph.cs` | Multi-file ordering and circular-import detection |
| `ModuleRegistry.cs` | Registered modules for a compilation |

## 5. Lower to IR

`IRBuilder.cs` (~5,430 lines) lowers the checked AST to the IR nodes in `IRNodes.cs`
(~2,160 lines), building a control-flow graph (`ControlFlowGraph.cs`) per routine.
Exceptions become `IRThrow` nodes. `IRPrettyPrinter.cs` dumps IR for debugging;
`IRInterpreter.cs` can execute it directly, which the debugger's
`DebuggableInterpreter.cs` builds on.

> [trap] **The C# backend reconstructs control flow by matching block *names*, not graph
> shape.** `IsIfThenElse` looks for `.then` / `.else` and resolves `{prefix}.end`. A CFG
> named anything else falls through to a path that emits both arms and never the merge —
> the program prints its first operand and stops. Name new CFGs exactly the way
> `Visit(IfStatementNode)` does. The JavaScript backend derives the merge block
> differently (`FindMergeBlock`, so the true target must never *be* the merge); the C++
> backend is goto-based and tolerates any shape.

## 6. Optimise

`IROptimizer.cs` (~3,220 lines) runs constant folding, dead-code elimination, and the
rest. `IROperandWalker.cs` — the single source of truth for "the operands an instruction
consumes" — is shared by `IRBuilder.cs`, `CppCodeGenerator.cs`, `JavaScriptBackend.cs`,
`CppCapabilityChecker.cs`, `ForeignFeatureChecker.cs` and `Net/NetSurfaceCollector.cs`, so a
foreign value in a `Case` or `When` position cannot slip past one guard while another sees it.
The optimizer does not use it.

> [trap] **Every shipping route runs the optimizer; the plain unit-test helper does not.**
> A fixture can be green while both the CLI and the IDE miscompile the same program.
> Validate codegen through the CLI, or through an optimizer-running helper such as
> `CompileToCppOptimized` in `CppCollectionTests.cs`. stdout is the only valid oracle.

> [note] **Passes that are registered nowhere.** `FunctionInliningPass` was commented out of
> `AddAggressivePasses` — measured, it miscompiled silently in six of seven call shapes (five
> separate defects, including inlined locals never added to the caller's `LocalVariables` and
> `InlineCallsInBlock` never calling `ReplaceUses`, so the callee was inlined *and* still
> called). `ConstantPropagationPass` is likewise commented out of `AddStandardPasses` — it
> propagated across control-flow merges. Both classes are still in `IROptimizer.cs`; only their
> `AddPass` lines are gone. `AlgebraicSimplificationPass` is still registered, but three of its
> arms (`(a + b) - b → a`, `(a - b) + b → a`, `(a * b) / b → a`) were **deleted** rather than
> repaired: all three are unsound — catastrophic cancellation, an unchecked `b = 0`, and plain
> floating-point rounding — and the pass's missing `ReplaceUses` was the only reason nobody ever
> saw a wrong answer. `2 * x → x + x` is kept.

## 7. Emit

`BackendRegistry.cs` selects a generator behind `ICodeGenerator.cs`, configured by
`CodeGenOptions.cs`. Each backend pairs with a standard-library shim under `StdLib/`
(`CSharpStdLib.cs`, `CppStdLib.cs`, `JavaScriptStdLib.cs`, and so on, registered through
`StdLibRegistry.cs`).

| Backend | File(s) | Notes |
|---|---|---|
| C# | `CSharpBackend.cs`, `Csharpcodegenerator.cs` | Name-shape-sensitive CFG reconstruction |
| C++ | `CppCodeGenerator.cs` + `.Split.cs` + `.NetCalls.cs`, plus `Compiler/CodeGen/CPlusPlus/` and `Compiler/CodeGen/Net/` | goto-based; `CppCapabilityChecker.cs` gates features; `NetProxyEmitter.cs` + `.Facade.cs` emit `blnet_proxies.g.hpp` / `blnet_facade.g.hpp` for .NET interop |
| JavaScript | `JavaScriptBackend.cs`, `JavaScriptEmitter.cs`, `JavaScriptSourceMap.cs` | ES modules + source maps; `JsCapabilityChecker.cs` gates features |
| LLVM | `LLVMBackend.cs` | <span class="pill mute">Unmaintained</span> — genuinely out of scope; untouched while MSIL was overhauled |
| MSIL | `MSILBackend.cs` | <span class="pill ok">Maintained</span> since 2026-09-15 — textual IL assembled by `ilasm`; round-tripped to a real process by `VisualGameStudio.Tests/Msil/` |

`MultiTargetCompiler.cs` drives more than one backend from a single parse, and
`Compiler.cs` is the front door the CLI, the IDE build service and the tests all call.

> [note] **MSIL is no longer out of scope.** `MSILBackend.cs` went 2,136 → 5,356 lines and
> `VisualGameStudio.Tests/Msil/` added 4,279 lines over six files (127 `[Test]` methods plus
> 18 `[TestCase]` rows) since 2026-09-15: properties, field initializers, arrays, `Try`/`Catch`
> as real EH regions, `Select Case`, instance methods, `Shared` members, module-level
> variables, `MyBase.New` arguments, and a narrow recorded .NET `Console` / `List` /
> `Dictionary` surface. **Never assert on emitted IL text alone** — `MsilHarness.cs` takes
> source → `.il` → `ilasm` → a real process → stdout, because the defect that motivated it was
> a `Select Case` that assembled, ran, and answered `Case Else` for every input. `ilasm` is
> located, not required: a machine without one gets `Assert.Ignore`. LLVM is still out of scope.

## Capability checkers

Not every language feature survives every backend. Rather than emit broken code, the
compiler *refuses* and says why:

- **`CppCapabilityChecker.cs`** refuses constructs the native backend cannot lower and reports
  them under `BL6001` — one positionless blob per build, and the only code it owns. The P2a-2
  lowering refusals ride the same `CppCapabilityException` under codes the analyzer owns
  (`BL6017`, the name-only gate; `BL6019`, an unmarshalable shape). The 22 `BL6xxx` codes in the
  tree (`BL6001`–`BL6027`, with gaps) are spread across `Net/`, `Compiler/CodeGen/Net/`,
  `CppCodeGenerator*.cs`, `SemanticAnalyzer.cs`, `TypeRegistry.cs` and
  `ProjectSystem/CppProjectBuilder.cs` — the last is where toolchain failures are actually
  reported, not here. `BL6027` is the newest: a blnet C++ facade name or signature collision,
  always a warning, never a build failure.
  backend cannot lower, and for toolchain failures.
- **`JsCapabilityChecker.cs`** raises `BL7xxx` — `ByRef` parameters (`BL7002`), `Long`
  (`BL7003`), `Char` (`BL7004`), value `Structure` (`BL7005`), operator overloads
  (`BL7006`), unavailable BCL types (`BL7007`), and more.
- **`ForeignFeatureChecker.cs`** polices inline foreign code blocks.

This is the design principle to preserve: **a backend that cannot express something must
produce a diagnostic, never plausible-looking wrong code.**

## Other entry points into the same pipeline

The compiler assembly is also the language server and the debug adapter, and both reuse
these stages rather than re-implementing them:

- `LSP/` builds on the parser and semantic analyzer for completion, hover, and diagnostics.
- `Debugger/` builds on the IR interpreter and on source maps emitted by the backends.
- `BasicLangRepl.cs` / `REPL.cs` drive the pipeline interactively.
