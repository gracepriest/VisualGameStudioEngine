title: Diagnostics and error codes
lede: `BL1xxx` through `BL7xxx` — which phase raises which range, and the full enumerated set.
---
Every compiler diagnostic carries a structured code. The first digit is the phase, which
tells you which file to open before you read the message.

| Range | Phase | Raised by |
|---|---|---|
| `BL1xxx` | Lexer and preprocessor | `BasicLangLexer.cs`, `Preprocessor.cs` |
| `BL2xxx` | Parser | `Parser.cs` |
| `BL3xxx` | Semantic analysis | `SemanticAnalyzer.cs`, `SymbolTable.cs` |
| `BL4xxx` | IR and code generation | `IRBuilder.cs`, backends |
| `BL5xxx` | Linker and build | module resolution, file loading |
| `BL6xxx` | Native C++ backend, toolchain, .NET boundary | `CppCapabilityChecker.cs`, `BasicLang/Net/` |
| `BL7xxx` | JavaScript backend capability | `JsCapabilityChecker.cs` |
| `BL9999` | Unknown | fallback |

The enum lives in `BasicLang/ErrorFormatter.cs`; `ErrorFormatter` also owns the rendered
message shape (caret line, source excerpt, suggestion).

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
| `BL4001` | Unsupported feature |
| `BL4002` | Backend error |
| `BL4003` | Invalid IR |
| `BL5001` | File not found |
| `BL5002` | Circular import |
| `BL5003` | Duplicate module |

## BL6xxx — Native backend, toolchain, and .NET boundary

Roughly two dozen codes, raised by `CppCapabilityChecker.cs` (constructs the native
backend cannot lower), the C++ toolchain layer (compiler not found, invalid pinned path,
compile or link failure), and the .NET boundary (`BL6019` / `BL6020` at a use site when a
member cannot cross).

`BL6001` is the checker's default blob code.

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
| `BL7010` | Unlowerable `#JsImport` |
| `BL7011` | Type name colliding with a provided global |
| `BL7012` | Non-provided exception type |

## Where diagnostics surface

| Surface | Path |
|---|---|
| CLI | Formatted by `ErrorFormatter` to stdout/stderr |
| IDE Error List / Problems panel | Via the LSP `DiagnosticsService`, or parsed from native compiler output by `CppDiagnosticsParser.cs` |
| Editor squiggles | LSP `publishDiagnostics`, with diagnostic tags for deprecated/unnecessary |
| VS 2022 / VS Code | Their own LSP clients, same server |
