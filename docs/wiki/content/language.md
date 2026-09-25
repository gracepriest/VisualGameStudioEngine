title: Language reference
lede: BasicLang in one page — types, control flow, OOP, generics, pattern matching, LINQ, async, and the preprocessor.
---
BasicLang reads like Visual Basic and behaves like a modern statically-typed language.
Source files use `.bas`; `.mod` and `.cls` are also recognised. In a `.cls` file, a
first code line of `Option Public` marks the implicit class public (a bare `Public`
still works but warns).

## Data types

| Type | Description | Literal |
|---|---|---|
| `Integer` | 32-bit signed | `42` |
| `Long` | 64-bit signed | `42L` |
| `Short` | 16-bit signed | `42S` |
| `Byte` | 8-bit unsigned | `255` |
| `Single` | 32-bit float | `3.14F` |
| `Double` | 64-bit float | `3.14` |
| `String` | Unicode text | `"Hello"` |
| `Boolean` | | `True` / `False` |
| `Char` | Single Unicode char | `"A"c` |
| `Object` | Any reference type | — |

Number literal prefixes: hex `&H1F`, octal `&O17`, binary `&B1010`.

## Declarations

```vb
Dim x As Integer
Dim name As String = "Player"
Auto count = 42            ' type inference
Auto ratio = 0.5F
Const MAX_HEALTH As Integer = 100
Dim a, b, c As Integer     ' several on one line
```

## Operators

| Group | Operators |
|---|---|
| Arithmetic | `+` `-` `*` `/` `\` (integer divide) `Mod` `^` |
| Comparison | `=` `<>` `<` `>` `<=` `>=` |
| Logical | `And` `Or` `Not` `AndAlso` `OrElse` |
| Bitwise | `And` `Or` `Xor` `Shl` `Shr` |
| String | `&` (concatenate) |
| Compound | `+=` `-=` `*=` `/=` `&=` and the rest |

A line continues with a trailing `_`. Comments start with `'` or `Rem`.

## Control flow

```vb
If condition Then
    ' ...
ElseIf other Then
    ' ...
Else
    ' ...
End If

For i = 0 To 10 Step 2
Next

For Each item In collection
Next

While condition
Wend

Do
Loop Until condition
```

`Exit For` / `Exit While` / `Exit Do` break; `Continue For` and friends skip to the next
iteration.

## Functions and subroutines

```vb
Sub Greet(name As String, Optional greeting As String = "Hello")
    Console.WriteLine(greeting & ", " & name)
End Sub

Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Sub Swap(ByRef a As Integer, ByRef b As Integer)
    Dim t = a : a = b : b = t
End Sub
```

Forward references are allowed — a `Sub` may call another declared later in the file.

## Classes

```vb
Class Player
    Private _health As Integer

    Public Sub New(health As Integer)
        _health = health
    End Sub

    Public Property Health As Integer
        Get
            Return _health
        End Get
        Set
            _health = value
        End Set
    End Property

    Public Sub TakeDamage(amount As Integer)
        _health -= amount
    End Sub
End Class
```

Access modifiers: `Public`, `Private`, `Protected`, `Friend`, `Protected Friend`.
Inheritance uses `Inherits`; `MustInherit` marks an abstract class, `MustOverride` an
abstract member, `Overridable` / `Overrides` a virtual one, `NotInheritable` a sealed
class. `MyBase` reaches the base implementation.

## Interfaces and modules

```vb
Interface IDamageable
    Sub TakeDamage(amount As Integer)
    ReadOnly Property IsAlive As Boolean
End Interface

Class Enemy
    Implements IDamageable
    ' ...
End Class

Module MathHelpers
    Public Function Clamp(v As Double, lo As Double, hi As Double) As Double
        If v < lo Then Return lo
        If v > hi Then Return hi
        Return v
    End Function
End Module
```

Module members are accessible without qualification once the module is in scope.

## Generics

```vb
Class Stack(Of T)
    Private _items As New List(Of T)()

    Public Sub Push(item As T)
        _items.Add(item)
    End Sub

    Public Function Pop() As T
        Dim last = _items(_items.Count - 1)
        _items.RemoveAt(_items.Count - 1)
        Return last
    End Function
End Class

Function Max(Of T As IComparable)(a As T, b As T) As T
    If a.CompareTo(b) > 0 Then Return a
    Return b
End Function
```

On the C++ backend these become **real C++ templates**, not type-erased containers.

## Pattern matching

`Select Case` carries the full pattern surface.

```vb
' When guards
Select Case x
    Case n When n > 10 : Console.WriteLine("Greater than 10")
    Case n When n > 0  : Console.WriteLine("Positive")
    Case 0             : Console.WriteLine("Zero")
    Case Else          : Console.WriteLine("Negative")
End Select

' Or patterns
Select Case day
    Case 1 Or 7 : Console.WriteLine("Weekend")
    Case Else   : Console.WriteLine("Weekday")
End Select

' Range patterns
Select Case score
    Case 90 To 100 : grade = "A"
    Case 80 To 89  : grade = "B"
End Select

' Comparison patterns
Select Case value
    Case Is > 10 : Console.WriteLine("big")
    Case Is < 0  : Console.WriteLine("negative")
End Select

' Type patterns
Select Case item
    Case s As String  : Console.WriteLine("String: " & s)
    Case i As Integer : Console.WriteLine("Integer: " & i)
End Select

' Nothing pattern
Select Case obj
    Case Nothing : Console.WriteLine("null")
End Select
```

## Arrays and collections

```vb
Dim nums(9) As Integer               ' fixed, 10 elements
Dim primes = {2, 3, 5, 7, 11}        ' initialised

Dim list As New List(Of String)()
list.Add("x")

Dim map As New Dictionary(Of String, Integer)()
map("alice") = 10

Dim seen As New HashSet(Of Integer)()
seen.Add(1)                          ' True when newly added
```

On the C++ backend collections lower to `std::shared_ptr<BasicLang::List<T>>` and keep
**reference semantics**, matching .NET. `String` and `Structure` stay values. See
[C++ backend and interop](#/cpp-interop).

## LINQ

```vb
Dim evens = From n In numbers
            Where n Mod 2 = 0
            Order By n
            Select n

Dim names = players.Where(Function(p) p.Health > 0).Select(Function(p) p.Name)
```

## Async / Await

```vb
Async Function LoadDataAsync() As Task(Of String)
    Dim result = Await FetchFromServer()
    Return result
End Function
```

On the C++ backend, async is a **synchronous `Task<T>` emulation** — there is no
scheduler. Iterators, by contrast, are real C++20 coroutines (`Generator<T>` /
`co_yield`).

## Error handling

```vb
Try
    risky()
Catch ex As InvalidOperationException
    Console.WriteLine(ex.Message)
Catch ex As Exception
    Throw
Finally
    cleanup()
End Try
```

## Preprocessor

```vb
#Define DEBUG

#IfDef DEBUG
    Console.WriteLine("debug build")
#Else
    ' shipping
#EndIf

#IfNDef SHIPPING
#EndIf

#If PLATFORM = "WINDOWS" Then
#EndIf

#Region "Initialization"
#End Region
```

Two more directives matter and are easy to confuse:

- `#Include "file.bas"` textually splices a **BasicLang** source file.
- `#CppInclude <header>` emits a real C++ `#include` — C++ backend only.

## Multi-file projects and .NET interop

```vb
Import "Utilities.bas"       ' another BasicLang file
Using System.Collections.Generic   ' a .NET namespace
```

Inline foreign code blocks let you drop into the target language where the abstraction
runs out. See [.NET interop](#/net-interop) and [C++ interop](#/cpp-interop) for the
rules and the capability checks that police them.
