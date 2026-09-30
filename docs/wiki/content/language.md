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

String literals follow VB: a backslash is an ordinary character, so `"C:\temp\new"` is
the eleven characters it shows. The only escape is a doubled quote, `"say ""hi"""`. In an
interpolated string, `{{` and `}}` are literal braces. For control characters use
`vbCrLf`, `vbNewLine`, `vbCr`, `vbLf` or `vbTab`, which work on every backend.

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
| Reference identity | `Is` `IsNot` — `x Is Nothing`, `a IsNot b`; references only (use `=` for values) |
| Logical | `And` `Or` `Not` `AndAlso` `OrElse` |
| Bitwise | `And` `Or` `Xor` `Shl` `Shr` |
| String | `&` (concatenate) |
| Compound | `+=` `-=` `*=` `/=` `&=` and the rest |

As in VB, `Not` binds looser than every comparison and tighter than `And`/`Or`:
`Not x Is Nothing` is `Not (x Is Nothing)`, `Not n = 5` is `Not (n = 5)`, and
`Not a And b` is `(Not a) And b`.

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

`:` joins statements on one line. A single-line `If` owns everything to the end of its line, so
a `:`-joined statement runs only when its branch does, and an `Else` binds to the nearest `If`:

```vb
If n > 0 Then Return "pos" Else Return "other"
If ready Then Start() : Log("started")          ' both run only when ready
If x < 0 Then x = 0 Else x = x * 2 : Log("x")   ' Log is part of the Else
If a Then If b Then P() Else Q()                ' the Else belongs to If b
For i = 1 To 3 : Console.Write(i) : Next
```

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
class. `MyBase` reaches the base implementation. Classes and interfaces may be declared in
any order — a base class or an implemented interface can appear below the code that uses it.
An inheritance cycle (`A Inherits B`, `B Inherits A`) is refused.

A class can declare its own binary operators; `a = b`, `a + b` and so on then call them,
including for a derived class. At least one parameter must be the class, and `=`/`<>`,
`<`/`>` and `<=`/`>=` come in pairs:

```vb
Public Shared Operator =(a As Money, b As Money) As Boolean
    Return a.Cents = b.Cents
End Operator
Public Shared Operator <>(a As Money, b As Money) As Boolean
    Return Not a = b
End Operator
```

Supported: `+ - * / Mod = <> < > <= >= And Or Xor`, on the C# and C++ backends. `Is`
still compares references. JavaScript has no operator overloading and refuses the class
(BL7006) — use a named `Shared Function` there.

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

`TypeOf x Is T` tests an object's runtime type (`TypeOf x IsNot T` is the negation):

```vb
If TypeOf pet Is Dog Then Console.WriteLine("woof")
```

`T` is a class or an interface. `TypeOf` on a value type, or between two classes neither of
which derives from the other, is refused. JavaScript tests a class target but refuses an
interface (BL7013) — it has no interfaces to test.

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

Declare a fixed-size array with square brackets. The number is the **element count**:

```vb
Dim nums[10] As Integer              ' 10 elements, nums[0] .. nums[9]
Dim grid[3, 4] As Integer            ' 3 x 4
```

Parentheses are also accepted, so older BASIC programs run unchanged. There the number is
the **upper bound**, as in VB and classic BASIC, so the array has one more element:

```vb
Dim scores(9) As Integer             ' upper bound 9: 10 elements, scores(0) .. scores(9)
```

Prefer `[]` in new code. The two spellings differ only in the declaration: once declared,
an array is indexed with either `nums[i]` or `nums(i)` (`grid[x, y]` or `grid(x, y)`), and a
one-dimensional array's `Length` counts its elements whichever form declared it. A size
must be a compile-time constant (a literal, a `Const`, or arithmetic over them).

For a size known only at run time, declare the array unsized and `ReDim` it. `ReDim` follows
the same rule: brackets give a count, parentheses an upper bound. `Preserve` keeps the
elements that still fit; without it the array starts over with default values.

```vb
Dim items[] As Integer
ReDim items[n]                       ' n elements, all 0
ReDim Preserve items[n * 2]          ' grow; the first n keep their values
ReDim scores(n)                      ' older-BASIC form: n + 1 elements
```

`ReDim` resizes a one-dimensional array, one array per statement, and keeps its element type.

Where there is no name to carry the brackets, such as a return type, a generic argument or a
cast, put them on the type. Leave them empty, because a type names no size. Commas give
the rank. The suffix belongs on the name or on the type, never both, because jagged arrays are
not supported:

```vb
Function Squares(n As Integer) As Integer[]      ' or As Integer()
    Dim r[] As Integer
    ReDim r[n]
    For i As Integer = 0 To n - 1
        r(i) = i * i
    Next
    Return r
End Function

Sub Show(values As Integer[])                    ' same as values[] As Integer, or values() As Integer
Dim rows As New List(Of String[])()
Dim grid As Integer[,]                           ' rank 2, unsized
```

```vb
Dim primes = {2, 3, 5, 7, 11}        ' initialised
Dim none[] As Integer = {}           ' empty: {} takes the type it is stored in

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

Conditional compilation removes code before the compiler ever sees it. BasicLang has VB's
`#If` form and the older `#IfDef` family; both nest, and `#End If` and `#EndIf` close either.

```vb
#If WEB Then
    ' only in a web (JavaScript) build
#ElseIf DESKTOP AndAlso DEBUG Then
    ' a desktop build in the Debug configuration
#Else
    ' everything else
#End If

#IfDef TRACE             ' is TRACE defined?
    Console.WriteLine("tracing")
#Else
    ' not defined
#EndIf

#IfNDef SHIPPING         ' is SHIPPING NOT defined?
#EndIf

#Define LOCAL            ' defines LOCAL for the rest of THIS file

#Region "Initialization"
#End Region
```

An `#If` / `#ElseIf` condition is made of symbol names, `Not`, `And` / `AndAlso`,
`Or` / `OrElse`, parentheses and `True` / `False` (`Not` binds tightest, then `And`, then
`Or`). There are no values or comparisons: a symbol is either defined or it is not, and a
symbol nothing defines is simply false. Symbol names are not case-sensitive.

`#Define` works per file, like VB's `#Const`: the symbol is defined from that line to the
end of its own file, including any file that file `#Include`s after it (an include is
spliced into the file). It never reaches another file of the project. For a symbol every
file sees, use the build's symbols below or `<DefineConstants>`.

The build defines these symbols for you:

| Symbol | Defined when |
|---|---|
| `WEB` | the target is JavaScript |
| `DESKTOP` | the target is anything else (C#, C++) |
| `DEBUG` | the build configuration is Debug |
| `RELEASE` | the build configuration is Release (any other configuration defines neither) |

A project adds its own through `<DefineConstants>` (separated by `;` or `,`). As in VB,
`NAME=False` or `NAME=0` means *not* defined, `NAME`, `NAME=True`, `NAME=1` or `NAME=-1`
defines it, and the last entry for a name wins. `WEB` and `DESKTOP` come from the target and
cannot be set or cleared there. From the CLI, a single-file compile is a Debug build unless
you pass `--configuration=Release`, and `--define=A;B` adds symbols.

> **Changed:** `DEBUG` and `RELEASE` are now defined by the build configuration, and
> `WEB`/`DESKTOP` by the target — code under `#IfDef DEBUG` now compiles in Debug builds.
> A program that never names these four symbols compiles exactly as before, and one that
> writes its own `#Define DEBUG` behaves as it always did.
>
> **Changed:** a `#Define` no longer leaks into the files compiled after it in the same
> project — it applies to its own file only. Move a symbol every file needs into
> `<DefineConstants>`.

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
