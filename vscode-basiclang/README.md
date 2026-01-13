# BasicLang for Visual Studio Code

Language support for BasicLang - a modern BASIC-inspired programming language designed for game development.

## Features

- **Syntax Highlighting** - Full TextMate grammar for BasicLang syntax
- **IntelliSense** - Code completion, hover information, and signature help
- **Code Navigation** - Go to definition, find references, document symbols
- **Diagnostics** - Real-time error and warning detection
- **Code Actions** - Quick fixes and refactoring suggestions
- **Formatting** - Automatic code formatting
- **Debugging** - Full debugging support with breakpoints, stepping, and variable inspection
- **Snippets** - Code snippets for common patterns

## Requirements

- Visual Studio Code 1.80.0 or higher
- BasicLang compiler/runtime (for build and run features)

## Extension Settings

This extension contributes the following settings:

* `basiclang.languageServerPath`: Path to BasicLang language server executable
* `basiclang.enableSemanticHighlighting`: Enable semantic syntax highlighting
* `basiclang.enableInlayHints`: Enable inlay hints for types and parameters
* `basiclang.enableCodeLens`: Enable code lens for references and implementations
* `basiclang.format.tabSize`: Number of spaces for indentation
* `basiclang.format.insertSpaces`: Use spaces instead of tabs

## Commands

* `BasicLang: Restart Language Server` - Restart the language server
* `BasicLang: Show Output Channel` - Show the output channel
* `BasicLang: Build Project` - Build the current project
* `BasicLang: Run Project` - Run the current project

## Debugging

To debug a BasicLang program, create a launch configuration in `.vscode/launch.json`:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "type": "basiclang",
            "request": "launch",
            "name": "Launch BasicLang",
            "program": "${workspaceFolder}/Main.bl",
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
