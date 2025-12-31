# BasicLang REPL - Quick Start Guide

## Getting Started in 60 Seconds

### 1. Start the REPL
```bash
basiclang --repl
```

### 2. Try Your First Expression
```vb
>>> 2 + 2
=> 4
```

### 3. Create a Variable
```vb
>>> Dim message As String = "Hello, REPL!"
>>> message
=> "Hello, REPL!"
```

### 4. Define a Function
```vb
>>> Function Square(x As Integer) As Integer
...     Return x * x
... End Function
>>> Square(7)
=> 49
```

### 5. Get Help
```vb
>>> :help
```

## Essential Commands

| Type This | To Do This |
|-----------|------------|
| `:help` | Show all commands |
| `:vars` | See your variables |
| `:funcs` | See your functions |
| `:clear` | Clear the screen |
| `:quit` | Exit the REPL |

## Quick Examples

### Math
```vb
>>> 10 * 5
=> 50
>>> Sqrt(16.0)
=> 4
```

### Strings
```vb
>>> "Hello" & " " & "World"
=> "Hello World"
>>> UCase("hello")
=> "HELLO"
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
>>> Dim nums[3] As Integer
>>> nums[0] = 10
>>> nums[1] = 20
>>> nums[2] = 30
>>> nums
=> {10, 20, 30}
```

## Multi-line Input

The REPL automatically continues when you have incomplete blocks:

```vb
>>> Function Factorial(n As Integer) As Integer
...     If n <= 1 Then
...         Return 1
...     Else
...         Return n * Factorial(n - 1)
...     End If
... End Function
```

Just type naturally - the REPL knows when you're done!

## Tips

1. **Use Up/Down arrows** to navigate command history
2. **End lines with `_`** for explicit continuation
3. **Use `:load filename.bas`** to run files
4. **Use `:reset`** to start fresh without restarting
5. **Check `:vars`** to see what you've defined

## Need More Help?

- Type `:help` in the REPL
- Read `REPL_README.md` for detailed documentation
- Try examples in `REPL_Examples.bas` with `:load REPL_Examples.bas`

## Example Session

```vb
╔════════════════════════════════════════════════════════════════╗
║                 BasicLang Interactive REPL                     ║
║                        Version 1.0                             ║
╚════════════════════════════════════════════════════════════════╝

Type :help for available commands

>>> Dim counter As Integer = 0
>>> counter = counter + 1
>>> counter
=> 1
>>> Function Greet(name As String) As String
...     Return "Hello, " & name & "!"
... End Function
>>> Greet("Alice")
=> "Hello, Alice!"
>>> :vars
Defined Variables:
─────────────────────────────────────────────────────────
  counter              : Int32           = 1
>>> :funcs
Defined Functions:
─────────────────────────────────────────────────────────
  Greet(name As String) As String
>>> :quit
Goodbye!
```

---

**Ready to explore?** Start with `:load REPL_Examples.bas` to see many more examples!
