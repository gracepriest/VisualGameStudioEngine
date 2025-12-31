# BasicLang Compiler Error Message Improvements

This document describes the comprehensive improvements made to error messages throughout the BasicLang compiler.

## Overview

The error messaging system has been enhanced with:

1. **ErrorFormatter Utility Class** - Centralized, consistent error formatting
2. **Error Codes** - Unique identifiers for each error type (BL1xxx, BL2xxx, BL3xxx)
3. **Context-Aware Messages** - Shows source code snippets with error locations
4. **Helpful Suggestions** - Provides actionable suggestions to fix errors
5. **Similar Name Detection** - Suggests similar symbols when names are undefined
6. **Typo Detection** - Recognizes common keyword typos

## Files Created

### 1. ErrorFormatter.cs
**Location**: `C:\Users\melvi\source\repos\BasicLang\BasicLang\ErrorFormatter.cs`

A comprehensive utility class for formatting compiler errors with:

#### Error Codes
- **BL1xxx**: Lexer errors (unterminated strings, invalid characters, invalid number formats)
- **BL2xxx**: Parser errors (unexpected tokens, missing keywords, mismatched blocks)
- **BL3xxx**: Semantic errors (type mismatches, undefined symbols, wrong number of arguments)

#### Key Features
- **Code Snippets**: Shows the problematic line with surrounding context
- **Visual Indicators**: Uses arrows and underlines to highlight errors
- **Levenshtein Distance**: Finds similar symbol names for "did you mean?" suggestions
- **Type Conversion Suggestions**: Recommends conversion functions for type mismatches
- **Common Typo Detection**: Recognizes and corrects common keyword typos

#### Example Usage
```csharp
var error = ErrorFormatter.FormatUndefinedSymbolError(
    "userName",        // Symbol name
    line: 15,          // Line number
    column: 10,        // Column number
    sourceCode,        // Full source code
    similarNames: new[] { "username", "UserName", "user" }
);
```

#### Example Output
```
Error BL3002: Undefined symbol 'userName'
  at line 15, column 10

  0014 | Dim total As Integer
> 0015 |     userName = "John"
       |     ^^^^^^^^
  0016 | End Sub

Suggestion: Did you mean: 'username', 'UserName', 'user'?
```

### 2. Integration Files

Three files provide code snippets for integrating improvements into existing compiler components:

#### BasicLangLexer_Improved.cs
**Location**: `C:\Users\melvi\source\repos\BasicLang\BasicLang\BasicLangLexer_Improved.cs`

Improvements for the lexer:
- Unterminated string detection with helpful messages
- Invalid character reporting with Unicode codes
- Number overflow detection with type suffix suggestions
- Invalid directive recognition

#### Parser_Improved.cs
**Location**: `C:\Users\melvi\source\repos\BasicLang\BasicLang\Parser_Improved.cs`

Improvements for the parser:
- Block mismatch tracking (shows where blocks were opened)
- Missing keyword detection with suggestions
- Common typo detection (e.g., "funciton" -> "Function")
- Expected token descriptions
- Context-aware error messages

#### SemanticAnalyzer_Improved.cs
**Location**: `C:\Users\melvi\source\repos\BasicLang\BasicLang\SemanticAnalyzer_Improved.cs`

Improvements for semantic analysis:
- Type mismatch errors with clear type names
- Undefined symbol errors with similar name suggestions
- Argument count errors with detailed information
- Binary operator errors with type information
- Member access errors with suggestions

## Error Code Reference

### Lexer Errors (BL1xxx)

| Code | Description | Example |
|------|-------------|---------|
| BL1001 | Unterminated string | Missing closing quote in string literal |
| BL1002 | Unterminated interpolated string | Missing closing quote in $"..." string |
| BL1003 | Invalid character | Unexpected character in source code |
| BL1004 | Invalid number format | Number too large for type or malformed |
| BL1005 | Invalid escape sequence | Unknown escape sequence in string |
| BL1006 | Unrecognized directive | Invalid preprocessor directive |

### Parser Errors (BL2xxx)

| Code | Description | Example |
|------|-------------|---------|
| BL2001 | Unexpected token | Token found in invalid context |
| BL2002 | Missing token | Expected token not found |
| BL2003 | Missing keyword | Required keyword is absent |
| BL2004 | Mismatched block | Block ending doesn't match opening |
| BL2005 | Invalid syntax | General syntax error |
| BL2006 | Unexpected end of file | File ended prematurely |

### Semantic Errors (BL3xxx)

| Code | Description | Example |
|------|-------------|---------|
| BL3001 | Type mismatch | Cannot convert between types |
| BL3002 | Undefined symbol | Symbol not found in scope |
| BL3003 | Redefined symbol | Symbol already defined |
| BL3004 | Wrong number of arguments | Incorrect argument count in call |
| BL3005 | Invalid assignment | Cannot assign to target |
| BL3006 | Invalid operation | Operator cannot be applied |
| BL3007 | Cannot convert | Explicit conversion failed |
| BL3008 | Missing return type | Function missing return type |
| BL3009 | Accessibility error | Member not accessible |
| BL3010 | Circular dependency | Circular type dependency |
| BL3011 | Invalid member access | Member doesn't exist |
| BL3012 | Invalid array access | Array indexing error |

## Examples of Improved Error Messages

### 1. Unterminated String (Lexer)

**Before:**
```
Unterminated string at line 5, column 10
```

**After:**
```
Error BL1001: Unterminated string literal - missing closing quote
  at line 5, column 10

  0004 | Function Greet(name As String) As String
> 0005 |     Return "Hello,
       |            ^
  0006 | End Function

Suggestion: Add a closing quote (") at the end of the string
```

### 2. Invalid Number (Lexer)

**Before:**
```
FormatException: Input string was not in correct format
```

**After:**
```
Error BL1004: Number '2147483648' is too large for Integer type
  at line 12, column 15

  0011 | Dim x As Integer
> 0012 |     x = 2147483648
       |         ^^^^^^^^^^
  0013 | End Sub

Suggestion: Use 'L' suffix for Long type (e.g., 2147483648L) or reduce the number. Maximum Integer value is 2147483647
```

### 3. Missing Keyword (Parser)

**Before:**
```
Unexpected token at top level: Identifier
```

**After:**
```
Error BL2003: Unrecognized keyword 'funciton'
  at line 8, column 1

  0007 |
> 0008 | funciton Add(a As Integer, b As Integer) As Integer
       | ^^^^^^^^
  0009 |     Return a + b

Expected: 'Function'
Suggestion: Did you mean 'Function'?
```

### 4. Mismatched Block (Parser)

**Before:**
```
Expected 'End Function' but found EOF
```

**After:**
```
Error BL2004: Block mismatch: 'Function' opened at line 10 but 'End Sub' found
  at line 15, column 1

  0014 |     Return total
> 0015 | End Sub
       | ^^^^^^^
  0016 |

Expected: End Function
Suggestion: Expected 'End Function' to match the 'Function' at line 10
```

### 5. Undefined Symbol (Semantic)

**Before:**
```
Undefined identifier 'usrName'
```

**After:**
```
Error BL3002: Undefined symbol 'usrName'
  at line 22, column 5

  0021 | Dim userName As String = "John"
> 0022 |     usrName = "Jane"
       |     ^^^^^^^
  0023 | End Sub

Suggestion: Did you mean: 'userName'?
```

### 6. Type Mismatch (Semantic)

**Before:**
```
Cannot assign value of type 'String' to variable of type 'Integer'
```

**After:**
```
Error BL3001: Assignment: Type mismatch: cannot convert 'String' to 'Integer'
  at line 18, column 5

  0017 | Dim age As Integer
> 0018 |     age = "25"
       |     ^^^^^^^^^^
  0019 | End Sub

Suggestion: Use Val(...) or CInt(...) to convert from String
```

### 7. Wrong Number of Arguments (Semantic)

**Before:**
```
Function 'Add' expects 2 arguments, but 1 were provided
```

**After:**
```
Error BL3004: Function 'Add' expects 2 arguments, but 1 were provided
  at line 25, column 5

  0024 | Dim result As Integer
> 0025 |     result = Add(5)
       |              ^^^^^^
  0026 | End Sub

Suggestion: Add 1 missing argument(s)
```

### 8. Invalid Operator (Semantic)

**Before:**
```
Arithmetic operator '+' requires numeric operands
```

**After:**
```
Error BL3006: Arithmetic operator '+' requires numeric operands, but found 'String' and 'Integer'
  at line 30, column 10

  0029 | Dim name As String = "Age: "
> 0030 |     Dim result = name + 25
       |                  ^^^^^^^^^
  0031 | End Sub

Suggestion: Ensure both operands are numeric types (Integer, Long, Single, Double)
```

## Integration Steps

### Step 1: Add ErrorFormatter.cs
The `ErrorFormatter.cs` file is ready to use - just include it in your project.

### Step 2: Integrate Lexer Improvements
Apply the changes from `BasicLangLexer_Improved.cs` to `BasicLangLexer.cs`:

1. Add the `_errors` field
2. Initialize it in the constructor
3. Add the `Errors` property
4. Replace error-throwing code with formatted errors
5. Add try-catch blocks for number parsing

### Step 3: Integrate Parser Improvements
Apply the changes from `Parser_Improved.cs` to `Parser.cs`:

1. Add `_source` and `_blockStack` fields
2. Update constructor to accept source code
3. Replace `Consume` method with improved version
4. Add block tracking in Parse methods
5. Update `ParseException` class

### Step 4: Integrate Semantic Analyzer Improvements
Apply the changes from `SemanticAnalyzer_Improved.cs` to `SemanticAnalyzer.cs`:

1. Add `_source` field
2. Update constructor to accept source code
3. Replace `Error` method with improved version
4. Update Visit methods with better error messages
5. Add `GetAllSymbolsInScope` helper method

## Common Typo Corrections

The system automatically detects and suggests corrections for common typos:

| Typo | Correction |
|------|------------|
| funciton, fucntion, funtion | Function |
| elsif, elseif | ElseIf |
| endif | End If |
| endsub | End Sub |
| endfunction | End Function |
| dimm | Dim |
| integr | Integer |
| strig, strng | String |
| boolen, booleen | Boolean |
| privat | Private |
| publc | Public |
| returnn, retrun | Return |

## Benefits

1. **Faster Debugging**: Developers can quickly identify and fix errors
2. **Better Learning Curve**: New users get helpful guidance
3. **Reduced Support**: Clear messages reduce "what does this mean?" questions
4. **Professional Quality**: Error messages match industry standards
5. **IDE Integration**: Formatted messages work well with IDE error display

## Future Enhancements

Potential areas for further improvement:

1. **Multi-language Support**: Localize error messages
2. **Error Recovery**: Continue parsing after errors to find more issues
3. **Fix-it Hints**: Automatic code fixes for common errors
4. **Warning Levels**: Configurable warning verbosity
5. **Error Statistics**: Track most common errors for UX improvements
6. **IDE Integration**: Better integration with LSP server for real-time errors

## Testing

To test the improved error messages:

1. Create test files with various errors
2. Run the compiler and verify error output
3. Check that suggestions are helpful and accurate
4. Ensure code snippets are displayed correctly
5. Verify similar name detection works

Example test cases are included in:
- `Tests/LexerErrorTests.bl`
- `Tests/ParserErrorTests.bl`
- `Tests/SemanticErrorTests.bl`

## License

These improvements maintain the same license as the BasicLang compiler project.
