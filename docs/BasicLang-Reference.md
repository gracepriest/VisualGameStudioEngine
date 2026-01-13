# BasicLang Language Reference

BasicLang is a modern BASIC-inspired programming language designed for game development.

## Data Types

| Type | Description |
|------|-------------|
| Integer | 32-bit signed integer |
| Double | 64-bit floating point |
| String | Text string |
| Boolean | True/False |
| Object | Reference type |

## Variable Declaration

```basic
Dim x As Integer
Dim name As String = "Player"
Dim score As Integer = 100
Const PI As Double = 3.14159
```

## Control Structures

### If Statement
```basic
If condition Then
    ' code
ElseIf otherCondition Then
    ' code
Else
    ' code
End If
```

### For Loop
```basic
For i = 0 To 10
    Print(i)
Next

For i = 10 To 0 Step -1
    Print(i)
Next
```

### While Loop
```basic
While condition
    ' code
Wend
```

### Select Case
```basic
Select Case value
    Case 1
        ' code
    Case 2, 3
        ' code
    Case Else
        ' code
End Select
```

## Functions and Subroutines

```basic
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function

Sub PrintMessage(msg As String)
    Print(msg)
End Sub
```

## Classes

```basic
Class Player
    Private _name As String
    Private _health As Integer

    Public Sub New(name As String)
        _name = name
        _health = 100
    End Sub

    Public Property Name As String
        Get
            Return _name
        End Get
        Set(value As String)
            _name = value
        End Set
    End Property

    Public Sub TakeDamage(amount As Integer)
        _health = _health - amount
        If _health < 0 Then
            _health = 0
        End If
    End Sub
End Class
```

## Arrays

```basic
Dim numbers(10) As Integer
Dim matrix(3, 3) As Double

numbers(0) = 42
matrix(1, 2) = 3.14
```

## Collections

```basic
Dim list As New List(Of Integer)
list.Add(1)
list.Add(2)

Dim dict As New Dictionary(Of String, Integer)
dict("key") = 100
```

## Error Handling

```basic
Try
    ' risky code
Catch ex As Exception
    Print(ex.Message)
Finally
    ' cleanup
End Try
```

## Project Files (.blproj)

```xml
<Project>
  <Name>MyGame</Name>
  <OutputType>Executable</OutputType>
  <RootNamespace>MyGame</RootNamespace>
  <Files>
    <File>Main.bl</File>
    <File>Player.bl</File>
  </Files>
  <References>
    <Reference>System</Reference>
  </References>
</Project>
```

## Compiler Commands

```bash
# Compile to C#
BasicLang.exe compile Main.bl --target=csharp

# Build project
BasicLang.exe build game.blproj

# Run directly
BasicLang.exe run Main.bl

# Start LSP server
BasicLang.exe lsp --stdio
```

## Built-in Functions

| Function | Description |
|----------|-------------|
| Print(text) | Output to console |
| Input() | Read from console |
| Len(str) | String length |
| Mid(str, start, len) | Substring |
| Val(str) | Parse number |
| Str(num) | Number to string |
| Rnd() | Random number 0-1 |
| Int(num) | Truncate to integer |
| Abs(num) | Absolute value |
| Sqr(num) | Square root |
