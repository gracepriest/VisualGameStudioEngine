title: Diagnostics and error codes
lede: `BL0xxx` through `BL9xxx` — which phase raises which range, and the full enumerated set.
---
Most compiler diagnostics carry a structured code. Where one is present the first digit is the phase, which
tells you which file to open before you read the message.

| Range | Phase | Raised by |
|---|---|---|
| `BL1xxx` | Lexer | `BasicLangLexer.cs` — `Preprocessor.cs` raises errors but they carry no code |
| `BL2xxx` | Parser | `Parser.cs` |
| `BL3xxx` | Semantic analysis | `SemanticAnalyzer.cs`, `SymbolTable.cs` |
| `BL4xxx` | IR and code generation | `IRBuilder.cs`, backends |
| `BL5xxx` | Linker and build | module resolution, file loading |
| `BL6xxx` | Native C++ backend, toolchain, entry points, .NET boundary | `CppCapabilityChecker.cs`, `ProjectSystem/CppProjectBuilder.cs`, `ProjectSystem/NativeEntryPoints.cs`, `SemanticAnalyzer.cs`, `BasicLang/Net/`, `Compiler/CodeGen/Net/` |
| `BL7xxx` | JavaScript backend capability | `JsCapabilityChecker.cs` (messages + the walk); `BL7008`/`BL7009` raised from `JavaScriptBackend.cs` |
| `BL0xxx` | IDE build orchestration | `BuildService.cs` — `BL0001` no source files / `BL0002` source file not found, `BL0010` build-order (topological sort) failure, `BL0011` project load failure, `BL0020` package restore, `BL0030` missing `RaylibWrapper.dll` (warning) |
| `BL9998` | IDE build host | `BuildService.cs` — "Compilation failed but the compiler reported no diagnostics", and internal compiler errors |
| `BL9999` | Unknown | fallback |

> [note] Not every diagnostic is a `BL` code. Preprocessor errors carry no code at all
> (`PreprocessorError` has only `Line`, `Column`, `Message`), and native toolchain output is
> parsed into the raw MSVC / clang code, or `CPP1001` / `CPP1002` when the line matches no
> known shape (`CppDiagnosticsParser.cs:27-28`).

The enum lives in `BasicLang/ErrorFormatter.cs`.

> [trap] `ErrorFormatter`'s *rendering* (caret line, source excerpt, suggestion) and its
> message-text-sniffing `InferErrorCode` have **no production caller**. `FormatLexerError`,
> `FormatParserError`, `FormatSemanticError`, `InferErrorCode` and `GetErrorCodeString` are
> reached only from `BasicLang/ErrorMessagesDemo.cs` — which nothing in the repo references —
> and from a handful of unit tests in `VisualGameStudio.Tests/Compiler/ErrorMessageTests.cs`.
> Nothing the CLI, IDE or LSP shows a user is shaped by this file. Read the enum for the code
> list, but do not assume a diagnostic you see was produced here.
message shape (caret line, source excerpt, suggestion).

> [note] The four tables below transcribe the `ErrorCode` enum, not the set of diagnostics
> a build can actually emit. **20 of the 54 members are never referenced outside the enum
> declaration** and cannot fire: `BL1006`, `BL1008`, `BL1009`, `BL1010`, `BL2005`, `BL2007`,
> `BL2008`, `BL2009`, `BL2010`, `BL2012`, `BL3007`, `BL3008`, `BL3012`, `BL3021`, `BL3022`,
> `BL3024`, `BL4001`, `BL4002`, `BL4003`, `BL5003`. Of the rest, only ten (`BL1001`-`BL1004`,
> `BL2001`-`BL2004`, `BL3001`, `BL3002`) are referenced anywhere outside `ErrorFormatter.cs`
> at all; the others are reachable only through `InferErrorCode`'s message-substring guesses,
> which nothing calls (see above).

## BL1xxx — Lexer

| Code | Meaning |
|---|---|
| `BL1001` | Unterminated string |
| `BL1002` | Unterminated interpolated string |
| `BL1003` | Invalid character |
| `BL1004` | Invalid number format |
| `BL1005` | Invalid escape sequence |
| `BL1006` | Unrecognized directive |
| `BL1007` | Invalid char literal |
| `BL1008` | Unterminated comment |
| `BL1009` | Invalid preprocessor directive |
| `BL1010` | Nested preprocessor mismatch |

## BL2xxx — Parser

| Code | Meaning |
|---|---|
| `BL2001` | Unexpected token |
| `BL2002` | Missing token |
| `BL2003` | Missing keyword |
| `BL2004` | Mismatched block |
| `BL2005` | Invalid syntax |
| `BL2006` | Unexpected end of file |
| `BL2007` | Duplicate modifier |
| `BL2008` | Invalid modifier |
| `BL2009` | Missing expression |
| `BL2010` | Invalid array declaration |
| `BL2011` | Missing `End` block |
| `BL2012` | Invalid parameter list |

## BL3xxx — Semantic

| Code | Meaning |
|---|---|
| `BL3001` | Type mismatch |
| `BL3002` | Undefined symbol |
| `BL3003` | Redefined symbol |
| `BL3004` | Wrong number of arguments |
| `BL3005` | Invalid assignment |
| `BL3006` | Invalid operation |
| `BL3007` | Cannot convert |
| `BL3008` | Missing return type |
| `BL3009` | Accessibility error |
| `BL3010` | Circular dependency |
| `BL3011` | Invalid member access |
| `BL3012` | Invalid array access |
| `BL3013` | Abstract method not implemented |
| `BL3014` | Sealed class inheritance |
| `BL3015` | Invalid constructor call |
| `BL3016` | Ambiguous overload |
| `BL3017` | Missing return statement |
| `BL3018` | Unreachable code |
| `BL3019` | Uninitialized variable |
| `BL3020` | Invalid cast |
| `BL3021` | Constraint violation |
| `BL3022` | Invalid base call |
| `BL3023` | Read-only assignment |
| `BL3024` | Invalid event usage |
| `BL3025` | Missing import |

## BL4xxx and BL5xxx

| Code | Meaning |
|---|---|
| `BL4001` | Unsupported feature (enum member, never raised). The literal `BL4001` a build emits is `BuildService.cs:730`'s "Code generation error" — *any* exception out of a backend's `Generate`, including JavaScript `BL70xx` refusals |
| `BL4002` | Backend error |
| `BL4003` | Invalid IR |
| `BL5001` | File not found |
| `BL5002` | Circular import |
| `BL5003` | Duplicate module |

## BL6xxx — Native backend, toolchain, and .NET boundary

Twenty-three live codes. `BL6001` is `CppCapabilityChecker`'s positionless default blob code
(`CppCapabilityChecker.DefaultDiagnosticCode`, one per build); the rest are reported against a
file.

| Code | Meaning | Raised by |
|---|---|---|
| `BL6001` | Native backend cannot lower this construct (positionless blob) | `CppCapabilityChecker.cs`, `CppProjectBuilder.cs` |
| `BL6005` | No C++ toolchain found (clang++, g++, MSVC all absent) | `CppProjectBuilder.cs` |
| `BL6006` | C++ compile or link failure, or a generated artefact could not be written / deployed | `CppProjectBuilder.cs` |
| `BL6007` | Project contains no C++ translation units and no BasicLang sources | `CppProjectBuilder.cs` |
| `BL6009` | Native library not found | `CppProjectBuilder.cs` |
| `BL6010` | C++ build threw (IDE wrapper around `CppProjectBuilder.Build`) | `BuildService.cs` |
| `BL6011` | `Exe` project has no entry point in either language | `NativeEntryPoints.cs` |
| `BL6012` | `Exe` project has more than one entry point across both languages | `NativeEntryPoints.cs` |
| `BL6013` | `Library` project defines an entry point | `NativeEntryPoints.cs` |
| `BL6014` | C/C++ sources on a managed backend (C#/MSIL/LLVM) | `Compiler.cs` |
| `BL6015` | The requested / pinned C++ toolchain is not installed | `CppProjectBuilder.cs`, `BuildService.cs` |
| `BL6016` | .NET type not found | `SemanticAnalyzer.cs` |
| `BL6017` | .NET member not found / no matching overload | `SemanticAnalyzer.cs`, `CppCodeGenerator.NetCalls.cs` |
| `BL6018` | Ambiguous .NET **overload** | `SemanticAnalyzer.cs` |
| `BL6019` | Unsupported marshaling at the .NET boundary | `SemanticAnalyzer.cs`, `CppCodeGenerator.NetCalls.cs`, `CppCodeGenerator.cs` |
| `BL6020` | AOT-incompatible member, mapped from any ILC trim/AOT diagnostic | `Compiler/CodeGen/Net/AotDiagnosticMapper.cs` |
| `BL6021` | .NET reference unresolvable, unreadable as managed metadata, or a `<ProjectReference>` | `Net/NetReferenceResolver.cs`, `Net/NetTypeResolver.cs`, `TypeRegistry.cs` |
| `BL6022` | `<NetProxy>` names an unknown or not-effectively-public type | `Net/NetSurfaceCollector.cs` |
| `BL6023` | Ambiguous .NET **type** reference | `SemanticAnalyzer.cs`, `Net/NetSurfaceCollector.cs` |
| `BL6024` | .NET call inside a BasicLang generic body | `SemanticAnalyzer.cs` |
| `BL6025` | Library output with a non-empty .NET surface | `CppProjectBuilder.cs` |
| `BL6026` | <span class="pill warn">Warning</span> `<NetProxy>` member omitted as unmarshalable or AOT-hostile | `Net/NetSurfaceCollector.cs` |
| `BL6027` | <span class="pill warn">Warning</span> the blnet C++ facade omits a colliding C++ name; call the mangled slot instead | `Compiler/CodeGen/Net/NetProxyEmitter.Facade.cs` |

> [trap] **Six of these depend on the backend.** `BL6016`, `BL6017`, `BL6018`, `BL6019`,
> `BL6023` and `BL6024` are build **errors** on the native backend — it cannot emit a proxy
> for an unresolved member. On the C# backend, which defers to `csc`, the ones raised through
> `NetWarning` become warnings (`SemanticAnalyzer.cs:2793-2806`, `IsWarning: !_netNativeBackend`)
> and the ones raised through `NetErrorNativeOnly` vanish entirely
> (`SemanticAnalyzer.cs:4146-4150`, an early `return` when the backend is not native).
> `BL6026` and `BL6027` are always warnings.

> [note] `BL6002`, `BL6003` and `BL6004` were retired with the legacy IDE transpile fork,
> and `BL6008` was retired when mixed BasicLang/C++ projects became supported. `BL6027` is
> the only code added since this page was first written.

> [trap] A diagnostic's message text must never name a *different* code than the one the
> diagnostic carries. Users seeing "BL6001" above text naming another code is exactly the
> confusion this rule exists to prevent — see the comment in
> `CppCodeGenerator.NetCalls.cs`.

## BL7xxx — JavaScript backend

| Code | Rejected construct |
|---|---|
| `BL7002` | `ByRef` parameter |
| `BL7003` | `Long` |
| `BL7004` | `Char` |
| `BL7005` | Value `Structure` |
| `BL7006` | Operator overload |
| `BL7007` | Unavailable BCL type |
| `BL7008` | LINQ operator with no faithful `Array`-method lowering |
| `BL7009` | `::` name with an interior namespace qualification, or a `::` naming nothing |
| `BL7010` | Unlowerable `#JsImport` |
| `BL7011` | Type name colliding with a provided global |
| `BL7012` | Non-provided exception type |

> [note] `BL7001` is reserved, not missing. It was to reject method overloading, but
> BasicLang's analyzer already refuses duplicate signatures, so the arm could never fire
> (`JsCapabilityChecker.cs:36-37`). `JsCapabilityCheckerTests.cs:154-180` is a tripwire that
> goes red the day BasicLang gains overloading, because JS has no overload resolution and the
> last definition would silently win.

## Where diagnostics surface

| Surface | Path |
|---|---|
| CLI | Front-end errors printed raw as `    {Error\|Warning}: {message}` (`Program.cs:553-554`) — no code, no caret line. .NET findings as `    {Error\|Warning}: {Code}: {Message}` (`:540-541`); native diagnostics via `CppDiagnosticsParser.FormatNormalized` (`:454`) |
| IDE Error List / Problems panel | `DiagnosticsAggregator` merges three keyspaces: LSP `publishDiagnostics`, build results from `BuildService.cs` (the `BL0xxx`/`BL4001`/`BL6010`/`BL9998` codes and stamped `BL3001`/`BL3002`), and per-extension collections. Native compiler output is parsed by `CppDiagnosticsParser.cs` |
| Editor squiggles | LSP `publishDiagnostics`, with diagnostic tags for deprecated/unnecessary. **No `code` is set** (`DiagnosticsService.cs` fills Range/Severity/Source/Message/Tags only), so a squiggle never shows its BL code |
| VS 2022 / VS Code | Their own LSP clients, same server |
