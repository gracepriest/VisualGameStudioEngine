# BasicLang Language Guide

BasicLang is a modern programming language with VB-like syntax, designed for game development and general-purpose programming. It compiles to C#, MSIL, LLVM IR, or C++ and has full .NET interop.

## Table of Contents

1. [Variables and Types](#variables-and-types)
2. [Operators](#operators)
3. [Control Flow](#control-flow)
4. [Pattern Matching](#pattern-matching)
5. [Functions and Subroutines](#functions-and-subroutines)
6. [Object-Oriented Programming](#object-oriented-programming)
7. [Generics / Templates](#generics--templates)
8. [Arrays and Collections](#arrays-and-collections)
9. [LINQ](#linq)
10. [Async/Await](#asyncawait)
11. [Error Handling](#error-handling)
12. [Modules](#modules)
13. [Preprocessor Directives](#preprocessor-directives)
14. [.NET Interop](#net-interop)
15. [Multi-File Projects](#multi-file-projects)
16. [Project Files (.blproj)](#project-files-blproj)
17. [Compiler Commands](#compiler-commands)
18. [Built-in Functions](#built-in-functions)

---

## Variables and Types

### Declaration

```vb
' Explicit type declaration
Dim x As Integer = 10
Dim name As String = "Hello"
Dim price As Double = 19.99
Dim isActive As Boolean = True

' Type inference with Auto
Auto count = 42          ' Inferred as Integer
Auto message = "Hi"      ' Inferred as String
Auto ratio = 0.5F        ' Inferred as Single

' Constants
Const PI = 3.14159
Const MAX_SIZE As Integer = 100

' Multiple declarations on one line
Dim a, b, c As Integer
```

### Built-in Types

| Type | Description | Example |
|------|-------------|---------|
| `Integer` | 32-bit signed integer | `42` |
| `Long` | 64-bit signed integer | `42L` |
| `Short` | 16-bit signed integer | `42S` |
| `Byte` | 8-bit unsigned integer | `255` |
| `Single` | 32-bit floating point | `3.14F` |
| `Double` | 64-bit floating point | `3.14` |
| `String` | Unicode text | `"Hello"` |
| `Boolean` | True or False | `True` |
| `Char` | Single Unicode character | `"A"c` |
| `Object` | Any reference type | — |

---

## Operators

### Arithmetic
```vb
Dim a = 10 + 5    ' Addition:         15
Dim b = 10 - 5    ' Subtraction:      5
Dim c = 10 * 5    ' Multiplication:   50
Dim d = 10 / 3    ' Division:         3.333...
Dim e = 10 \ 3    ' Integer division: 3
Dim f = 10 Mod 3  ' Modulo:           1
Dim g = 2 ^ 3     ' Power:            8
```

### Comparison
```vb
a = b      ' Equal
a <> b     ' Not equal
a < b      ' Less than
a > b      ' Greater than
a <= b     ' Less than or equal
a >= b     ' Greater than or equal
```

### Logical
```vb
a AndAlso b  ' Short-circuit AND (logical)
a OrElse b   ' Short-circuit OR  (logical)
Not a        ' Logical NOT
```

### Bitwise
```vb
a And b    ' Bitwise AND  (&)
a Or b     ' Bitwise OR   (|)
a Xor b    ' Bitwise XOR  (^)
Not a      ' Bitwise NOT  (~)
a Shl n    ' Left shift   (<<)
a Shr n    ' Right shift  (>>)
```

### String
```vb
Dim s = "Hello" & " " & "World"  ' Concatenation
Dim t = $"Value is {x}"          ' Interpolation
```

### Compound Assignment
```vb
x += 5    ' x = x + 5
x -= 5    ' x = x - 5
x *= 2    ' x = x * 2
x /= 2    ' x = x / 2
x \= 2    ' x = x \ 2  (integer division)
x Mod= 3  ' x = x Mod 3
x &= "!"  ' x = x & "!" (string concat)
x And= y  ' x = x And y (bitwise)
x Or= y   ' x = x Or y
x Xor= y  ' x = x Xor y
x <<= 1   ' x = x Shl 1
x >>= 1   ' x = x Shr 1
```

---

## Control Flow

### If Statement
```vb
If score >= 90 Then
    grade = "A"
ElseIf score >= 80 Then
    grade = "B"
ElseIf score >= 70 Then
    grade = "C"
Else
    grade = "F"
End If

' Single-line form
If lives = 0 Then GameOver()
```

### Select Case
```vb
Select Case dayOfWeek
    Case 1
        dayName = "Monday"
    Case 2
        dayName = "Tuesday"
    Case 6, 7
        dayName = "Weekend"
    Case Is > 7
        dayName = "Invalid"
    Case Else
        dayName = "Unknown"
End Select
```

### For Loop
```vb
' Basic
For i = 1 To 10
    PrintLine(i)
Next

' With inline type declaration
For i As Integer = 1 To 10
    PrintLine(i)
Next

' With Step
For i = 10 To 1 Step -1
    PrintLine(i)
Next

For i = 0 To 100 Step 5
    PrintLine(i)
Next
```

### For Each
```vb
' Inferred element type
For Each item In collection
    PrintLine(item)
Next

' Explicit element type
For Each item As String In names
    PrintLine(item)
Next
```

### While Loop
```vb
While condition
    ' statements
Wend

' Alternative
Do While condition
    ' statements
Loop
```

### Do Loop
```vb
' Do While (check at start)
Do While count < 10
    count += 1
Loop

' Do Until (check at start)
Do Until count >= 10
    count += 1
Loop

' Loop While (check at end)
Do
    count += 1
Loop While count < 10

' Loop Until (check at end)
Do
    count += 1
Loop Until count >= 10
```

### Break / Continue
```vb
For i = 1 To 100
    If i = 50 Then Exit For          ' Break out of loop
    If i Mod 2 = 0 Then Continue For ' Skip to next iteration
    PrintLine(i)
Next
```

---

## Pattern Matching

Advanced `Select Case` patterns:

### When Guards
```vb
Dim x As Integer = 7
Select Case x
    Case n When n > 10
        PrintLine("Greater than 10")
    Case n When n > 0
        PrintLine("Positive")
    Case 0
        PrintLine("Zero")
    Case Else
        PrintLine("Negative")
End Select
```

### Or Patterns
```vb
Dim day As Integer = 6
Select Case day
    Case 1 Or 7
        PrintLine("Weekend")
    Case 2 Or 3 Or 4 Or 5 Or 6
        PrintLine("Weekday")
End Select
```

### Range Patterns
```vb
Dim score As Integer = 85
Select Case score
    Case 90 To 100
        PrintLine("A")
    Case 80 To 89
        PrintLine("B")
    Case 70 To 79
        PrintLine("C")
    Case Else
        PrintLine("F")
End Select
```

### Comparison Patterns
```vb
Dim value As Integer = 15
Select Case value
    Case Is > 10
        PrintLine("Greater than 10")
    Case Is < 0
        PrintLine("Negative")
    Case Else
        PrintLine("0 to 10")
End Select
```

### Type Patterns
```vb
Dim item As Object = "Hello"
Select Case item
    Case s As String
        PrintLine("String: " & s)
    Case i As Integer
        PrintLine("Integer: " & i)
    Case Else
        PrintLine("Unknown type")
End Select
```

### Nothing Pattern
```vb
Dim obj As Object = Nothing
Select Case obj
    Case Nothing
        PrintLine("Null")
    Case Else
        PrintLine("Has value")
End Select
```

---

## Functions and Subroutines

### Subroutine (No Return Value)
```vb
Sub Greet(name As String)
    PrintLine($"Hello, {name}!")
End Sub

Greet("World")
```

### Function (Returns Value)
```vb
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Dim result = Add(5, 3)
```

### Optional Parameters
```vb
Sub Log(message As String, Optional level As String = "INFO")
    PrintLine($"[{level}] {message}")
End Sub

Log("Starting")           ' Uses default level
Log("Error!", "ERROR")    ' Specifies level
```

### ByRef Parameters
```vb
Sub Increment(ByRef value As Integer)
    value += 1
End Sub

Dim x = 5
Increment(x)  ' x is now 6
```

### Forward References

Functions and subroutines can be called before their definition — the compiler performs a two-pass analysis:

```vb
Sub Main()
    PrintScore(100)   ' Called before definition — OK
End Sub

Sub PrintScore(s As Integer)
    PrintLine($"Score: {s}")
End Sub
```

---

## Object-Oriented Programming

### Classes
```vb
Class Player
    ' Fields
    Private _name As String
    Private _health As Integer

    ' Constructor
    Public Sub New(name As String)
        _name = name
        _health = 100
    End Sub

    ' Overloaded constructor
    Public Sub New(name As String, health As Integer)
        _name = name
        _health = health
    End Sub

    ' Read/write property
    Public Property Name As String
        Get
            Return _name
        End Get
        Set(value As String)
            _name = value
        End Set
    End Property

    ' Read-only property
    Public ReadOnly Property Health As Integer
        Get
            Return _health
        End Get
    End Property

    ' Methods
    Public Sub TakeDamage(amount As Integer)
        _health -= amount
        If _health < 0 Then _health = 0
    End Sub

    Public Function IsAlive() As Boolean
        Return _health > 0
    End Function
End Class

' Usage
Dim player = New Player("Hero")
player.TakeDamage(30)
PrintLine($"{player.Name} has {player.Health} HP")
```

### Inheritance
```vb
Class Enemy
    Inherits Player

    Private _damage As Integer

    Public Sub New(name As String, damage As Integer)
        MyBase.New(name)   ' Call base constructor
        _damage = damage
    End Sub

    Public Sub Attack(target As Player)
        target.TakeDamage(_damage)
    End Sub

    Public Overrides Function IsAlive() As Boolean
        Return MyBase.IsAlive() AndAlso _damage > 0
    End Function
End Class
```

### Abstract Classes
```vb
MustInherit Class Shape
    Public MustOverride Function Area() As Double
    Public MustOverride Function Perimeter() As Double

    Public Function Describe() As String
        Return $"Area={Area():F2}"
    End Function
End Class

Class Circle
    Inherits Shape

    Private _radius As Double

    Public Sub New(r As Double)
        _radius = r
    End Sub

    Public Overrides Function Area() As Double
        Return Math.PI * _radius * _radius
    End Function

    Public Overrides Function Perimeter() As Double
        Return 2 * Math.PI * _radius
    End Function
End Class
```

### Interfaces
```vb
Interface IDrawable
    Sub Draw()
    Property X As Single
    Property Y As Single
End Interface

Interface ICollidable
    Function GetBounds() As Rectangle
End Interface

' Implementing multiple interfaces
Class Sprite
    Implements IDrawable
    Implements ICollidable

    Public Property X As Single
    Public Property Y As Single

    Public Sub Draw()
        ' drawing logic
    End Sub

    Public Function GetBounds() As Rectangle
        Return New Rectangle(X, Y, 32, 32)
    End Function
End Class
```

---

## Generics / Templates

```vb
' Generic function
Function Max(Of T)(a As T, b As T) As T
    If a > b Then Return a
    Return b
End Function

Dim biggest = Max(Of Integer)(10, 20)

' Generic class
Class Stack(Of T)
    Private _items As New List(Of T)

    Public Sub Push(item As T)
        _items.Add(item)
    End Sub

    Public Function Pop() As T
        Dim last = _items(_items.Count - 1)
        _items.RemoveAt(_items.Count - 1)
        Return last
    End Function

    Public ReadOnly Property Count As Integer
        Get
            Return _items.Count
        End Get
    End Property
End Class

' Usage
Dim s As New Stack(Of Integer)
s.Push(1)
s.Push(2)
Dim top = s.Pop()   ' Returns 2
```

---

## Arrays and Collections

### Arrays
```vb
' Fixed-size declaration
Dim numbers(9) As Integer      ' 10 elements (0–9)
Dim matrix(3, 3) As Double     ' 4×4 matrix

' Array literals
Dim scores() As Integer = {10, 20, 30, 40, 50}
Dim names() As String = {"Alice", "Bob", "Charlie"}

' Access
numbers(0) = 42
matrix(1, 2) = 3.14
Dim first = scores(0)
```

### Dynamic Collections
```vb
' List
Dim list As New List(Of Integer)
list.Add(1)
list.Add(2)
list.Add(3)
list.Remove(2)
Dim count = list.Count
Dim item = list(0)

' Dictionary
Dim dict As New Dictionary(Of String, Integer)
dict("score") = 100
dict("lives") = 3
Dim score = dict("score")

If dict.ContainsKey("score") Then
    PrintLine(dict("score"))
End If
```

### Array Bounds
```vb
Dim arr() As Integer = {5, 2, 8, 1, 9}

Dim lower = LBound(arr, 1)   ' 0 (dimension arg is 1-based)
Dim upper = UBound(arr, 1)   ' 4

' Multi-dimensional
Dim grid(4, 9) As Integer
Dim rows = UBound(grid, 1) - LBound(grid, 1) + 1   ' 5
Dim cols = UBound(grid, 2) - LBound(grid, 2) + 1   ' 10
```

---

## LINQ

```vb
Dim numbers() As Integer = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10}

' Filter
Dim evens = From n In numbers
            Where n Mod 2 = 0
            Select n

' Transform
Dim doubled = From n In numbers
              Select n * 2

' Sort
Dim sorted = From n In numbers
             Order By n Descending
             Select n

' Aggregate
Dim total = Aggregate n In numbers Into Sum(n)
Dim avg   = Aggregate n In numbers Into Average(n)

' Complex query
Dim result = From n In numbers
             Where n > 3
             Order By n
             Select New With {.Value = n, .Square = n * n}

For Each item In result
    PrintLine($"{item.Value}^2 = {item.Square}")
Next
```

---

## Async/Await

```vb
Async Function LoadDataAsync() As Task(Of String)
    Dim client As New System.Net.Http.HttpClient()
    Dim response = Await client.GetStringAsync("https://api.example.com/data")
    Return response
End Function

Async Sub ProcessAsync()
    Dim result = Await LoadDataAsync()
    PrintLine(result)
End Sub

' Async main entry point
Async Function Main() As Task
    Await ProcessAsync()
End Function
```

---

## Error Handling

```vb
Try
    Dim result = RiskyOperation()
    PrintLine(result)
Catch ex As InvalidOperationException
    PrintLine($"Invalid operation: {ex.Message}")
Catch ex As ArgumentException
    PrintLine($"Bad argument: {ex.Message}")
Catch ex As Exception
    PrintLine($"Error: {ex.Message}")
Finally
    Cleanup()   ' Always runs
End Try

' Throwing exceptions
Sub ValidateAge(age As Integer)
    If age < 0 Then
        Throw New ArgumentException("Age cannot be negative")
    End If
End Sub
```

---

## Modules

Modules contain shared functions and state — no instance required:

```vb
Module MathUtils
    Public Function Clamp(value As Single, min As Single, max As Single) As Single
        If value < min Then Return min
        If value > max Then Return max
        Return value
    End Function

    Public Function Lerp(a As Single, b As Single, t As Single) As Single
        Return a + (b - a) * t
    End Function
End Module

' Usage — no instance needed
Dim clamped = MathUtils.Clamp(speed, 0, 300)
```

### Namespaces
```vb
Namespace MyGame.Entities
    Class Player
        ' ...
    End Class
End Namespace
```

---

## Preprocessor Directives

Preprocessor runs before the compiler. Inactive blocks are excluded from compilation.

```vb
' Define a symbol
#Define DEBUG

' Conditional compilation
#IfDef DEBUG
    PrintLine("Debug build")
#Else
    PrintLine("Release build")
#EndIf

' Negative check
#IfNDef SHIPPING
    PrintLine("Not a shipping build")
#EndIf

' Nesting is supported
#IfDef WINDOWS
  #IfDef DEBUG
    PrintLine("Windows debug")
  #EndIf
#EndIf

' Code regions (IDE folding only — not a compilation directive)
#Region "Initialization"
    ' ...
#End Region
```

> **Note:** Use `#IfDef` / `#IfNDef`, not `#If ... Then`.

---

## .NET Interop

Use `Using` to import .NET namespaces:

```vb
Using System
Using System.Collections.Generic
Using System.IO

Sub ReadFile(path As String)
    Dim lines = File.ReadAllLines(path)
    For Each line In lines
        Console.WriteLine(line)
    Next
End Sub

Function GetTimestamp() As String
    Return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
End Function
```

### Inline Code Blocks

For code that needs raw C# or C++:

```vb
Inline CSharp
{
    System.Console.WriteLine("Raw C# code");
}

Inline Cpp
{
    printf("Raw C++ code\n");
}
```

---

## Multi-File Projects

Use `Import` to reference other `.bas` files in the same project:

```vb
' Main.bas
Import Player
Import Utils

Sub Main()
    Dim p = New Player("Hero")
    Utils.Log($"Created {p.Name}")
End Sub
```

```vb
' Player.bas
Class Player
    ' ...
End Class
```

```vb
' Utils.bas
Module Utils
    Public Sub Log(msg As String)
        PrintLine($"[LOG] {msg}")
    End Sub
End Module
```

---

## Project Files (.blproj)

```xml
<Project>
  <Name>MyGame</Name>
  <OutputType>Executable</OutputType>
  <RootNamespace>MyGame</RootNamespace>
  <Target>CSharp</Target>
  <Files>
    <File>Main.bas</File>
    <File>Player.bas</File>
    <File>Utils.bas</File>
  </Files>
  <References>
    <Reference>System</Reference>
    <Reference>System.Collections</Reference>
  </References>
</Project>
```

**OutputType values:** `Executable`, `Library`

**Target values:** `CSharp` (default), `MSIL`, `LLVM`, `Cpp`

---

## Compiler Commands

```bash
# Compile a single file (defaults to C# target)
BasicLang.exe compile Main.bas --target=csharp

# Other backends
BasicLang.exe compile Main.bas --target=msil
BasicLang.exe compile Main.bas --target=llvm
BasicLang.exe compile Main.bas --target=cpp

# Build a full project
BasicLang.exe build MyGame.blproj

# Run a file directly
BasicLang.exe run Main.bas

# Start LSP server (used by IDE and editor extensions)
BasicLang.exe --lsp
```

---

## Built-in Functions

### Console I/O

| Function | Description |
|----------|-------------|
| `PrintLine(text)` | Print with newline |
| `Print(text)` | Print without newline |
| `Input()` | Read line from console |
| `Console.Write(text)` | .NET console write |
| `Console.ReadLine()` | .NET console read |

### String

| Function | Description |
|----------|-------------|
| `Len(str)` | String length |
| `Mid(str, start, length)` | Substring |
| `Left(str, n)` | First N characters |
| `Right(str, n)` | Last N characters |
| `UCase(str)` | Convert to uppercase |
| `LCase(str)` | Convert to lowercase |
| `Trim(str)` | Remove leading/trailing whitespace |
| `Replace(str, old, new)` | Replace substring |
| `InStr(str, search)` | Find position of substring |
| `Split(str, delimiter)` | Split into array |
| `Join(arr, delimiter)` | Join array into string |
| `Str(num)` | Number to string |
| `Val(str)` | String to number |
| `Chr(code)` | Character from ASCII code |
| `Asc(char)` | ASCII code from character |

### Math

| Function | Description |
|----------|-------------|
| `Abs(n)` | Absolute value |
| `Int(n)` | Truncate to integer |
| `Round(n)` | Round to nearest integer |
| `Sqr(n)` | Square root |
| `Rnd()` | Random number 0.0–1.0 |
| `Randomize()` | Seed the RNG |
| `Math.Sin(n)` | Sine |
| `Math.Cos(n)` | Cosine |
| `Math.Sqrt(n)` | Square root |
| `Math.Pow(base, exp)` | Power |
| `Math.Floor(n)` | Round down |
| `Math.Ceiling(n)` | Round up |
| `Math.Min(a, b)` | Minimum |
| `Math.Max(a, b)` | Maximum |
| `Math.PI` | π constant |

### Type Conversion

| Function | Description |
|----------|-------------|
| `CInt(x)` | Convert to Integer |
| `CLng(x)` | Convert to Long |
| `CSng(x)` | Convert to Single |
| `CDbl(x)` | Convert to Double |
| `CStr(x)` | Convert to String |
| `CBool(x)` | Convert to Boolean |
| `CByte(x)` | Convert to Byte |

### Array

| Function | Description |
|----------|-------------|
| `LBound(arr, dimension)` | Lower bound (dimension arg is 1-based) |
| `UBound(arr, dimension)` | Upper bound (dimension arg is 1-based) |
| `Array.Length` | Total element count |

### Miscellaneous

| Function | Description |
|----------|-------------|
| `TypeOf obj Is T` | Type check |
| `IsNothing(obj)` | Null check |
| `IIf(condition, trueVal, falseVal)` | Inline conditional |
| `Environment.Exit(code)` | Exit program |

---

## Next Steps

- [IDE User Guide](ide-guide.md) — Using the development environment
- [Game Engine Guide](engine-guide.md) — Building games
- [API Reference](../API_REFERENCE.md) — Complete API documentation
