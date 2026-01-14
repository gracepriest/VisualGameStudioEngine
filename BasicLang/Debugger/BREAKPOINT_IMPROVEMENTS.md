# BasicLang Debugger Breakpoint Improvements

This document describes the enhanced breakpoint support added to the BasicLang debugger.

## Overview

The debugger now supports advanced breakpoint features that go beyond simple line breakpoints:

1. **Conditional Breakpoints** - Break only when a condition is true
2. **Hit Count Breakpoints** - Break after N hits
3. **Logpoints** - Log a message instead of breaking
4. **Breakpoint Validation** - Warn if breakpoint is on non-executable line
5. **Column Breakpoints** - Support breakpoints at specific columns
6. **Function Breakpoints** - Break when entering a named function

## New Files

### Breakpoint.cs
Contains the core breakpoint models and management:

- `Breakpoint` - Enhanced breakpoint class with support for all breakpoint types
- `BreakpointType` - Enum defining Line, Conditional, HitCount, Logpoint, and Function types
- `HitCountCondition` - Enum for hit count comparison operators (Equals, GreaterThan, etc.)
- `BreakpointManager` - Manages collections of breakpoints efficiently

### BreakpointValidator.cs
Validates breakpoints against source code:

- `BreakpointValidator` - Analyzes source code to determine executable lines
- `BreakpointValidationResult` - Contains validation results with adjusted line numbers
- Automatically moves breakpoints to nearest executable line when possible

## Updated Files

### DebuggableInterpreter.cs
Enhanced interpreter with advanced breakpoint support:

**New Features:**
- Integration with `BreakpointManager` for efficient breakpoint lookup
- Support for conditional expression evaluation
- Hit count tracking per breakpoint
- Logpoint message formatting with variable substitution
- Function breakpoint triggering on function entry
- Column-aware breakpoint checking

**New Methods:**
- `AddBreakpoint(Breakpoint)` - Add a breakpoint with full feature support
- `RemoveBreakpoint(int)` - Remove breakpoint by ID
- `GetAllBreakpoints()` - Get all registered breakpoints
- `ClearAllBreakpoints()` - Clear all breakpoints
- `SetCurrentFile(string)` - Set current source file for debugging

**New Events:**
- `LogpointHit` - Fired when a logpoint is triggered

### DebugSession.cs
Updated DAP (Debug Adapter Protocol) session handler:

**New Capabilities:**
- Advertises support for all new breakpoint types in initialization
- Handles `setFunctionBreakpoints` command
- Parses conditional expressions from DAP protocol
- Parses hit count conditions (==, >, >=, <, <=, %)
- Parses logpoint messages with expression interpolation
- Supports column-based breakpoints

**New Methods:**
- `HandleSetFunctionBreakpoints(DAPMessage)` - Handle function breakpoint requests
- `ParseHitCondition(string, Breakpoint)` - Parse hit count conditions from DAP
- `OnLogpointHit(object, LogpointEventArgs)` - Handle logpoint events

## Usage Examples

### 1. Conditional Breakpoints

Break only when a variable meets a condition:

```json
{
  "line": 42,
  "condition": "count > 10"
}
```

The debugger will:
- Evaluate the condition at runtime
- Only break if the condition evaluates to true
- Continue execution if the condition is false or fails to evaluate

### 2. Hit Count Breakpoints

Break on the 5th time a line is hit:

```json
{
  "line": 42,
  "hitCondition": "== 5"
}
```

Supported operators:
- `== N` - Break when hit count equals N
- `> N` - Break when hit count is greater than N
- `>= N` - Break when hit count is greater than or equal to N
- `< N` - Break when hit count is less than N
- `<= N` - Break when hit count is less than or equal to N
- `% N` - Break every Nth hit (e.g., `% 3` breaks on hits 3, 6, 9, ...)

### 3. Logpoints

Log a message without stopping execution:

```json
{
  "line": 42,
  "logMessage": "Variable x = {x}, y = {y}"
}
```

The debugger will:
- Evaluate expressions inside `{...}`
- Replace them with actual values
- Send the formatted message to the debug console
- Continue execution without breaking

### 4. Function Breakpoints

Break when entering a function:

```json
{
  "name": "CalculateTotal"
}
```

Can be combined with conditions:

```json
{
  "name": "ProcessItem",
  "condition": "itemCount > 100",
  "hitCondition": "> 5"
}
```

### 5. Column Breakpoints

Break at a specific column on a line:

```json
{
  "line": 42,
  "column": 15
}
```

Useful for lines with multiple statements or lambda expressions.

## Implementation Details

### Breakpoint Evaluation Flow

1. **Check Location** - Verify line and optional column match
2. **Increment Hit Count** - Track how many times breakpoint was hit
3. **Check Hit Count Condition** - If specified, verify hit count meets criteria
4. **Evaluate Condition** - If conditional, evaluate the expression
5. **Handle Logpoints** - If logpoint, format and log message
6. **Trigger Break** - If all checks pass, pause execution

### Condition Evaluation

Conditions are evaluated using the interpreter's `EvaluateExpression` method:
- Supports variable lookups from current scope
- Supports local and global variables
- Returns null for undefined variables
- Failures are silently ignored (breakpoint doesn't trigger)

### Logpoint Message Formatting

Logpoint messages support variable interpolation:
- Expressions in `{...}` are evaluated
- Results are converted to strings
- Failed evaluations leave the expression unchanged
- Example: `"Counter: {i}, Total: {sum}"` → `"Counter: 5, Total: 42"`

### Function Breakpoints

Function breakpoints are checked when:
- A function is about to execute
- Before the function's call frame is pushed
- Can include conditional and hit count logic

### Breakpoint Validation

The `BreakpointValidator` analyzes source code to:
- Identify executable lines (statements, expressions)
- Track function declarations and their line numbers
- Suggest nearest executable line if breakpoint is on non-executable line
- Provide helpful error messages

Non-executable lines include:
- Blank lines
- Comments
- Variable declarations (Dim statements)
- Class/property declarations
- End statements (End If, End Function, etc.)

## Debug Adapter Protocol Support

The debugger now advertises these capabilities:

```json
{
  "supportsFunctionBreakpoints": true,
  "supportsConditionalBreakpoints": true,
  "supportsHitConditionalBreakpoints": true,
  "supportsLogPoints": true
}
```

This enables IDEs and editors to show appropriate UI for these features.

## Performance Considerations

- Breakpoints are indexed by file path and line for O(1) lookup
- Function breakpoints use a separate hash map for fast lookup
- Hit count tracking has minimal overhead (single integer increment)
- Conditional evaluation only occurs when breakpoint location is hit
- Logpoint formatting is lazy (only done when triggered)

## New in Latest Version

### Data Breakpoints (Implemented)

Data breakpoints trigger when a variable's value changes:

```csharp
// Add a data breakpoint
var bp = new Breakpoint
{
    Type = BreakpointType.Data,
    VariableName = "counter",
    DataAccessType = DataBreakpointAccessType.Write  // or Read, ReadWrite
};
```

Properties:
- `VariableName` - The variable to watch
- `DataAccessType` - When to break: `Write`, `Read`, or `ReadWrite`
- `PreviousValue` - Tracks the previous value for change detection

### Exception Breakpoints (Implemented)

Exception breakpoints trigger when exceptions occur:

```csharp
// Break on all exceptions
var bp = new Breakpoint
{
    Type = BreakpointType.Exception,
    ExceptionType = "*",  // or specific type like "DivisionByZeroException"
    ExceptionMode = ExceptionBreakMode.Always  // or Unhandled, UserUnhandled
};
```

Properties:
- `ExceptionType` - The exception type to catch ("*" for all)
- `ExceptionMode` - `Never`, `Always`, `Unhandled`, or `UserUnhandled`

### New Event Args

- `DataBreakpointEventArgs` - Contains `VariableName`, `OldValue`, `NewValue`, `AccessType`
- `ExceptionBreakpointEventArgs` - Contains `ExceptionType`, `ExceptionMessage`, `StackTrace`, `IsHandled`

## Future Enhancements

Potential improvements:
1. ~~Data breakpoints (break when variable changes)~~ ✓ Implemented
2. ~~Exception breakpoints (break on throw/catch)~~ ✓ Implemented
3. Breakpoint groups and bulk enable/disable
4. Breakpoint import/export
5. Tracepoints (like logpoints but with full stack trace)
6. Advanced condition syntax (e.g., regex matching)

## Testing

To test the new breakpoint features:

1. **Conditional Breakpoint:**
   ```basic
   For i = 1 To 100
       Sum = Sum + i  ' Set condition: i > 50
   Next
   ```

2. **Hit Count Breakpoint:**
   ```basic
   For i = 1 To 10
       PrintLine(i)  ' Set hit condition: == 5
   Next
   ```

3. **Logpoint:**
   ```basic
   For i = 1 To 5
       x = i * 2  ' Set log message: "Iteration {i}, x = {x}"
   Next
   ```

4. **Function Breakpoint:**
   ```basic
   Function Calculate(n As Integer) As Integer
       Return n * 2
   End Function
   ' Set function breakpoint on "Calculate"
   ```

## Backward Compatibility

The changes maintain backward compatibility:
- Old `SetBreakpoint(file, line)` method still works
- Basic line breakpoints function identically
- DAP clients that don't use new features are unaffected
- Graceful fallback when validation isn't available
