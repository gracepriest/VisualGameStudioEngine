title: Command line
lede: `BasicLang.exe` is the compiler, the project builder, the package client, the REPL, the language server and the debug adapter.
---
```text
Usage: basiclang [command] [options]
```

The binary is at `IDE/BasicLang.exe` in the prebuilt drop, or in the build output of
`BasicLang/BasicLang.csproj`.

## Commands

| Command | What it does |
|---|---|
| `new <template>` | Create a project from a template |
| `build` | Build a project |
| `run` | Build and run a project |
| `restore` | Restore NuGet packages |
| `add package <id>` | Add a NuGet package (`--version <v>` optional) |
| `remove package <id>` | Remove a package |
| `list packages` | List installed packages |
| `search <query>` | Search NuGet for packages |

With no command, a path is treated as a source file to compile — **there is no `compile`
subcommand**:

```powershell
IDE/BasicLang.exe program.bas --target=csharp
```

## Options

| Option | Meaning |
|---|---|
| `--target=X` | Backend: `csharp`, `cpp`, `javascript` (or `js`), `llvm`, `msil` |
| `--output=FILE` | Output file path |
| `--optimize` | Enable aggressive optimizations |
| `--search-path=DIR` | Add a module search path |
| `--show-generated` | Print the generated code — the fastest way to debug a backend |
| `--repl`, `-i` | Interactive REPL |
| `--lsp` | Run as a Language Server Protocol server |
| `--debug-adapter` | Run as a Debug Adapter Protocol adapter (`--dap-legacy` for the old one) |
| `--help`, `-h` | Help |
| `--version`, `-v` | Version |

> [note] An unknown `--target` is a hard error listing the valid targets. It used to fall
> through to C#, which meant `--target=jscript` silently wrote a `.cs` file. See
> [Backends](#/backends) for the wider version of this trap.

## Examples

```powershell
basiclang new console -n MyApp          # new console app
basiclang new game -n MyGame            # new game project
basiclang new web -n MySite             # new JavaScript/browser project
basiclang new --list                    # list templates

basiclang build                         # build the project in this directory
basiclang build MyProject.blproj        # build a specific project
basiclang run                           # build and run

basiclang add package Newtonsoft.Json
basiclang restore

basiclang --repl                        # interactive
basiclang program.bas                   # compile one file (default target)
basiclang program.bas --target=cpp --show-generated
```

## The three long-running modes

### `--lsp`

Speaks LSP over stdio. This is what the IDE, the VS 2022 extension and the VS Code
extension all launch. See [Language server](#/lsp).

### `--debug-adapter`

Speaks DAP over stdio, driving the managed debugger for C#-backend builds.
`--dap-legacy` selects the previous implementation. See [Debugging](#/debugging).

### `--repl` / `-i`

Drives the full pipeline interactively through `BasicLangRepl.cs` / `REPL.cs`, using the
IR interpreter for execution.

## Build output, read correctly

A successful project build prints `Build succeeded.`; failures print each diagnostic
indented beneath the failing file. For a JavaScript build the CLI also reminds you to
serve the output over HTTP, because ES modules will not load from `file://`.

> [trap] When scripting the CLI or the test host, **capture both stdout and stderr**. A
> crashed .NET test host still prints a per-assembly `Passed!` summary line while the
> abort goes to stderr — a summary line is not proof that anything passed.
