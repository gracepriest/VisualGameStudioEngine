# `Option Public` Directive for `.cls` Files — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an `Option Public` file directive to `.cls` files that makes the implicit class wrapper public, replacing the cryptic bare-`Public` first line (which stays as a deprecated synonym that warns).

**Architecture:** All compiler changes live in one method — `PreprocessClassFile` in `BasicLang/Compiler.cs` — which rewrites `.cls` source *before* lexing, so no lexer/parser changes exist anywhere in this plan. The directive line is replaced in place by the class header, so line numbers never shift (`LineOffset = 0`). Everything else is tests, one template string, and three TextMate grammar files.

**Tech Stack:** C# (.NET), NUnit (`Assert.That` style), TextMate JSON grammars.

**Spec:** `docs/superpowers/specs/2026-07-05-cls-option-public-design.md` — read it first.

**Branch/worktree:** This project commits directly to `master` (repo: `C:\Users\melvi\source\repos\VisualGameStudioEngine`). Do not push; the user pushes manually.

---

## Background for a zero-context engineer

- BasicLang is a VB-like language. A `.cls` file's whole content is implicitly wrapped by the compiler in `Private Class <filename-stem> ... End Class`. Putting the bare keyword `Public` on the first line makes the wrapper `Public Class` instead. That bare keyword is what we're replacing with `Option Public`.
- Comments in BasicLang are `' ...` or `Rem ...` (see `BasicLang/BasicLangLexer.cs:1223`).
- The preprocessing happens in `PreprocessClassFile`, `BasicLang/Compiler.cs` (currently lines ~698–741). The sibling method `PreprocessModFile` directly above it shows the house style for stderr warnings (`Console.Error.WriteLine` with the filename).
- `CompilationUnit.LineOffset` tells downstream diagnostics/debugger how many lines the wrapper inserted before the user's code. `0` = line numbers unchanged.
- Tests live in `VisualGameStudio.Tests/Compiler/ClassFileTests.cs` (NUnit, `[TestFixture]`, a `_tempDir` created per-test in `SetUp`). Follow the existing tests' style exactly — e.g. `ClsFile_PublicClass_AccessibleWithoutImport` for AST access assertions and `CrossFile_InstanceFieldAccess_ResolvesFieldType` for the `CompileProjectFiles` cross-file pattern.

**Commands** (run from repo root; PowerShell):

```powershell
# Fast: just this fixture
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ClassFileTests"

# Full suite (final task only; takes a while, ~1900 tests)
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
```

## File map

| File | Change |
|---|---|
| `BasicLang/Compiler.cs` | Rewrite `PreprocessClassFile` (~line 698): directive scan + deprecation warning |
| `VisualGameStudio.Tests/Compiler/ClassFileTests.cs` | Add 6 tests; update fixture doc comment |
| `BasicLang/ProjectSystem/TemplateEngine.cs` | `Player.cls` game template (~line 243): `Public` → `Option Public` |
| `vscode-basiclang/syntaxes/basiclang.tmLanguage.json` | Add directive highlight pattern |
| `VS.BasicLang/Resources/BasicLangGrammar.json` | Same pattern |
| `BasicLang.VisualStudio/src/BasicLang.VisualStudio/LanguageService/BasicLangGrammar.json` | Same pattern (source file only — NOT the copies under `bin/`) |
| `CLAUDE.md` | One short entry under Recent Bug Fixes |

---

### Task 1: Directive recognition in the preprocessor

**Files:**
- Test: `VisualGameStudio.Tests/Compiler/ClassFileTests.cs` (append before the closing brace)
- Modify: `BasicLang/Compiler.cs` — `PreprocessClassFile` (~line 698)

- [ ] **Step 1: Write three failing tests**

Append to `ClassFileTests.cs` (note: existing usings already cover `System.IO` and `System.Linq`):

```csharp
    /// <summary>
    /// "Option Public" as the first code line makes the implicit class public.
    /// </summary>
    [Test]
    public void OptionPublic_MakesClassPublic()
    {
        var clsFilePath = Path.Combine(_tempDir, "GlobalThing.cls");
        File.WriteAllText(clsFilePath, @"Option Public
Public Value As Integer

Sub New()
    Me.Value = 7
End Sub
");
        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
        var classNode = result.Units[0].AST.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(classNode, Is.Not.Null, "Expected a ClassNode in the AST");
        Assert.That(classNode.Name, Is.EqualTo("GlobalThing"));
        Assert.That(classNode.Access, Is.EqualTo(BasicLang.Compiler.AST.AccessModifier.Public));
    }

    /// <summary>
    /// The directive is honored below leading blank lines and comment lines
    /// (both ' and Rem forms) — unlike the legacy bare "Public" marker.
    /// </summary>
    [Test]
    public void OptionPublic_AfterLeadingComments_Works()
    {
        var clsFilePath = Path.Combine(_tempDir, "Banner.cls");
        File.WriteAllText(clsFilePath, @"' File header banner
Rem legacy-style comment

Option Public
Public Value As Integer
");
        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        var classNode = result.Units[0].AST.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(classNode, Is.Not.Null);
        Assert.That(classNode.Access, Is.EqualTo(BasicLang.Compiler.AST.AccessModifier.Public));
    }

    /// <summary>
    /// The directive is case-insensitive, like all BasicLang keywords.
    /// </summary>
    [Test]
    public void OptionPublic_IsCaseInsensitive()
    {
        foreach (var variant in new[] { "OPTION PUBLIC", "option public", "Option public" })
        {
            var clsFilePath = Path.Combine(_tempDir, "Case" + variant.GetHashCode().ToString("X") + ".cls");
            File.WriteAllText(clsFilePath, variant + "\nPublic Value As Integer\n");

            var compiler = new BasicCompiler();
            var result = compiler.CompileFile(clsFilePath);

            Assert.That(result.AllErrors, Is.Empty,
                $"Variant '{variant}' failed: {string.Join(", ", result.AllErrors)}");
            var classNode = result.Units[0].AST.Declarations.OfType<ClassNode>().FirstOrDefault();
            Assert.That(classNode.Access, Is.EqualTo(BasicLang.Compiler.AST.AccessModifier.Public),
                $"Variant '{variant}' did not produce a public class");
        }
    }
```

- [ ] **Step 2: Run the new tests — verify they FAIL**

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ClassFileTests.OptionPublic"
```

Expected: 3 failures. Failure mode: the class compiles but `Access` is `Private` (the unknown `Option Public` line gets wrapped as body code, so you may also see parse errors — either way, FAIL).

- [ ] **Step 3: Implement the directive scan**

In `BasicLang/Compiler.cs`, replace the body of `PreprocessClassFile` (keep the method signature and the XML doc, updating the doc's second sentence to mention the directive). Full replacement:

```csharp
        /// <summary>
        /// Preprocess a .cls/.class file by wrapping its contents in an implicit Class block.
        /// The filename becomes the class name. Private by default; the "Option Public"
        /// directive on the first code line (or the deprecated bare "Public" first line)
        /// makes the class public.
        /// </summary>
        private void PreprocessClassFile(CompilationUnit unit)
        {
            if (unit == null || string.IsNullOrEmpty(unit.FilePath))
                return;

            if (!ModuleResolver.IsClassFile(unit.FilePath))
                return;

            unit.IsClassFile = true;

            var source = unit.SourceCode ?? string.Empty;
            var className = Path.GetFileNameWithoutExtension(unit.FilePath);

            // "Option Public" directive: the first code line (leading blank lines and
            // ' / Rem comment lines are skipped) may be exactly "Option Public".
            // The directive line is replaced in place by the class header so every
            // line keeps its original number (LineOffset = 0); comments above the
            // directive legally remain above the class declaration.
            var lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var content = lines[i].TrimEnd('\r');
                if (i == 0)
                    content = content.TrimStart('\uFEFF');
                content = content.Trim();

                if (content.Length == 0 || content.StartsWith("'"))
                    continue;
                if (content.Equals("Rem", StringComparison.OrdinalIgnoreCase) ||
                    content.StartsWith("Rem ", StringComparison.OrdinalIgnoreCase) ||
                    content.StartsWith("Rem\t", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (content.Equals("Option Public", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"Public Class {className}";
                    unit.SourceCode = string.Join("\n", lines) + "\nEnd Class\n";
                    unit.LineOffset = 0;
                    return;
                }

                break; // first code line is not the directive — legacy handling below
            }

            // Legacy: bare "Public" keyword as the first content of the file (deprecated).
            var trimmed = source.TrimStart('\uFEFF').TrimStart();
            string accessModifier = "Private";
            string body = source;

            if (trimmed.StartsWith("Public", StringComparison.OrdinalIgnoreCase))
            {
                var afterPublic = trimmed.Substring(6);
                // Make sure "Public" is standalone keyword on its own line (not "Public Sub" etc.)
                if (afterPublic.Length == 0 || afterPublic[0] == '\r' || afterPublic[0] == '\n')
                {
                    var firstLine = trimmed.Split('\n')[0].Trim().TrimEnd('\r');
                    if (firstLine.Equals("Public", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine(
                            $"Warning: '{Path.GetFileName(unit.FilePath)}': bare 'Public' first line is deprecated — use 'Option Public'");
                        accessModifier = "Public";
                        var newlineIndex = trimmed.IndexOf('\n');
                        body = newlineIndex >= 0 ? trimmed.Substring(newlineIndex + 1) : string.Empty;
                    }
                }
            }

            unit.SourceCode = $"{accessModifier} Class {className}\n{body}\nEnd Class\n";
            // When "Public" was on its own line and stripped from body, the class
            // declaration replaces it so the remaining body lines keep their original
            // positions (offset = 0). Otherwise 1 line is inserted before the body.
            unit.LineOffset = (accessModifier == "Public" && body != source) ? 0 : 1;
        }
```

This is the *existing* legacy logic verbatim plus (a) the directive loop up front and (b) the one `Console.Error.WriteLine` deprecation warning. Nothing else changes. (The deprecation warning is asserted later in Task 4 — it's included now so the method is only touched once.)

- [ ] **Step 4: Run the fixture — new tests PASS, all pre-existing tests still PASS**

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ClassFileTests"
```

Expected: all pass (14 existing + 3 new = 17). If `ClsFile_PublicSubOnFirstLine_StaysPrivate` fails, the directive loop's `break` is wrong; if `ClsFile_PublicClass_AccessibleWithoutImport` fails, the legacy branch was altered — compare against the original at git HEAD.

- [ ] **Step 5: Commit**

```powershell
git add BasicLang/Compiler.cs VisualGameStudio.Tests/Compiler/ClassFileTests.cs
git commit -m "feat: Option Public directive for .cls files"
```

---

### Task 2: Cross-file access without Import

**Files:**
- Test: `VisualGameStudio.Tests/Compiler/ClassFileTests.cs`

- [ ] **Step 1: Write the integration test**

Model on `CrossFile_InstanceFieldAccess_ResolvesFieldType` (same file, ~line 205):

```csharp
    /// <summary>
    /// A .cls class made public via Option Public is usable from a sibling file
    /// with no Import statement (project-files compilation).
    /// </summary>
    [Test]
    public void OptionPublic_CrossFile_NoImportNeeded()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Enemy.cls"), @"Option Public
Public Name As String

Public Sub New(n As String)
    Me.Name = n
End Sub
");
        var mainPath = Path.Combine(_tempDir, "Main.bas");
        File.WriteAllText(mainPath, @"Module Main
    Sub Main()
        Dim e As New Enemy(""Slime"")
        Dim s As String = e.Name
    End Sub
End Module
");

        var compiler = new BasicCompiler();
        var result = compiler.CompileProjectFiles(new[]
        {
            mainPath,
            Path.Combine(_tempDir, "Enemy.cls")
        });

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        Assert.That(result.Success, Is.True);
    }
```

- [ ] **Step 2: Run it — expected PASS**

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ClassFileTests.OptionPublic_CrossFile"
```

This behavior should fall out of Task 1 (the directive produces the same `Public Class` wrapper the bare keyword did, and public-class cross-file access already works). If it FAILS, stop and debug with @superpowers:systematic-debugging before proceeding — do not paper over it.

- [ ] **Step 3: Commit**

```powershell
git add VisualGameStudio.Tests/Compiler/ClassFileTests.cs
git commit -m "test: pin Option Public cross-file access without Import"
```

---

### Task 3: Line numbers preserved (`LineOffset = 0`, in-place replacement)

**Files:**
- Test: `VisualGameStudio.Tests/Compiler/ClassFileTests.cs`

- [ ] **Step 1: Write the test**

```csharp
    /// <summary>
    /// The directive line is replaced in place by the class header, so every
    /// line keeps its original number and LineOffset is 0 (diagnostics and the
    /// debugger SourceMapper need no adjustment).
    /// </summary>
    [Test]
    public void OptionPublic_PreservesLineNumbers()
    {
        var clsFilePath = Path.Combine(_tempDir, "LineCheck.cls");
        File.WriteAllText(clsFilePath, "' banner\r\nOption Public\r\nPublic Value As Integer\r\n");

        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        var unit = result.Units[0];
        Assert.That(unit.LineOffset, Is.EqualTo(0));

        var lines = unit.SourceCode.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.That(lines[0], Is.EqualTo("' banner"), "comment must stay on line 1");
        Assert.That(lines[1], Is.EqualTo("Public Class LineCheck"), "directive line replaced in place");
        Assert.That(lines[2], Is.EqualTo("Public Value As Integer"), "body lines must not shift");
    }
```

- [ ] **Step 2: Run it — expected PASS** (Task 1 already implemented this; this test pins it)

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ClassFileTests.OptionPublic_PreservesLineNumbers"
```

- [ ] **Step 3: Commit**

```powershell
git add VisualGameStudio.Tests/Compiler/ClassFileTests.cs
git commit -m "test: pin Option Public in-place line numbering"
```

---

### Task 4: Bare `Public` still works and warns

**Files:**
- Test: `VisualGameStudio.Tests/Compiler/ClassFileTests.cs` (also add `using System;` at the top — `Console` needs it)

- [ ] **Step 1: Write the test**

`Console.SetError` swaps a global, so mark the test `[NonParallelizable]`:

```csharp
    /// <summary>
    /// The legacy bare "Public" first line still compiles as a public class,
    /// but emits a deprecation warning on stderr pointing at Option Public.
    /// </summary>
    [Test]
    [NonParallelizable]
    public void BarePublic_StillWorks_AndWarns()
    {
        var clsFilePath = Path.Combine(_tempDir, "OldStyle.cls");
        File.WriteAllText(clsFilePath, @"Public
Public Value As Integer
");
        var originalError = Console.Error;
        var captured = new StringWriter();
        CompilationResult result;
        try
        {
            Console.SetError(captured);
            var compiler = new BasicCompiler();
            result = compiler.CompileFile(clsFilePath);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.That(result.AllErrors, Is.Empty,
            $"Expected no errors but got: {string.Join(", ", result.AllErrors)}");
        var classNode = result.Units[0].AST.Declarations.OfType<ClassNode>().FirstOrDefault();
        Assert.That(classNode.Access, Is.EqualTo(BasicLang.Compiler.AST.AccessModifier.Public),
            "bare Public must keep working");
        Assert.That(captured.ToString(), Does.Contain("deprecated"));
        Assert.That(captured.ToString(), Does.Contain("Option Public"));
    }
```

If `CompilationResult` is not directly nameable, use `var` + declare-and-assign inside the try and hoist with the concrete type the compiler suggests.

- [ ] **Step 2: Run it — expected PASS** (warning was implemented in Task 1). If it fails on the stderr assertions, check the warning text in `PreprocessClassFile` matches: `bare 'Public' first line is deprecated — use 'Option Public'` (note the em dash, matching the `.mod` warning style at Compiler.cs ~line 690).

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ClassFileTests.BarePublic_StillWorks_AndWarns"
```

- [ ] **Step 3: Commit**

```powershell
git add VisualGameStudio.Tests/Compiler/ClassFileTests.cs
git commit -m "test: pin bare-Public deprecation warning for .cls files"
```

---

### Task 5: Directive after real code is not a directive

**Files:**
- Test: `VisualGameStudio.Tests/Compiler/ClassFileTests.cs`

- [ ] **Step 1: Write the test**

```csharp
    /// <summary>
    /// "Option Public" below the first code line is NOT a directive: the class
    /// wraps as Private and the stray line surfaces as an ordinary error.
    /// </summary>
    [Test]
    public void OptionPublic_AfterCode_NotADirective()
    {
        var clsFilePath = Path.Combine(_tempDir, "NotFirst.cls");
        File.WriteAllText(clsFilePath, @"Public Value As Integer
Option Public
");
        var compiler = new BasicCompiler();
        var result = compiler.CompileFile(clsFilePath);

        // The stray "Option Public" lands inside the class body and is invalid
        // there. The essential contract: this file must NOT compile as a clean
        // public class.
        Assert.That(result.AllErrors, Is.Not.Empty,
            "Option Public below code must not be silently accepted");
    }
```

Note for the implementer: if this unexpectedly compiles clean (parser tolerating `Option Public` as body code), do NOT weaken the assertion — investigate what the parser did with the line; the fallback assertion is that the unit's preprocessed `SourceCode` starts with `Private Class` (but `result.Units` is only populated when parsing succeeds, which is why errors are the primary assertion).

- [ ] **Step 2: Run it — expected PASS**

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ClassFileTests.OptionPublic_AfterCode"
```

- [ ] **Step 3: Commit**

```powershell
git add VisualGameStudio.Tests/Compiler/ClassFileTests.cs
git commit -m "test: Option Public after code is not a directive"
```

---

### Task 6: Update the game template

**Files:**
- Modify: `BasicLang/ProjectSystem/TemplateEngine.cs` (~line 243)

- [ ] **Step 1: Check whether any test pins the old template text**

```powershell
Get-ChildItem VisualGameStudio.Tests -Recurse -Filter *.cs | Select-String -Pattern "Player.cls" -SimpleMatch
```

(`Select-String -Path` does not support `**` recursion in Windows PowerShell 5.1.)
Expected result: no matches — as of plan review, no test pins the template text and no
template end-to-end test exists in the suite (nothing references `TemplateEngine`). Do
not go hunting for one. If a match DOES appear (something landed since), update that
expectation in the same commit.

- [ ] **Step 2: Edit the template**

In `TemplateEngine.cs`, the game template's `Player.cls` entry currently begins:

```csharp
                    ["Player.cls"] = @"Public

Public Name As String
```

Change only the first line of the template string:

```csharp
                    ["Player.cls"] = @"Option Public

Public Name As String
```

Leave `Types.cls` in the library template untouched — **explicit user decision** (it stays private).

- [ ] **Step 3: Run template-related tests + the ClassFileTests fixture**

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~Template|FullyQualifiedName~ClassFileTests"
```

Expected: all pass.

- [ ] **Step 4: Commit**

```powershell
git add BasicLang/ProjectSystem/TemplateEngine.cs
git commit -m "feat: game template Player.cls uses Option Public"
```

(Include any updated test file in the `git add` if Step 1 found one.)

---

### Task 7: TextMate grammar highlighting

**Files:**
- Modify: `vscode-basiclang/syntaxes/basiclang.tmLanguage.json`
- Modify: `VS.BasicLang/Resources/BasicLangGrammar.json`
- Modify: `BasicLang.VisualStudio/src/BasicLang.VisualStudio/LanguageService/BasicLangGrammar.json`

Do NOT touch the copies under any `bin/` directory or under `.claude/worktrees/`. VSIX rebuilds are out of scope (spec: deferred to next extension release).

- [ ] **Step 1: Add the directive pattern to all three grammars**

Each grammar has a `"keywords"` repository entry whose `"patterns"` array starts with `keyword.control.basiclang` (in the vscode file, ~line 246). Insert this as the FIRST element of that array in each file (line-anchored, so it wins over the generic `Public` modifier match):

```json
        {
          "name": "keyword.other.option.basiclang",
          "match": "(?i)^\\s*(Option\\s+Public)\\b"
        },
```

The two VS grammar files are historical copies of the vscode one — verify each actually contains the same `"keywords"` structure before editing; if one differs, insert the same pattern object into its equivalent keyword patterns array.

- [ ] **Step 2: Validate the JSON parses**

```powershell
Get-Content vscode-basiclang/syntaxes/basiclang.tmLanguage.json -Raw | ConvertFrom-Json | Out-Null; if ($?) { "vscode OK" }
Get-Content VS.BasicLang/Resources/BasicLangGrammar.json -Raw | ConvertFrom-Json | Out-Null; if ($?) { "VS.BasicLang OK" }
Get-Content BasicLang.VisualStudio/src/BasicLang.VisualStudio/LanguageService/BasicLangGrammar.json -Raw | ConvertFrom-Json | Out-Null; if ($?) { "BasicLang.VisualStudio OK" }
```

Expected: three OK lines, no exceptions.

- [ ] **Step 3: Commit**

```powershell
git add vscode-basiclang/syntaxes/basiclang.tmLanguage.json VS.BasicLang/Resources/BasicLangGrammar.json BasicLang.VisualStudio/src/BasicLang.VisualStudio/LanguageService/BasicLangGrammar.json
git commit -m "feat: highlight Option Public directive in TextMate grammars"
```

---

### Task 8: Full-suite verification and docs

**Files:**
- Modify: `VisualGameStudio.Tests/Compiler/ClassFileTests.cs` (fixture doc comment, lines 9–14)
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update the fixture doc comment**

Lines 9–14 of `ClassFileTests.cs` describe the old contract. Replace the sentence `Private by default; use "Public" on the first line for global access.` with:

```
/// Private by default; use "Option Public" as the first code line for global access
/// (bare "Public" on line 1 still works but is deprecated and warns).
```

- [ ] **Step 2: Run the FULL test suite**

```powershell
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
```

Expected: everything passes (1918 pre-existing + 6 new = 1924, exact count may differ if other work landed). Any failure outside `ClassFileTests` — especially in template, LSP, or debugger fixtures — must be investigated (@superpowers:systematic-debugging), not skipped.

- [ ] **Step 3: Add a CLAUDE.md entry**

Under `## Recent Bug Fixes (January 2026)` — append a new subsection at the end of that section:

```markdown
### .cls Option Public Directive (July 2026)
- **`Option Public` directive**: first code line of a .cls file (comments/blank lines may precede) makes the implicit class public — clearer than the legacy bare `Public` line
- Bare `Public` first line still works but emits a deprecation warning
- Directive line is replaced in place by the class header, so `LineOffset = 0` (no diagnostic/debugger shifts)
- Spec: `docs/superpowers/specs/2026-07-05-cls-option-public-design.md`
```

- [ ] **Step 4: Commit**

```powershell
git add VisualGameStudio.Tests/Compiler/ClassFileTests.cs CLAUDE.md
git commit -m "docs: document Option Public directive"
```

- [ ] **Step 5: Verify completion**

Use @superpowers:verification-before-completion: re-run the full suite, confirm green output is actually in hand before reporting done. Do not push — the user pushes manually.
