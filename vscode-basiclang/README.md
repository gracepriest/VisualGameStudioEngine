# BasicLang for Visual Studio Code

Language support for BasicLang - a modern BASIC-inspired programming language designed for game development.

## Features

- **Syntax Highlighting** - TextMate grammar for `.bas` and `.bl` files (plus `.blproj` project files)
- **Code Completion** - Completions provided by the BasicLang language server
- **Hover Information** - Type and symbol info on hover
- **Diagnostics** - Compiler errors reported as you type
- **Go to Definition** - Navigate to symbol definitions
- **Debugging** - Launch and debug BasicLang programs with breakpoints via the Debug Adapter Protocol (`BasicLang.exe --debug-adapter`)
- **Snippets** - Code snippets for common patterns
- **Build/Run Tasks** - `basiclang` task type and commands that invoke the BasicLang compiler, with a problem matcher that surfaces build errors in the Problems panel

Note: the language server currently provides completion, hover, diagnostics, and go to definition. Find references, document symbols, signature help, code actions, and formatting are not implemented yet.

## Requirements

- Visual Studio Code 1.82.0 or higher
- The BasicLang compiler (`BasicLang.exe`). The extension looks for it in this order:
  1. The `basiclang.languageServerPath` setting
  2. A `server/` folder inside the extension
  3. `%LOCALAPPDATA%\BasicLang` or `%PROGRAMFILES%\BasicLang`
  4. Anywhere on `PATH`

The same executable provides the language server (`--lsp`), the debug adapter (`--debug-adapter`), and the build/run commands.

## Extension Settings

* `basiclang.languageServerPath`: Path to the BasicLang compiler/language server executable. This is the only setting most users need.
* `basiclang.enableSemanticHighlighting`, `basiclang.enableInlayHints`, `basiclang.enableCodeLens`: Passed to the language server, but the server does not implement these features yet, so they currently have no effect.
* `basiclang.format.tabSize`, `basiclang.format.insertSpaces`: Reserved for a future formatter; no formatter is currently provided.

## Commands

* `BasicLang: Restart Language Server` - Restart the language server
* `BasicLang: Show Output Channel` - Show the output channel
* `BasicLang: Build Project` - Build the current project (`BasicLang.exe build <project>`)
* `BasicLang: Run Project` - Run the current project (`BasicLang.exe run <project>`)

## Tasks and Problem Matchers

The extension contributes a `basiclang` task type (tasks `build` and `run`) and two problem matchers:

* `$basiclang` - matches errors from `BasicLang.exe build <project>` output. The source file is taken from the compiler's `Compiling <file>...` line, so errors are attributed to the right file with line and column.
* `$basiclang-compile` - matches errors from compiling a single file directly (`BasicLang.exe <file> --target=...`), using the absolute path from the `Compiling: <path>` line.

Known limitation: the compiler does not print a file, line, or column for some semantic errors in `build` mode (e.g. `Error: Undefined identifier 'x'`); those errors appear in the terminal output but cannot be placed in the Problems panel.

## Debugging

Press F5 in a `.bas`/`.bl` file, or create a launch configuration in `.vscode/launch.json`:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "type": "basiclang",
            "request": "launch",
            "name": "Launch BasicLang",
            "program": "${workspaceFolder}/Program.bas",
            "stopOnEntry": false
        }
    ]
}
```

## Language Features

### Syntax Support

- Functions and Subroutines
- Classes, Modules, and Namespaces
- Properties with Get/Set
- Interfaces and Structures
- Enumerations
- Exception handling (Try/Catch/Finally)
- Async/Await
- Lambda expressions
- LINQ-style queries

### Code Examples

```basiclang
' Simple Hello World
Sub Main()
    Console.WriteLine("Hello, World!")
End Sub

' Function with parameters
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

' Class example
Class Player
    Public Property Name As String
    Public Property Health As Integer = 100

    Sub TakeDamage(amount As Integer)
        Health = Health - amount
        If Health <= 0 Then
            Console.WriteLine(Name & " has been defeated!")
        End If
    End Sub
End Class
```

## Release Notes

### 1.0.0

Initial release of BasicLang for VS Code.

## License

MIT License
