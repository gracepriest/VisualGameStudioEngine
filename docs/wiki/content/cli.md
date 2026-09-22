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
| `add package <id>` | Add a NuGet package. <span class="pill crit">`--version` unreachable</span> — see the trap under this table |
| `remove package <id>` | Remove a package |
| `list packages` | List installed packages (`list templates` lists the 11 built-in project templates, same as `new --list`) |
| `search <query>` | Search NuGet for packages |

> [trap] The global flags are matched against the **whole** argument list before any
> subcommand is dispatched (`BasicLang/Program.cs:34-89`), so a subcommand flag that
> collides with one of them never arrives. `basiclang add package X --version 13.0.3` —
> and the `-v` spelling — prints the version banner and exits 0 without touching the
> project; `add`'s own version parser at `Program.cs:361` is dead code. `-i`, `-h`,
> `--repl`, `--lsp` and `--debug-adapter` anywhere on the line hijack the run the same way.

With no command, a path is treated as a source file to compile — **there is no `compile`
subcommand**. A `.blproj` path is recognised too and routes straight to `build`; a `.bli`
declaration file is rejected (exit 1 — compile the `.bas` that uses it); any other
unrecognised positional argument prints usage and exits **2**. With no arguments at all the
binary runs its built-in multi-target demo showcase, not the help text:

```powershell
IDE/BasicLang.exe program.bas --target=csharp
```

## Options

> [trap] Only the long-running modes and `--help` / `--version` are matched before subcommand
> dispatch. `--target=`, `--output=`, `--optimize`, `--search-path=` and `--show-generated`
> are parsed **only** on the single-file compile route (`CompileFile`,
> `BasicLang/Program.cs:1060`) — `build` and `run` silently ignore them, taking the backend
> and optimization level from the `.blproj` and its configuration block and writing to
> `bin/<config>/<tfm>`. `-c` / `--configuration` is the mirror image: only `build` and `run`
> read it.

| Option | Meaning |
|---|---|
| `--target=X` | Backend: `csharp`, `cpp`, `javascript` (or `js`), `llvm`, `msil` |
| `--output=FILE` | Output file path |
| `--optimize`, `-O` | Enable aggressive optimizations |
| `--search-path=DIR` | Add a module search path |
| `--show-generated`, `--show-cs` | Print the generated code — the fastest way to debug a backend |
| `--repl`, `-i`, `--interactive` | Interactive REPL |
| `--lsp`, `--language-server` | Run as a Language Server Protocol server (add `--lsp-simple` for the minimal fallback server) |
| `--debug-adapter`, `--dap` | Run as a Debug Adapter Protocol adapter (add `--dap-legacy` alongside it to select the old interpreter-based `DebugSession`) |
| `--help`, `-h` | Help |
| `--version`, `-v` | Version |
| `-c`, `--configuration <name>` | Configuration for `build` / `run` (default `Debug`) |
| `--parser-tests` | Run the in-tree parser harness (`BasicLang/ParserTests.cs`); checked after subcommand dispatch, so pass it on its own |

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

Drives the full pipeline interactively through the `REPL` class in `BasicLang/REPL.cs`,
using the IR interpreter for execution. `BasicLang/BasicLangRepl.cs` is an older,
unreferenced REPL — nothing in the tree constructs it.

## Build output, read correctly

A successful managed build prints `Build succeeded. Output: <path>`; a native (C++) build
prints an indented `  Build succeeded.` after its toolchain messages. Failures print
`Build failed.`, and **every diagnostic goes to stderr** in the flat,
problem-matcher-parsable shape `    Error: <message>` / `    Warning: <message>` — they are
*not* grouped under the `  Compiling <file>...` headings, which are all printed before
compilation even starts. A JavaScript build adds `  Site written to: <dir>`; the reminder
to serve that directory over HTTP (ES modules will not load from `file://`) comes from
`basiclang run`, not from the build.

> [trap] When scripting the CLI or the test host, **capture both stdout and stderr**. A
> crashed .NET test host still prints a per-assembly `Passed!` summary line while the
> abort goes to stderr — a summary line is not proof that anything passed.
