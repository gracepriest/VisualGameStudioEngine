# BasicLang for Visual Studio

Language support for BasicLang in Visual Studio 2022 - a modern BASIC-inspired programming language designed for game development.

## Features

- **Syntax Highlighting** - Full colorization for BasicLang syntax
- **IntelliSense** - Code completion, hover information, and signature help
- **Code Navigation** - Go to definition, find references, document symbols
- **Diagnostics** - Real-time error and warning detection
- **Code Actions** - Quick fixes and refactoring suggestions
- **Formatting** - Automatic code formatting
- **Debugging** - Full debugging support with breakpoints, stepping, and variable inspection

## Requirements

- Visual Studio 2022 (version 17.0 or higher)
- BasicLang compiler/runtime

## Installation

1. Download the VSIX file from the releases
2. Double-click to install
3. Restart Visual Studio

## Configuration

The extension automatically looks for the BasicLang language server in:
- `%LOCALAPPDATA%\BasicLang\BasicLang.exe`
- `%PROGRAMFILES%\BasicLang\BasicLang.exe`
- System PATH

## Commands

Access via Tools > BasicLang menu:
- **Build Project** - Build the current BasicLang project
- **Run Project** - Run the current BasicLang project
- **Restart Language Server** - Restart the language server

## File Extensions

This extension provides support for:
- `.bl` - BasicLang source files
- `.bas` - BasicLang source files (alternative)
- `.blproj` - BasicLang project files

## Debugging

1. Set breakpoints by clicking in the margin
2. Press F5 or use Debug > Start Debugging
3. Use the debug toolbar to step through code
4. Inspect variables in the Locals and Watch windows

## Building from Source

```bash
# Clone the repository
git clone https://github.com/visualgamestudio/VS.BasicLang

# Open in Visual Studio 2022
# Build the solution
# The VSIX will be in the bin\Debug folder
```

## License

MIT License

## Support

For issues and feature requests, please visit:
https://github.com/visualgamestudio/basiclang/issues
