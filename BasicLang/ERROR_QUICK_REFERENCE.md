# BasicLang Error Messages - Quick Reference Guide

## Quick Usage Examples

### Lexer Errors

```csharp
// Unterminated string
throw new LexerException(
    ErrorCode.BL1001_UnterminatedString,
    ErrorFormatter.FormatLexerError(
        ErrorCode.BL1001_UnterminatedString,
        "Unterminated string literal - missing closing quote",
        startLine, startColumn, _source,
        "Add a closing quote (\") at the end of the string"),
    startLine, startColumn, _source);

// Invalid character
var error = ErrorFormatter.FormatLexerError(
    ErrorCode.BL1003_InvalidCharacter,
    $"Invalid character '{c}' (Unicode: U+{((int)c):X4})",
    startLine, startColumn, _source,
    "Remove or replace this character with a valid symbol");
_errors.Add(error);

// Number overflow
var error = ErrorFormatter.FormatLexerError(
    ErrorCode.BL1004_InvalidNumberFormat,
    $"Number '{sb}' is too large for Integer type",
    startLine, startColumn, _source,
    $"Use 'L' suffix for Long type (e.g., {sb}L). Max Integer: {int.MaxValue}");
_errors.Add(error);
```

### Parser Errors

```csharp
// Missing token
var error = ErrorFormatter.FormatParserError(
    ErrorCode.BL2002_MissingToken,
    message,
    current,
    _source,
    GetExpectedTokenDescription(type),
    GetSuggestionForMissingToken(type, current));
throw new ParseException(error, current);

// Block mismatch
var error = ErrorFormatter.FormatBlockMismatchError(
    openedBlock: "Function",
    closedBlock: "End Sub",
    openLine: 10,
    closeLine: current.Line,
    closeColumn: current.Column,
    _source);
throw new ParseException(error, current);

// Common typo
var correction = ErrorFormatter.SuggestKeywordCorrection(token.Lexeme);
if (correction != null)
{
    var error = ErrorFormatter.FormatParserError(
        ErrorCode.BL2003_MissingKeyword,
        $"Unrecognized keyword '{token.Lexeme}'",
        token, _source, correction,
        $"Did you mean '{correction}'?");
    throw new ParseException(error, token);
}
```

### Semantic Errors

```csharp
// Undefined symbol with suggestions
var allSymbols = GetAllSymbolsInScope(_currentScope);
var error = ErrorFormatter.FormatUndefinedSymbolError(
    node.Name, node.Line, node.Column,
    _source, allSymbols);
_errors.Add(new SemanticError(error, node.Line, node.Column));

// Type mismatch
var error = ErrorFormatter.FormatTypeMismatchError(
    expectedType: targetType.Name,
    actualType: valueType.Name,
    line: node.Line,
    column: node.Column,
    sourceCode: _source,
    context: "Assignment");
_errors.Add(new SemanticError(error, node.Line, node.Column));

// Argument count error
var error = ErrorFormatter.FormatArgumentCountError(
    functionName: calleeSymbol.Name,
    expected: totalParams,
    actual: node.Arguments.Count,
    line: node.Line,
    column: node.Column,
    sourceCode: _source,
    hasOptionalParams: hasOptional,
    minRequired: requiredCount);
_errors.Add(new SemanticError(error, node.Line, node.Column));
```

## Error Code Quick Reference

| Code | Category | Description | Suggestion Pattern |
|------|----------|-------------|-------------------|
| BL1001 | Lexer | Unterminated string | Add closing quote |
| BL1002 | Lexer | Unterminated interpolated string | Add closing quote |
| BL1003 | Lexer | Invalid character | Remove/replace character |
| BL1004 | Lexer | Invalid number format | Use type suffix or reduce value |
| BL1006 | Lexer | Unrecognized directive | List valid directives |
| BL2001 | Parser | Unexpected token | Expected declaration |
| BL2002 | Parser | Missing token | Add required token |
| BL2003 | Parser | Missing keyword | Suggest correction |
| BL2004 | Parser | Mismatched block | Show opening location |
| BL3001 | Semantic | Type mismatch | Suggest conversion function |
| BL3002 | Semantic | Undefined symbol | Suggest similar names |
| BL3004 | Semantic | Wrong argument count | Show expected vs actual |
| BL3005 | Semantic | Invalid assignment | Explain constraint |
| BL3006 | Semantic | Invalid operation | Suggest valid types |

## Common Typo Corrections

```csharp
var typos = new Dictionary<string, string>
{
    { "funciton", "Function" },
    { "fucntion", "Function" },
    { "elsif", "ElseIf" },
    { "endif", "End If" },
    { "endsub", "End Sub" },
    { "dimm", "Dim" },
    { "integr", "Integer" },
    { "strig", "String" },
    { "boolen", "Boolean" },
    { "returnn", "Return" }
};
```

## Helper Functions

### Get Similar Names
```csharp
private List<string> GetAllSymbolsInScope(Scope scope)
{
    var symbols = new List<string>();
    var currentScope = scope;
    while (currentScope != null)
    {
        symbols.AddRange(currentScope.Symbols.Keys);
        currentScope = currentScope.Parent;
    }
    return symbols.Distinct().ToList();
}
```

### Track Block Context
```csharp
// In Parser class
private Stack<(string blockType, int line)> _blockStack;

// When opening block
_blockStack.Push(("Function", token.Line));

// When closing block
if (!Check(TokenType.EndFunction))
{
    var (blockType, openLine) = _blockStack.Peek();
    // Generate block mismatch error
}
_blockStack.Pop();
```

### Expected Token Descriptions
```csharp
private string GetExpectedTokenDescription(TokenType type)
{
    switch (type)
    {
        case TokenType.EndIf: return "'End If'";
        case TokenType.EndFunction: return "'End Function'";
        case TokenType.Then: return "'Then'";
        case TokenType.Identifier: return "an identifier";
        default: return $"'{type}'";
    }
}
```

## Integration Pattern

```csharp
// 1. Add fields to your class
private readonly string _source;
private readonly List<string> _errors;

// 2. Initialize in constructor
public YourClass(string sourceCode)
{
    _source = sourceCode;
    _errors = new List<string>();
}

// 3. Use when encountering errors
if (errorCondition)
{
    var error = ErrorFormatter.FormatXxxError(
        errorCode,
        message,
        line, column,
        _source,
        suggestion);

    // Either add to list
    _errors.Add(error);

    // Or throw immediately
    throw new LexerException(errorCode, error, line, column, _source);
}
```

## Testing Pattern

```csharp
[Test]
public void TestUnterminatedString()
{
    var source = "Dim s As String\ns = \"hello";
    var lexer = new Lexer(source);

    Assert.Throws<LexerException>(() => lexer.Tokenize());
    Assert.That(lexer.Errors.Count, Is.GreaterThan(0));
    Assert.That(lexer.Errors[0], Contains.Substring("BL1001"));
    Assert.That(lexer.Errors[0], Contains.Substring("Unterminated"));
}
```

## Best Practices

### DO
- Always provide source code when available for context
- Give specific, actionable suggestions
- Use appropriate error codes
- Include relevant type names in error messages
- Suggest similar names for undefined symbols
- Show where blocks were opened for mismatches

### DON'T
- Don't use generic error messages
- Don't show suggestions that won't help
- Don't forget to include line/column information
- Don't make assumptions about what the user meant
- Don't provide too many suggestions (max 3)
- Don't skip error codes

## Message Templates

### Lexer Template
```
Error {code}: {description}
  at line {line}, column {column}

{code snippet}

Suggestion: {actionable suggestion}
```

### Parser Template
```
Error {code}: {description}
  at line {line}, column {column}

{code snippet}

Expected: {expected token/keyword}
[Found: {actual token}]

Suggestion: {actionable suggestion}
```

### Semantic Template
```
Error {code}: {description}
  at line {line}, column {column}

{code snippet}

Suggestion: {actionable suggestion with specifics}
```

## Example Output Formats

### Good Error Message
```
Error BL3002: Undefined symbol 'userName'
  at line 15, column 5

  0014 | Dim username As String
> 0015 |     userName = "John"
       |     ^^^^^^^^
  0016 | End Sub

Suggestion: Did you mean: 'username'?
```

### Bad Error Message (Avoid)
```
Error: Variable not found
Line 15
```

## Severity Levels

```csharp
public enum ErrorSeverity
{
    Warning,  // Code may work but has issues
    Error     // Code will not compile
}
```

Use warnings for:
- Unused variables
- Type comparisons that may be unintended
- Deprecated features
- Style issues

Use errors for:
- Syntax errors
- Type mismatches
- Undefined symbols
- Invalid operations

## Performance Considerations

- Code snippet extraction is O(n) where n = number of lines
- Similar name detection is O(m*k) where m = candidates, k = avg name length
- Limit similar name search to 3 suggestions
- Cache source code lines if checking multiple errors
- Use lazy evaluation for expensive suggestions

## Maintenance

When adding new error codes:
1. Add to ErrorCode enum
2. Update error code reference documentation
3. Add example to demo file
4. Create unit test
5. Update this quick reference
