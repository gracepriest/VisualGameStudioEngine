# BasicLang Language Guide

BasicLang is a modern programming language with VB-like syntax, designed for game development and general-purpose programming.

## Table of Contents

1. [Variables and Types](#variables-and-types)
2. [Operators](#operators)
3. [Control Flow](#control-flow)
4. [Functions and Subroutines](#functions-and-subroutines)
5. [Object-Oriented Programming](#object-oriented-programming)
6. [Arrays and Collections](#arrays-and-collections)
7. [LINQ](#linq)
8. [Async/Await](#asyncawait)
9. [Error Handling](#error-handling)

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

' Constants
Const PI = 3.14159
Const MAX_SIZE As Integer = 100
```

### Built-in Types

| Type | Description | Example |
|------|-------------|---------|
| Integer | 32-bit integer | `42` |
| Long | 64-bit integer | `42L` |
| Single | 32-bit float | `3.14f` |
| Double | 64-bit float | `3.14` |
| String | Text | `"Hello"` |
| Boolean | True/False | `True` |
| Char | Single character | `"A"c` |
| Byte | 8-bit unsigned | `255` |

## Operators

### Arithmetic
```vb
Dim a = 10 + 5    ' Addition: 15
Dim b = 10 - 5    ' Subtraction: 5
Dim c = 10 * 5    ' Multiplication: 50
Dim d = 10 / 3    ' Division: 3.333...
Dim e = 10 \ 3    ' Integer division: 3
Dim f = 10 Mod 3  ' Modulo: 1
Dim g = 2 ^ 3     ' Power: 8
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
a And b    ' Logical AND
a Or b     ' Logical OR
Not a      ' Logical NOT
a AndAlso b ' Short-circuit AND
a OrElse b  ' Short-circuit OR
```

### String
```vb
Dim s = "Hello" & " " & "World"  ' Concatenation
Dim t = $"Value is {x}"          ' Interpolation
```

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
' Standard For loop
For i = 1 To 10
    PrintLine(i)
Next

' With Step
For i = 10 To 1 Step -1
    PrintLine(i)
Next

' For Each
For Each item In collection
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

## Functions and Subroutines

### Subroutine (No Return Value)
```vb
Sub Greet(name As String)
    PrintLine($"Hello, {name}!")
End Sub

' Call
Greet("World")
```

### Function (Returns Value)
```vb
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

' Call
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

### ByVal and ByRef
```vb
Sub Increment(ByRef value As Integer)
    value += 1
End Sub

Dim x = 5
Increment(x)  ' x is now 6
```

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

    ' Properties
    Public Property Name As String
        Get
            Return _name
        End Get
        Set
            _name = value
        End Set
    End Property

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
        MyBase.New(name)
        _damage = damage
    End Sub

    Public Sub Attack(target As Player)
        target.TakeDamage(_damage)
    End Sub
End Class
```

### Interfaces
```vb
Interface IDrawable
    Sub Draw()
    Property X As Integer
    Property Y As Integer
End Interface

Class Sprite
    Implements IDrawable

    Public Property X As Integer
    Public Property Y As Integer

    Public Sub Draw()
        ' Drawing logic
    End Sub
End Class
```

### Abstract Classes
```vb
MustInherit Class Shape
    Public MustOverride Function Area() As Double
    Public MustOverride Function Perimeter() As Double
End Class

Class Circle
    Inherits Shape

    Private _radius As Double

    Public Overrides Function Area() As Double
        Return 3.14159 * _radius * _radius
    End Function

    Public Overrides Function Perimeter() As Double
        Return 2 * 3.14159 * _radius
    End Function
End Class
```

## Arrays and Collections

### Arrays
```vb
' Declaration
Dim numbers(10) As Integer
Dim names() As String = {"Alice", "Bob", "Charlie"}

' Multi-dimensional
Dim matrix(3, 3) As Integer

' Access
numbers(0) = 42
Dim first = names(0)
```

### Dynamic Arrays
```vb
Dim list As New List(Of Integer)
list.Add(1)
list.Add(2)
list.Add(3)

For Each item In list
    PrintLine(item)
Next
```

## LINQ

BasicLang supports LINQ queries for data manipulation:

```vb
Dim numbers = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10}

' Filter
Dim evens = From n In numbers
            Where n Mod 2 = 0
            Select n

' Transform
Dim doubled = From n In numbers
              Select n * 2

' Order
Dim sorted = From n In numbers
             Order By n Descending
             Select n

' Aggregate
Dim sum = Aggregate n In numbers Into Sum(n)

' Complex query
Dim result = From n In numbers
             Where n > 3
             Order By n
             Select New With {.Value = n, .Square = n * n}
```

## Async/Await

```vb
Async Function LoadDataAsync() As Task(Of String)
    Dim data = Await FetchFromServerAsync()
    Return data
End Function

Async Sub ProcessAsync()
    Dim result = Await LoadDataAsync()
    PrintLine(result)
End Sub
```

## Error Handling

```vb
Try
    Dim result = riskyOperation()
Catch ex As InvalidOperationException
    PrintLine($"Invalid operation: {ex.Message}")
Catch ex As Exception
    PrintLine($"Error: {ex.Message}")
Finally
    ' Cleanup code
End Try

' Throwing exceptions
Sub ValidateAge(age As Integer)
    If age < 0 Then
        Throw New ArgumentException("Age cannot be negative")
    End If
End Sub
```

## Modules and Namespaces

### Modules
```vb
Module MathUtils
    Public Function Square(x As Double) As Double
        Return x * x
    End Function

    Public Function Cube(x As Double) As Double
        Return x * x * x
    End Function
End Module

' Usage (no instance needed)
Dim sq = MathUtils.Square(5)
```

### Namespaces
```vb
Namespace MyGame.Entities
    Class Player
        ' ...
    End Class
End Namespace

' Using
Using MyGame.Entities
Dim p = New Player()
```

## Preprocessor Directives

```vb
#Define DEBUG

#If DEBUG Then
    PrintLine("Debug mode")
#Else
    PrintLine("Release mode")
#End If

#Region "Initialization"
' Code here
#End Region
```

## Next Steps

- [IDE User Guide](ide-guide.md) - Using the development environment
- [Game Engine Guide](engine-guide.md) - Building games
- [API Reference](../api/index.md) - Complete API documentation
