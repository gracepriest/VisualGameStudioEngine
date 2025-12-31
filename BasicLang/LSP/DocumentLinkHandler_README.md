# DocumentLinkHandler

## Overview

The `DocumentLinkHandler` provides clickable links in BasicLang source files for imports, file paths, and URLs. This enhances the development experience by allowing users to quickly navigate to referenced files and documentation.

## Features

### 1. Import/Imports Statement Links

The handler detects `Import` and `Imports` statements and creates clickable links to the referenced files.

**Example:**
```vb
Import "stdlib.bas"
Import "utils.bas"
Imports "helpers.bas"
```

- Supports both `Import` and `Imports` keywords (case-insensitive)
- Resolves relative paths based on the current document's directory
- Automatically adds `.bas` extension if not present (for AST-based imports)
- Only creates links if the target file exists

### 2. File Path String Links

Any string literal containing a valid file path becomes clickable.

**Example:**
```vb
Dim configPath As String = "C:\config\settings.ini"
Dim dataPath As String = ".\data\input.txt"
Dim relativePath As String = "..\shared\common.bas"
```

**Supported path formats:**
- Absolute Windows paths: `"C:\path\to\file.txt"`
- Relative paths: `".\file.txt"`, `"..\parent\file.txt"`
- Forward slashes: `"C:/path/to/file.txt"`

**Path resolution:**
- Absolute paths are used as-is
- Relative paths are resolved against the current document's directory
- Links are only created if the target file exists

### 3. URL Links in Comments

URLs in comment lines are automatically detected and made clickable.

**Example:**
```vb
' Documentation: https://github.com/basiclang/basiclang
' API Reference: https://api.example.com/v1/docs
' Stack Overflow: https://stackoverflow.com/questions/1234567
```

**Supported URL schemes:**
- `http://`
- `https://`

**Features:**
- URLs are detected using regex pattern matching
- Only works in comment lines (lines starting with `'`)
- Invalid URLs are silently skipped

### 4. AST-Based Import Detection

In addition to text-based pattern matching, the handler also scans the AST for `ImportDirectiveNode` instances. This provides more accurate detection of import statements that may have been parsed.

## Implementation Details

### Pattern Matching

The handler uses regular expressions for efficient pattern detection:

1. **URL Pattern:**
   ```regex
   https?://[^\s\)<>"']+
   ```
   Matches HTTP and HTTPS URLs until whitespace or common delimiters.

2. **File Path Pattern:**
   ```regex
   "([A-Za-z]:[\\\/](?:[^"\\]|\\.)+|\.{1,2}[\\\/](?:[^"\\]|\\.)+)"
   ```
   Matches quoted strings containing:
   - Absolute Windows paths (e.g., `C:\...`)
   - Relative paths (e.g., `.\...` or `..\...`)

3. **Import Pattern:**
   ```regex
   \b(?:Import|Imports)\s+"([^"]+)"
   ```
   Matches Import/Imports keywords followed by a quoted string.

### Path Resolution

The `ResolveFileUri` method handles path resolution:

1. Checks if the path is rooted (absolute)
2. If relative, combines with the document's directory using `Path.Combine`
3. Normalizes the path using `Path.GetFullPath`
4. Verifies the file exists using `File.Exists`
5. Returns `DocumentUri` if successful, `null` otherwise

### LSP Integration

The handler implements the `IDocumentLinkHandler` interface from OmniSharp.Extensions.LanguageServer.Protocol:

- **Handle Method:** Processes `DocumentLinkParams` and returns `DocumentLinkContainer`
- **GetRegistrationOptions:** Configures the handler for "basiclang" language

### DocumentLink Structure

Each link contains:
- **Range:** Start and end position in the document (line and column)
- **Target:** The URI to navigate to (file:// or http://)
- **Tooltip:** Descriptive text shown on hover (e.g., "Open filename.bas")

## Usage

### In VS Code

When using the BasicLang LSP server in VS Code:

1. Hover over an import statement, file path, or URL
2. The text should be underlined (if supported by the client)
3. Ctrl+Click (Cmd+Click on Mac) to open the link
4. Tooltips appear on hover showing the target

### Registration

The handler is registered in `BasicLangLanguageServer.cs`:

```csharp
.WithHandler<DocumentLinkHandler>()
```

The `DocumentManager` service is automatically injected via dependency injection.

## Testing

To test the DocumentLinkHandler:

1. Create a BasicLang file with various link types
2. Use the provided `test_document_links.bas` file
3. Open in an LSP-enabled editor (VS Code with BasicLang extension)
4. Verify that:
   - Import statements are clickable
   - File paths in strings are clickable (if files exist)
   - URLs in comments are clickable

## Limitations

1. **File Existence:** Links are only created for files that exist on the filesystem
2. **Comment Detection:** URL detection only works in single-line comments starting with `'`
3. **Multi-line Imports:** Currently only handles single-line import statements
4. **String Escaping:** Complex escape sequences in file paths may not be fully supported
5. **Include Directives:** While mentioned in requirements, BasicLang uses `Import` rather than `Include`

## Future Enhancements

Possible improvements:

1. Support for module/namespace resolution (e.g., `Import System.IO`)
2. Peek definition for imported files
3. Validation warnings for missing import files
4. Support for URL links in all contexts (not just comments)
5. Configurable import path resolution strategies
6. Support for workspace-relative paths

## Technical Notes

### Thread Safety

The handler is stateless and thread-safe. Each request creates a new list of links without shared mutable state.

### Performance

- Line-by-line scanning for efficient processing
- Compiled regex patterns for fast matching
- Early exit for non-existent files to avoid unnecessary I/O

### Error Handling

- Invalid URIs are caught and silently skipped
- File system errors (permissions, etc.) return null instead of crashing
- Malformed paths are gracefully handled

## Related Files

- `DocumentLinkHandler.cs` - Main implementation
- `BasicLangLanguageServer.cs` - Handler registration
- `DocumentManager.cs` - Document state management
- `ASTNodes.cs` - ImportDirectiveNode definition
