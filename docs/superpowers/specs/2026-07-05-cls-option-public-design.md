# Design: `Option Public` directive for `.cls` files

**Date:** 2026-07-05
**Status:** Approved (design), pending implementation
**Owner:** melvin / Claude Code session

## Problem

A `.cls` file is wrapped by the compiler in an implicit `Private Class <filename>` block.
To make the class public (usable from other files without `Import`), the coder must put a
bare `Public` keyword on the first line of the file. This syntax has three problems:

1. **Unclear intent.** A lone `Public` reads like an unfinished declaration, not a
   visibility directive. Nothing about it says "this applies to the whole file".
2. **Ambiguity requires defensive parsing.** `PreprocessClassFile` (Compiler.cs) must
   special-case `Public Sub Foo()` on the first line so it is *not* mistaken for the
   marker (pinned by test `ClsFile_PublicSubOnFirstLine_StaysPrivate`).
3. **Cannot coexist with file header comments.** The scanner only skips whitespace, so a
   `.cls` file that starts with a `'` comment banner (as the shipped `Types.cls` template
   does) can never be made public with the bare keyword.

## Decision

Introduce an **`Option Public`** file directive for `.cls`/`.class` files, following the
VB idiom of `Option Explicit` / `Option Strict`. Keep the bare `Public` form working as a
deprecated synonym that emits a warning.

Alternatives considered and rejected:

- **`Public Class` header (no name):** reads like a truncated declaration; invites users
  to add a name and `End Class`, forking the syntax; collides with legitimate nested
  class declarations inside `.cls` bodies.
- **New `Global` keyword:** clearest single word, but reserves a keyword that VB.NET uses
  for namespace qualification (`Global.System.String`), and the wrapper actually maps to
  `Public` accessibility, so the word slightly misstates the semantics.

## Syntax and semantics

- The directive is the line `Option Public`, case-insensitive, standalone on its own line
  (nothing else on the line except trailing whitespace / a trailing comment is NOT
  allowed — the whole line must be the directive).
- It must be the **first code line** of the file: leading blank lines and `'` comment
  lines are skipped when scanning for it. This is a deliberate improvement over the bare
  `Public` form.
- Effect: the implicit wrapper becomes `Public Class <filename>` instead of
  `Private Class <filename>`. No other semantics change.
- If the directive appears after any real code, it is not recognized as a directive; it
  will land inside the class body and surface as an ordinary parse error.
- Only `.cls`/`.class` files are affected. In `.mod`/`.bas` files the text `Option Public`
  is unchanged behavior (a normal parse error today). Out of scope by design.

## Implementation

All changes are confined to `PreprocessClassFile` in `BasicLang/Compiler.cs` (the
directive is consumed before lexing, so no lexer or parser changes):

1. Scan leading trivia (blank lines, `'` comment lines) for a line whose trimmed content
   equals `Option Public` (OrdinalIgnoreCase). Strip BOM (`﻿`) as the existing code
   does.
2. When found: **replace that line in place** with `Public Class <filename-stem>` and
   append `End Class` at the end of the file. Because comments are legal above a class
   declaration, every original line keeps its line number → `unit.LineOffset = 0`.
   Diagnostics adjustment and the debugger `SourceMapper` need no changes.
3. When not found, fall through to the existing logic unchanged:
   - Bare `Public` first line → public wrapper as today, **plus** a deprecation warning
     on stderr in the same style as the existing `.mod` double-Module warning:
     `Warning: '<file>': bare 'Public' first line is deprecated — use 'Option Public'`
   - Otherwise → `Private Class` wrapper, `LineOffset = 1`, as today.

Precedence: if both forms are somehow present, `Option Public` (scanned first) wins; a
later bare `Public` line is ordinary code and will produce a parse error.

## Templates and tooling

- **Game template** (`BasicLang/ProjectSystem/TemplateEngine.cs`): `Player.cls` changes
  from bare `Public` first line to `Option Public`.
- **`Types.cls` in the library template stays as-is (private)** — explicit user decision.
- **TextMate grammars**: add `Option Public` highlighting to `vscode-basiclang` and the
  grammar copies shipped in the two VS extensions (`VS.BasicLang`,
  `BasicLang.VisualStudio/LanguageService/BasicLangGrammar.json`).
- LSP completion for the directive in empty `.cls` files: **out of scope** (nice-to-have).

## Testing (TDD — tests first)

New tests in `VisualGameStudio.Tests/Compiler/ClassFileTests.cs`:

1. `OptionPublic_MakesClassAccessibleWithoutImport` — cross-file: `.cls` with the
   directive is usable from a sibling `.bas` with no `Import`.
2. `OptionPublic_AfterLeadingComments_Works` — directive honored below a `'` banner.
3. `OptionPublic_IsCaseInsensitive` — `option public` / `OPTION PUBLIC` work.
4. `OptionPublic_PreservesLineNumbers` — an error in the body reports the original line
   (asserts `LineOffset == 0` behavior end to end).
5. `BarePublic_StillWorks_AndWarns` — old form compiles public; stderr contains the
   deprecation warning.
6. `OptionPublic_AfterCode_NotADirective` — directive below real code does not make the
   class public (and produces a parse error).
7. Existing game-template end-to-end test updated/extended so the template still builds.

All existing `ClassFileTests` must keep passing unchanged (back-compat guarantee).

## Out of scope

- `Option Public` in `.mod` / `.bas` files (no special handling, no "has no effect"
  warning).
- Per-symbol `Global` visibility modifiers.
- LSP completion / code actions for the directive.
- Removing bare `Public` support (no removal date set).
