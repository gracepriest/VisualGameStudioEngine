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
| `Short` | 16-bit signed | — (no `S` suffix) |
| `Byte` | 8-bit unsigned | `255` |
| `Single` | 32-bit float | `3.14F` |
| `Double` | 64-bit float | `3.14` |
| `String` | Unicode text | `"Hello"` |
| `Boolean` | | `True` / `False` |
| `Char` | Single Unicode char | `"A"c` |
| `Object` | Any reference type | — |
| `UByte` | 8-bit unsigned (same range as `Byte`) | — |
| `UShort` | 16-bit unsigned | — |
| `UInteger` | 32-bit unsigned | — |
| `ULong` | 64-bit unsigned | — |

Number literal prefixes: hex `&H1F`, octal `&O17`, binary `&B1010`.

> [note] **Narrowing rounds half-to-even, everywhere.** `CInt(8.5)` is `8` and `Dim i As Integer = 7.5` is `8` on C#, C++, JavaScript and MSIL alike — the four backends used to give two different answers (`Convert.ToInt32` vs truncation), and the assignment path truncated on all four. An integral argument keeps its plain conversion. The conversion functions are `CInt`, `CLng`, `CDbl`, `CSng`, `CStr` and `CBool`.
>
> **A constant that does not fit its target is a compile error**, not a wrap: `Dim b As Byte = 300` is refused by the analyzer (VB's BC30439 rule), as are `b = 300`, `a(0) = 300`, `x.F = 300`, `Return 300` and `Take(300)`. It used to compile clean and then mean four different things.

## Declarations

```vb
Dim x As Integer
Dim name As String = "Player"
Auto count = 42            ' type inference
Auto ratio = 0.5F
Const MAX_HEALTH As Integer = 100
Dim a As Integer           ' one name per Dim — no comma lists
```

## Operators

| Group | Operators |
|---|---|
| Arithmetic | `+` `-` `*` `/` `\` (integer divide) `Mod` (or `%`) |
| Comparison | `=` `<>` `<` `>` `<=` `>=` |
| Logical | `And` `Or` `Not` `AndAlso` `OrElse` — plus the C spellings `&&` `\|\|` `!` (`&&`/`\|\|` short-circuit) |
| Bitwise / shift | *none* — `And`/`Or` are **Boolean-only**; `Xor`, `Shl` and `Shr` are not keywords, and `<<` / `>>` lex but no parser or analyzer arm accepts them |
| String | `&` (concatenate) |
| Compound | `+=` `-=` `*=` `/=` only (no `&=`, `\=`, `^=` or `Mod=`) |

A line continues with a trailing `_`. Comments start with `'` or `Rem`.

> [trap] **One statement per line — `:` is not a statement separator.** The lexer emits a
> `TokenType.Colon`, but `ParseBlock` only ever calls `SkipNewlines()` (which skips
> `Newline` tokens and nothing else) and `ParseStatement` has no arm for `Colon`, so the
> colon falls through to the expression parser and dies with *"Unexpected token in
> expression: ':'"*. This applies everywhere, including one-line `Case` bodies.

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

`Exit For` / `Exit While` / `Exit Do` break out of a loop, and `Exit Sub` / `Exit Function`
return early. There is **no `Continue`** — `Continue For` and friends are not part of the
language (no keyword, no AST node, no IR node); restructure with an `If` instead.

## Functions and subroutines

```vb
Sub Greet(name As String, Optional greeting As String = "Hello")
    Console.WriteLine(greeting & ", " & name)
End Sub

Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Sub Swap(ByRef a As Integer, ByRef b As Integer)
    Dim t = a
    a = b
    b = t
End Sub
```

Forward references are allowed — a `Sub` may call another declared later in the file.

> [note] **There is no overloading.** A second `Sub Show` is refused with *"Subroutine
> 'Show' is already defined in this scope"* — one name, one signature. An omitted
> `Optional` argument is now filled by the compiler at the call site on every backend
> (C#, C++, JavaScript and MSIL), including a constructor's; before that it worked only
> on C#, where `csc` happened to fill the signature default.

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
A `Function` or `Sub` with no modifier is **`Public`**; a variable, constant or nested type
with no modifier is **`Private`**. Access is enforced by the front end on every backend
<span class="pill ok">since #58</span> — calling a `Private` procedure from another module
is refused before codegen, not left for one backend's compiler to notice.
Inheritance uses `Inherits`; `MustInherit` marks an abstract class, `MustOverride` an
abstract member and `Overridable` / `Overrides` a virtual one. `MyBase` reaches the base
implementation and `Me` the current instance. There is **no `NotInheritable`** — the
keyword is not implemented, so a class cannot be sealed.

`Shared` members (fields, `Const`s, methods and property accessors) work on all four
backends <span class="pill ok">since 2026-09</span>, qualified (`Box.K`, `Box.Read()`) as
well as bare. A `Class` may declare a `Const`, and instance field initializers
(`Private _health As Integer = 100`) run — all three were broken or unparseable before.

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

A Module's procedures, `Dim`s and `Const`s are reachable both **unqualified** (`Twice(4)`)
and **qualified** (`Helpers.Twice(4)`, `Helpers.Value`), on C#, C++, JavaScript and MSIL
alike, single-file and across `Import`ed files in either compile order.

> [note] This is new <span class="pill ok">#56 / #57</span>. Before it, `Helpers.Value`
> failed on three backends and resolved on none of them, the bare form worked "by two
> coincidences", and two modules sharing a procedure or variable name silently lost one.
> Access is enforced now too, so a `Private` module member is refused from outside.

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

`Select Case` carries a wide pattern surface: constants, `Or` alternatives, comma lists,
ranges, `Is` comparisons, type patterns with or without a binding, `Nothing`, tuple
deconstruction, and a `When` guard on any of them.

> [trap] A `Case` body goes on its own line. `Case 0 : Console.WriteLine("Zero")` does not
> parse — `:` is lexed but never consumed as a statement separator.

```vb
' When guards
Select Case x
    Case n When n > 10
        Console.WriteLine("Greater than 10")
    Case n When n > 0
        Console.WriteLine("Positive")
    Case 0
        Console.WriteLine("Zero")
    Case Else
        Console.WriteLine("Negative")
End Select

' Or patterns — and the comma list, which means the same thing
Select Case day
    Case 1 Or 7
        Console.WriteLine("Weekend")
    Case 2, 3, 4, 5, 6
        Console.WriteLine("Weekday")
End Select

' Range patterns
Select Case score
    Case 90 To 100
        grade = "A"
    Case 80 To 89
        grade = "B"
End Select

' Comparison patterns
Select Case value
    Case Is > 10
        Console.WriteLine("big")
    Case Is < 0
        Console.WriteLine("negative")
End Select

' Type patterns — with a binding, or bare after Is
Select Case item
    Case s As String
        Console.WriteLine("String: " & s)
    Case Is Integer
        Console.WriteLine("an Integer")
End Select

' Nothing pattern (Case Nothing or Case Is Nothing)
Select Case obj
    Case Nothing
        Console.WriteLine("null")
End Select
```

## Arrays and collections

```vb
Dim nums(9) As Integer               ' fixed, 9 elements — a COUNT, not a VB upper bound
Dim primes = {2, 3, 5, 7, 11}        ' initialised

Dim list As New List(Of String)()
list.Add("x")

Dim map As New Dictionary(Of String, Integer)()
map("alice") = 10

Dim seen As New HashSet(Of Integer)()
seen.Add(1)                          ' True when newly added
```

> [trap] An array's declared dimension is an **element count**, not VB's upper bound:
> `Dim nums(9)` gives nine slots, indexed 0-8. This diverges from real VB deliberately and
> consistently across C#, C++, JavaScript and MSIL. The size must also fold at compile time
> (a literal, a `Const`, or arithmetic over them) — `ReDim` is not implemented, so a
> run-time size is refused rather than lowered to something wrong.

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

> [trap] **LINQ query syntax parses but does not run.** `IRBuilder.Visit(LinqQueryExpressionNode)`
> lowers `From … Where … Order By … Select` to bare `Where` / `Select` / `OrderBy` calls —
> with the clause expression evaluated eagerly rather than passed as a lambda — and no
> backend, StdLib shim or runtime defines those functions, so the emitted program fails to
> compile. **Method syntax** is the working form: on C# the emitted `.Where(` / `.Select(` /
> `.OrderBy(` pulls in a real `using System.Linq;`, and on JavaScript it lowers to eager
> array methods (`filter` / `map` / a copying `sort`), with the throw-on-empty operators
> (`First`, `Min`, `Max`, `Average`, `Last`) refused as `BL7008`. Query syntax is
> <span class="pill crit">front-end only</span>.

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

> [trap] On the C++ backend, a `Return` inside a `Try` bypasses its `Finally`. This is a
> known limitation, not a bug to be surprised by.

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
```

Three more directives matter and are easy to confuse:

- `#Include "file.bas"` textually splices a **BasicLang** source file.
- `#CppInclude <header>` emits a real C++ `#include` — C++ backend only.
- `#JsImport "./mod.js"` emits a real ES `import` — JavaScript backend only.

> [trap] The directive set `Preprocessor.cs` implements is exactly `#Include`, `#Define`,
> `#IfDef`, `#IfNDef`, `#Else`, `#EndIf`, `#CppInclude` and `#JsImport`. Conditional
> compilation is <span class="pill warn">symbol presence only</span> — there is no
> `#If <expression> Then`. `#If`, `#ElseIf`, `#Undef`, `#Const`, `#Region` and
> `#End Region` get a token from the lexer and nothing else: the parser has no arm for
> them, so they surface as an "Unexpected token" parse error. (`#ElseIf` is worse than
> unsupported — the preprocessor's `#Else` test is a prefix match, so it is silently
> treated as a plain `#Else` and its condition is discarded.)

## Multi-file projects and .NET interop

```vb
Import "Utilities.bas"       ' another BasicLang file
Using System.Collections.Generic   ' a .NET namespace
```

Inline foreign code blocks let you drop into the target language where the abstraction
runs out. See [.NET interop](#/net-interop) and [C++ interop](#/cpp-interop) for the
rules and the capability checks that police them.
