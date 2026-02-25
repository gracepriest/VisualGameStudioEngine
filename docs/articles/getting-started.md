# Getting Started with Visual Game Studio Engine

This guide will help you get up and running with Visual Game Studio Engine.

## Prerequisites

- .NET 8.0 SDK
- Visual Studio 2022 or VS Code
- C++ compiler (for engine DLL, optional)

## Installation

### 1. Clone the Repository

```bash
git clone https://github.com/gracepriest/VisualGameStudioEngine.git
cd VisualGameStudioEngine
```

### 2. Build the Solution

```bash
dotnet build VisualGameStudioEngine.sln
```

### 3. Run the IDE

```bash
dotnet run --project VisualGameStudio.Editor/VisualGameStudio.Editor.csproj
```

## Your First Project

### Creating a New Project

1. Open Visual Game Studio IDE
2. Select **File > New Project**
3. Choose a project template
4. Enter a project name and location
5. Click **Create**

### Writing Your First Program

Create a new file `HelloWorld.bas`:

```vb
Sub Main()
    PrintLine("Hello, World!")
End Sub
```

### Running Your Program

1. Press **F5** or click **Run** in the toolbar
2. The output window will display "Hello, World!"

## Project Structure

```
MyProject/
├── MyProject.blproj      # Project file
├── Main.bas              # Entry point
├── Modules/              # Additional modules
└── Assets/               # Game assets
```

## Next Steps

- [BasicLang Language Guide](basiclang-guide.md) - Learn the language
- [IDE User Guide](ide-guide.md) - Master the IDE
- [Game Engine Guide](engine-guide.md) - Build games

## Running Tests

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj
```

## Code Coverage

```bash
dotnet test --collect:"XPlat Code Coverage" --settings VisualGameStudio.Tests/coverlet.runsettings
```

Coverage reports will be generated in `TestResults/` directory.
