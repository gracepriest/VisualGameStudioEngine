# BasicLang REPL (Read-Eval-Print-Loop)

## Overview

The BasicLang REPL provides an interactive command-line environment where you can:
- Execute BasicLang code immediately
- Define variables and functions that persist across commands
- Test code snippets quickly
- Load and execute BasicLang files
- Navigate command history with arrow keys

## Starting the REPL

Run the BasicLang compiler with the `--repl` flag:

```bash
basiclang --repl
# or
basiclang -i
# or
basiclang --interactive
```

## Features

### 1. Interactive Code Execution

Simply type BasicLang code and press Enter:

```vb
>>> 5 + 3
=> 8

>>> "Hello" & " " & "World"
=> "Hello World"

>>> Dim x As Integer = 10
>>> x * 2
=> 20
```

### 2. Multi-line Input Support

The REPL automatically detects incomplete statements and continues reading:

```vb
>>> Function Double(n As Integer) As Integer
...     Return n * 2
... End Function
```

You can also explicitly continue lines with underscore (`_`):

```vb
>>> Dim message As String = _
...     "This is a " & _
...     "long message"
```

### 3. State Persistence

Variables and functions defined in the REPL persist throughout the session:

```vb
>>> Dim counter As Integer = 0
>>> counter = counter + 1
>>> counter
=> 1

>>> Function Add(a As Integer, b As Integer) As Integer
...     Return a + b
... End Function
>>> Add(5, 3)
=> 8
```

### 4. Command History

Use the **up** and **down arrow keys** to navigate through previous commands. The REPL maintains a complete history of all executed commands during the session.

### 5. Special Commands

All special commands start with a colon (`:`):

| Command | Shortcut | Description |
|---------|----------|-------------|
| `:help` | `:h`, `:?` | Show help information |
| `:quit` | `:q`, `:exit` | Exit the REPL |
| `:clear` | `:cls` | Clear the screen |
| `:reset` | - | Clear all variables and functions |
| `:vars` | `:v` | Show all defined variables |
| `:funcs` | `:f` | Show all defined functions |
| `:history` | `:hist` | Show command history |
| `:load <file>` | `:l <file>` | Load and execute a BasicLang file |
| `:save <file>` | `:s <file>` | Save command history to a file |
| `:type <var>` | `:t <var>` | Show the type of a variable |

### 6. Loading Files

Load and execute BasicLang files directly in the REPL:

```vb
>>> :load examples.bas
Loading examples.bas...
Successfully loaded examples.bas
```

This is useful for:
- Loading library code
- Running test suites
- Importing function definitions

### 7. Helpful Error Messages

The REPL provides detailed error messages with line numbers and severity levels:

```vb
>>> Dim x As Integer = "hello"
Error: Type mismatch
    at line 1, column 20
```

## Usage Examples

### Simple Calculations

```vb
>>> 2 + 2
=> 4

>>> 10 / 3
=> 3.3333333333333335

>>> Sqrt(16.0)
=> 4
```

### Variables

```vb
>>> Dim name As String = "Alice"
>>> Dim age As Integer = 30
>>> PrintLine("Hello, " & name & "! You are " & age & " years old.")
Hello, Alice! You are 30 years old.
```

### Functions

```vb
>>> Function Greet(name As String) As String
...     Return "Hello, " & name & "!"
... End Function

>>> Greet("World")
=> "Hello, World!"
```

### Loops

```vb
>>> Dim i As Integer
>>> For i = 1 To 5
...     PrintLine(i)
... Next i
1
2
3
4
5
```

### Arrays

```vb
>>> Dim numbers[3] As Integer
>>> numbers[0] = 10
>>> numbers[1] = 20
>>> numbers[2] = 30
>>> :vars
Defined Variables:
─────────────────────────────────────────────────────────
  numbers              : Int32[]         = {10, 20, 30}
```

### String Operations

```vb
>>> Dim text As String = "BasicLang"
>>> UCase(text)
=> "BASICLANG"

>>> LCase(text)
=> "basiclang"

>>> Len(text)
=> 9
```

### Math Functions

```vb
>>> Abs(-10)
=> 10

>>> Pow(2.0, 10.0)
=> 1024

>>> Max(5.0, 10.0)
=> 10
```

## Keyboard Shortcuts

- **Enter**: Execute the current line/block
- **Up Arrow**: Navigate to previous command in history
- **Down Arrow**: Navigate to next command in history
- **Left/Right Arrow**: Move cursor within the current line
- **Home**: Move cursor to beginning of line
- **End**: Move cursor to end of line
- **Backspace**: Delete character before cursor
- **Delete**: Delete character at cursor
- **Tab**: Insert 4 spaces (can be used for indentation)

## Tips and Tricks

1. **Quick Testing**: Use the REPL to test small code snippets before adding them to your main program.

2. **Function Library**: Define commonly-used functions in a separate file and load it with `:load` at the start of your REPL session.

3. **Debug Helper**: Use `:vars` and `:type` to inspect the state of your program.

4. **Save Progress**: Use `:save session.bas` to save your command history for later reference.

5. **Clean Slate**: Use `:reset` to clear all state and start fresh without restarting the REPL.

6. **Multi-line Editing**: When writing multi-line code, the REPL automatically continues if the block is incomplete. No special syntax required for most cases.

## Limitations

1. **No Up-Arrow Mid-Block**: History navigation works before starting a multi-line block, but not while editing one.

2. **No Tab Completion**: Tab completion for variable names and functions is not currently implemented.

3. **Performance**: The REPL uses an interpreter, which is slower than compiled code. For performance-critical code, compile to executable.

4. **State Isolation**: Each REPL session maintains its own state. State is not persisted between REPL sessions.

## Error Handling

The REPL provides three types of errors:

1. **Parse Errors**: Syntax errors in your BasicLang code
   ```
   Parse error: Unexpected token 'End'
   ```

2. **Semantic Errors**: Type mismatches, undefined variables, etc.
   ```
   Error: Undefined variable 'x'
   ```

3. **Runtime Errors**: Errors that occur during execution
   ```
   Runtime error: Division by zero
   ```

## Advanced Features

### Loading Libraries

Create a file `mylib.bas` with common functions:

```vb
' mylib.bas
Function Square(n As Integer) As Integer
    Return n * n
End Function

Function IsEven(n As Integer) As Boolean
    Return n Mod 2 = 0
End Function
```

Load it in the REPL:
```vb
>>> :load mylib.bas
>>> Square(5)
=> 25
>>> IsEven(4)
=> True
```

### Exploring State

```vb
>>> :vars
Defined Variables:
─────────────────────────────────────────────────────────
  counter              : Int32           = 42
  message              : String          = "Hello"

>>> :funcs
Defined Functions:
─────────────────────────────────────────────────────────
  Square(n As Integer) As Integer
  IsEven(n As Integer) As Boolean

>>> :type counter
counter : System.Int32
```

## Troubleshooting

**Q: The REPL doesn't start**
- A: Make sure you're using the correct flag: `--repl`, `-i`, or `--interactive`

**Q: My variable isn't showing up in `:vars`**
- A: Make sure you declared it with `Dim`. Bare expressions don't create variables.

**Q: Multi-line input isn't working**
- A: The REPL auto-detects incomplete blocks. Make sure you're using proper `End` statements (e.g., `End Function`, `End If`).

**Q: How do I exit?**
- A: Use `:quit`, `:q`, `:exit`, or press Ctrl+C

## Examples File

See `REPL_Examples.bas` for a comprehensive set of examples demonstrating REPL features.

## Contributing

Found a bug or want to suggest a feature? Please open an issue on the BasicLang GitHub repository.
