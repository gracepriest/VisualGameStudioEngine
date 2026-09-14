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

`Preprocessor.cs` resolves conditional compilation (`#If`, `#IfDef`, `#IfNDef`, `#Else`,
`#EndIf`), `#Define`, `#Region`, and the two include forms — `#Include`, which splices a
BasicLang file, and `#CppInclude`, which records a C++ header for the native backend to
emit. Errors here are `BL1xxx` codes, because the preprocessor runs inside the lexing
phase's error budget.

## 2. Lex

`BasicLangLexer.cs` (~1,470 lines) produces the token stream. It handles the
VB-flavoured literal forms — `&H`/`&O`/`&B` prefixes, `"A"c` char literals,
interpolated strings — line continuations (`_`), and both comment forms (`'` and `Rem`).

## 3. Parse

`Parser.cs` (~5,200 lines) is a recursive-descent parser producing the AST declared in
`ASTNodes.cs`. `ASTPrettyPrinter.cs` renders an AST back to source, which is how several
tests assert structure without string-matching generated code.

## 4. Semantic analysis

`SemanticAnalyzer.cs` is the largest file in the compiler at ~8,570 lines, and it is
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

`IRBuilder.cs` (~4,500 lines) lowers the checked AST to the IR nodes in `IRNodes.cs`
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

`IROptimizer.cs` (~3,030 lines) runs constant folding, dead-code elimination, and the
rest. `IROperandWalker.cs` is the traversal utility it and the backends share.

> [trap] **Every shipping route runs the optimizer; the plain unit-test helper does not.**
> A fixture can be green while both the CLI and the IDE miscompile the same program.
> Validate codegen through the CLI, or through an optimizer-running helper such as
> `CompileToCppOptimized` in `CppCollectionTests.cs`. stdout is the only valid oracle.

## 7. Emit

`BackendRegistry.cs` selects a generator behind `ICodeGenerator.cs`, configured by
`CodeGenOptions.cs`. Each backend pairs with a standard-library shim under `StdLib/`
(`CSharpStdLib.cs`, `CppStdLib.cs`, `JavaScriptStdLib.cs`, and so on, registered through
`StdLibRegistry.cs`).

| Backend | File(s) | Notes |
|---|---|---|
| C# | `CSharpBackend.cs`, `Csharpcodegenerator.cs` | Name-shape-sensitive CFG reconstruction |
| C++ | `CppCodeGenerator.cs` + `.Split.cs` + `.NetCalls.cs` | goto-based; `CppCapabilityChecker.cs` gates features |
| JavaScript | `JavaScriptBackend.cs`, `JavaScriptEmitter.cs`, `JavaScriptSourceMap.cs` | ES modules + source maps; `JsCapabilityChecker.cs` gates features |
| LLVM | `LLVMBackend.cs` | Unmaintained |
| MSIL | `MSILBackend.cs` | Unmaintained |

`MultiTargetCompiler.cs` drives more than one backend from a single parse, and
`Compiler.cs` is the front door the CLI, the IDE build service and the tests all call.

## Capability checkers

Not every language feature survives every backend. Rather than emit broken code, the
compiler *refuses* and says why:

- **`CppCapabilityChecker.cs`** raises `BL6xxx` diagnostics for constructs the native
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
