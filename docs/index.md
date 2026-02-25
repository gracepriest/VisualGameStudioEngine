# Visual Game Studio Engine Documentation

Welcome to the Visual Game Studio Engine documentation. This comprehensive guide covers the game engine, BasicLang compiler, and IDE.

## Quick Links

- [Getting Started](GETTING_STARTED.md) — Step-by-step guide to your first game
- [API Reference](API_REFERENCE.md) — Complete function listing for the engine DLL
- [BasicLang Reference](BasicLang-Reference.md) — Full language syntax reference
- [User Guide](UserGuide.md) — Visual Game Studio IDE guide

## Articles

- [Getting Started (IDE)](articles/getting-started.md) — IDE setup and first project
- [Engine Guide](articles/engine-guide.md) — ECS, camera, audio, collision patterns
- [BasicLang Language Guide](articles/basiclang-guide.md) — Language features with examples
- [IDE User Guide](articles/ide-guide.md) — Editor features, shortcuts, debugging
- [Debugging Guide](articles/debugging-guide.md) — Breakpoints, watch, call stack
- [Code Coverage Guide](articles/code-coverage.md) — Test coverage setup and reporting

## Components

### Game Engine (`VisualGameStudioEngine.dll`)

A Raylib-based 2D engine exported as a C DLL with VB.NET P/Invoke bindings in `RaylibWrapper`:

- **Drawing** — Shapes, textures, fonts, sprite sheets, shaders, render textures
- **Input** — Keyboard, mouse (full cursor control)
- **ECS** — Entities, Transform2D, Sprite2D, Velocity2D, BoxCollider2D, Hierarchy, Name, Tag, Enabled
- **Camera 2D** — Smooth follow, deadzone, lookahead, shake, flash, zoom/pan transitions, world bounds
- **Audio** — Sound handles, music streaming, audio groups, spatial audio, sound pooling, playlists, crossfade, reverb/echo/filter effects
- **Scene Manager** — Scene stack, 14 transition types, 21 easing curves, loading screens
- **Physics** — Overlap queries (box, circle, entity-vs-entity)
- **Shaders** — Vertex/fragment shader load, uniform setters
- **Component Introspection** — Runtime field read/write on any component
- **Debug Overlay** — Collider bounds, hierarchy lines, entity stats
- **Asset Cache** — Ref-counted handle API for textures, fonts, music

### BasicLang Compiler (`BasicLang.exe` / `BasicLang.dll`)

A full compiler for a VB-like language targeting C#, MSIL, LLVM IR, or C++:

- VB-like syntax with type inference (`Auto`)
- Full OOP: classes, interfaces, modules, inheritance, generics/templates
- Advanced pattern matching (When guards, type/range/Or/Nothing patterns)
- LINQ, Async/Await, Try/Catch
- Bitwise operators, compound assignments, preprocessor directives
- .NET interop via `Using` directive and inline code blocks
- Multi-file projects via `Import`
- LSP server (`BasicLang.exe --lsp`) for IDE integration
- Source files use `.bas` extension, projects use `.blproj`

### Visual Game Studio IDE (`VisualGameStudio.Shell`)

An Avalonia-based IDE with:

- IntelliSense via LSP (completions, hover, diagnostics, go-to-definition, find references)
- Syntax highlighting, code folding, bracket matching, multi-cursor editing
- Breakpoint debugging, watch window, call stack
- Project and solution management
- Build system integration

## Getting Help

- [GitHub Issues](https://github.com/gracepriest/VisualGameStudioEngine/issues)
- [GitHub Repository](https://github.com/gracepriest/VisualGameStudioEngine)
