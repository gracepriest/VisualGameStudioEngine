# Debugging Guide

This guide covers advanced debugging techniques in Visual Game Studio.

## Breakpoint Types

### Line Breakpoints

Basic breakpoints that pause execution at a specific line:

1. Click in the margin next to the line number
2. Or place cursor and press `F9`
3. Red circle indicates active breakpoint

### Conditional Breakpoints

Break only when a condition is true:

1. Right-click breakpoint > Conditions
2. Enter condition expression (e.g., `count > 10`)
3. Yellow icon indicates conditional breakpoint

```vb
' Breaks only when i equals 50
For i = 0 To 100
    ProcessItem(i)  ' <-- Conditional breakpoint: i = 50
Next
```

### Hit Count Breakpoints

Break after a specific number of hits:

1. Right-click breakpoint > Hit Count
2. Select condition type:
   - `=` Break when hit count equals N
   - `>=` Break when hit count >= N
   - `%` Break every N hits (modulo)

### Logpoints (Tracepoints)

Print messages without stopping:

1. Right-click breakpoint > Actions
2. Enter log message with variables: `Value is {variableName}`
3. Diamond icon indicates logpoint

```vb
' Logs "Processing item: 5" without stopping
For i = 0 To 100
    ProcessItem(i)  ' <-- Logpoint: "Processing item: {i}"
Next
```

### Function Breakpoints

Break when entering a specific function:

1. Debug > New Breakpoint > Function Breakpoint
2. Enter function name (e.g., `Player.TakeDamage`)
3. Optionally add condition

## Debug Windows

### Locals Window

Shows all local variables in current scope:

```
Name          Value       Type
─────────────────────────────────
player        {Player}    Player
score         150         Integer
isGameOver    False       Boolean
```

- Expand objects to see properties
- Values update at each step

### Watch Window

Monitor specific expressions:

1. Add expressions manually
2. Right-click variable > Add Watch
3. Expressions can include:
   - Variable names: `score`
   - Properties: `player.Health`
   - Calculations: `score * 10`
   - Method calls: `items.Count`

### Call Stack Window

Shows execution path:

```
ProcessDamage() at Game.bas:45
TakeDamage() at Player.bas:23
HandleCollision() at Physics.bas:67
Update() at Game.bas:12
Main() at Program.bas:5
```

- Double-click to navigate to frame
- Shows parameter values

### Immediate Window

Execute code during debugging:

```
> ?player.Health
75
> player.TakeDamage(10)
> ?player.Health
65
> score = 1000
> ?score
1000
```

Commands:
- `?expression` - Evaluate and print
- `variable = value` - Set value
- `clear` - Clear window
- `help` - Show commands

### Output Window

Shows:
- Build messages
- Debug.Print output
- Exception messages
- Module load information

## Stepping Through Code

### Step Over (F10)

Execute current line, don't enter functions:

```vb
Dim result = Calculate(x, y)  ' Executes entire function
PrintLine(result)             ' <-- Next stop
```

### Step Into (F11)

Enter function calls:

```vb
Dim result = Calculate(x, y)  ' Enters Calculate function
```

### Step Out (Shift+F11)

Continue until returning from current function:

```vb
Function Calculate(x, y)
    Dim temp = x + y
    Return temp * 2  ' <-- Step Out stops after this returns
End Function
```

### Run to Cursor

Execute until reaching cursor position:

1. Place cursor on target line
2. Right-click > Run to Cursor
3. Or press `Ctrl+F10`

### Set Next Statement

Jump execution to a different line:

1. Right-click target line
2. Select "Set Next Statement"
3. Or drag yellow arrow

**Warning:** Skipping code can cause unexpected behavior.

## Exception Handling

### Break on Exceptions

Configure when to break:

1. Debug > Windows > Exception Settings
2. Check exception types to break on
3. Options:
   - **Thrown** - Break when exception is thrown
   - **User-unhandled** - Break only if not caught

### Examining Exceptions

When stopped at exception:
- View exception details in popup
- Check `$exception` in Immediate window
- View inner exceptions

## Advanced Techniques

### Data Tips

Hover over variables to see values:

```vb
Dim player = GetPlayer()
player.TakeDamage(10)  ' Hover over "player" to see object
```

- Pin data tips to keep visible
- Expand objects inline

### Edit and Continue

Modify code while debugging (limited support):

1. Make changes while paused
2. Continue execution
3. Some changes require restart

### Debugging Collections

View collection contents:

```
items (List<Item>)
├── [0] {Item} Name="Sword"
├── [1] {Item} Name="Shield"
└── [2] {Item} Name="Potion"
```

- Expand to see individual items
- Use Results View for IEnumerable

## Debugging Best Practices

### 1. Use Meaningful Breakpoints

Don't scatter breakpoints randomly:
- Set breakpoints at decision points
- Use conditional breakpoints to reduce stops
- Use logpoints for tracing

### 2. Leverage the Watch Window

Track related values together:
```
player.Health
player.MaxHealth
player.Health / player.MaxHealth * 100
```

### 3. Check Call Stack

When something fails:
1. Examine call stack
2. Click through frames
3. Find where values went wrong

### 4. Use Immediate Window

Test fixes before changing code:
```
> player.Health = 100
> player.IsAlive()
True
```

### 5. Debug in Isolation

When debugging complex issues:
1. Create minimal reproduction
2. Comment out unrelated code
3. Add extra logging temporarily

## Common Debugging Scenarios

### Null Reference

When you get null reference error:
1. Break on exception
2. Check which variable is null
3. Trace back to find where it should be set

### Infinite Loop

When program hangs:
1. Press Pause button
2. Check current location
3. Examine loop variables
4. Find exit condition bug

### Wrong Value

When variable has unexpected value:
1. Set breakpoint where variable is assigned
2. Step through assignments
3. Find incorrect assignment

### Logic Error

When behavior is wrong:
1. Set breakpoint at decision point
2. Examine condition values
3. Verify logic is correct

## Performance Debugging

### Timing Code

Use Stopwatch:
```vb
Dim sw = Stopwatch.StartNew()
ExpensiveOperation()
sw.Stop()
Debug.Print($"Took {sw.ElapsedMilliseconds}ms")
```

### Memory Leaks

Monitor object creation:
1. Check watch window for growing collections
2. Look for objects not being disposed
3. Use profiler for detailed analysis

## Keyboard Reference

| Shortcut | Action |
|----------|--------|
| `F5` | Start/Continue |
| `Shift+F5` | Stop |
| `Ctrl+Shift+F5` | Restart |
| `F9` | Toggle Breakpoint |
| `F10` | Step Over |
| `F11` | Step Into |
| `Shift+F11` | Step Out |
| `Ctrl+F10` | Run to Cursor |

## Next Steps

- [IDE User Guide](ide-guide.md) - General IDE features
- [BasicLang Guide](basiclang-guide.md) - Language reference
