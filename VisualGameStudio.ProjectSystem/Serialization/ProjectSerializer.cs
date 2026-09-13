using System.Text;
using System.Xml;
using System.Xml.Linq;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Serialization;

public class ProjectSerializer
{
    private const string ProjectVersion = "1.0";

    /// <param name="defaultBackendWhenOmitted">
    /// When the project file has no explicit <c>&lt;TargetBackend&gt;</c> element, use this backend
    /// instead of the model default. Lets the IDE apply the <c>basiclang.compiler.backend</c> setting
    /// to hand-written .blproj files that omit the element (a per-project value always wins because it
    /// is read from the file below). Null keeps the <see cref="BasicLangProject"/> model default (CSharp).
    /// </param>
    public async Task<BasicLangProject> LoadAsync(string filePath, TargetBackend? defaultBackendWhenOmitted = null, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var doc = XDocument.Parse(content);
        var root = doc.Root;

        if (root == null || (root.Name.LocalName != "BasicLangProject" && root.Name.LocalName != "Project"))
        {
            throw new InvalidOperationException("Invalid project file format: root element must be <BasicLangProject> or <Project>");
        }

        var project = new BasicLangProject
        {
            FilePath = filePath,
            Version = root.Attribute("Version")?.Value ?? ProjectVersion,
            // Seed with the IDE's configured default; an explicit <TargetBackend> in the file
            // (parsed below) overrides it, so the per-project value always wins.
            TargetBackend = defaultBackendWhenOmitted ?? TargetBackend.CSharp
        };

        // Parse PropertyGroup elements
        foreach (var propertyGroup in root.Elements("PropertyGroup"))
        {
            var condition = propertyGroup.Attribute("Condition")?.Value;

            if (string.IsNullOrEmpty(condition))
            {
                // Global properties
                project.Name = propertyGroup.Element("ProjectName")?.Value ?? Path.GetFileNameWithoutExtension(filePath);
                project.RootNamespace = propertyGroup.Element("RootNamespace")?.Value ?? project.Name;

                var outputType = propertyGroup.Element("OutputType")?.Value;
                if (!string.IsNullOrEmpty(outputType) && Enum.TryParse<OutputType>(outputType, true, out var ot))
                {
                    project.OutputType = ot;
                }

                var backend = propertyGroup.Element("TargetBackend")?.Value;
                if (!string.IsNullOrEmpty(backend) && Enum.TryParse<TargetBackend>(backend, true, out var tb))
                {
                    project.TargetBackend = tb;
                }

                var language = propertyGroup.Element("Language")?.Value;
                if (!string.IsNullOrEmpty(language) &&
                    Enum.TryParse<ProjectLanguage>(language, true, out var lang))
                {
                    project.Language = lang;
                }

                // CppStandard is parsed regardless of Language: a Language=BasicLang project with
                // TargetBackend=Cpp (a "mixed" native project) also carries C++ settings.
                var cppStandard = propertyGroup.Element("CppStandard")?.Value;
                if (!string.IsNullOrEmpty(cppStandard))
                {
                    project.CppSettings ??= new CppProjectSettings();
                    project.CppSettings.CppStandard = cppStandard;
                }

                // CppToolchain follows the same language-independent rule as CppStandard.
                // Trimmed + lowercased to match the compiler-side ProjectFile exactly;
                // absent/empty = null (machine probe).
                var cppToolchain = propertyGroup.Element("CppToolchain")?.Value.Trim();
                if (!string.IsNullOrEmpty(cppToolchain))
                {
                    project.CppSettings ??= new CppProjectSettings();
                    project.CppSettings.CppToolchain = cppToolchain.ToLowerInvariant();
                }

                // .NET framework/UI properties. Absent element => null, never a default: the
                // serializer writes a property only when the model disagrees with the file, so a
                // null keeps a project that omits these byte-identical across a save.
                // Read case-insensitively: MSBuild property names are case-insensitive but XLinq
                // lookup is not, and the spellings genuinely differ in the wild — the IDE's own
                // template emits <UseWPF> while <UseWpf> is just as valid.
                var targetFramework = PropertyValue(propertyGroup, "TargetFramework")?.Trim();
                if (!string.IsNullOrEmpty(targetFramework))
                {
                    project.TargetFramework = targetFramework;
                }

                var assemblyName = PropertyValue(propertyGroup, "AssemblyName")?.Trim();
                if (!string.IsNullOrEmpty(assemblyName))
                {
                    project.AssemblyName = assemblyName;
                }

                if (bool.TryParse(PropertyValue(propertyGroup, "UseWindowsForms"), out var useWinForms))
                {
                    project.UseWindowsForms = useWinForms;
                }

                if (bool.TryParse(PropertyValue(propertyGroup, "UseWPF"), out var useWpf))
                {
                    project.UseWpf = useWpf;
                }

                var highDpiMode = PropertyValue(propertyGroup, "ApplicationHighDpiMode")?.Trim();
                if (!string.IsNullOrEmpty(highDpiMode))
                {
                    project.ApplicationHighDpiMode = highDpiMode;
                }
            }
            else
            {
                // Configuration-specific properties
                var configName = ExtractConfigurationName(condition);
                if (!string.IsNullOrEmpty(configName))
                {
                    var config = new BuildConfiguration
                    {
                        Name = configName,
                        OutputPath = propertyGroup.Element("OutputPath")?.Value ?? $"bin\\{configName}",
                        DebugSymbols = bool.TryParse(propertyGroup.Element("DebugSymbols")?.Value, out var ds) && ds,
                        Optimize = bool.TryParse(propertyGroup.Element("Optimize")?.Value, out var opt) && opt,
                        DefineConstants = propertyGroup.Element("DefineConstants")?.Value
                    };

                    project.Configurations[configName] = config;
                }
            }
        }

        // Parse ItemGroup elements
        foreach (var itemGroup in root.Elements("ItemGroup"))
        {
            foreach (var compile in itemGroup.Elements("Compile"))
            {
                var include = compile.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(include))
                {
                    project.Items.Add(new ProjectItem(include, ProjectItemType.Compile));
                }
            }

            foreach (var contentItem in itemGroup.Elements("Content"))
            {
                var include = contentItem.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(include))
                {
                    project.Items.Add(new ProjectItem(include, ProjectItemType.Content));
                }
            }

            foreach (var resource in itemGroup.Elements("Resource"))
            {
                var include = resource.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(include))
                {
                    project.Items.Add(new ProjectItem(include, ProjectItemType.Resource));
                }
            }

            foreach (var includeDir in itemGroup.Elements("IncludeDir"))
            {
                var include = includeDir.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(include))
                {
                    project.CppSettings ??= new CppProjectSettings();
                    project.CppSettings.IncludeDirs.Add(include);
                }
            }

            foreach (var nativeLib in itemGroup.Elements("NativeLib"))
            {
                var include = nativeLib.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(include))
                {
                    project.CppSettings ??= new CppProjectSettings();
                    project.CppSettings.NativeLibs.Add(include);
                }
            }

            foreach (var define in itemGroup.Elements("Define"))
            {
                var include = define.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(include))
                {
                    project.CppSettings ??= new CppProjectSettings();
                    project.CppSettings.Defines.Add(include);
                }
            }

            foreach (var reference in itemGroup.Elements("Reference"))
            {
                var name = reference.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    project.References.Add(new ProjectReference
                    {
                        Name = name,
                        Path = reference.Element("HintPath")?.Value
                    });
                }
            }

            // Project-to-project references. Parsed (and re-emitted on save) so an IDE
            // save never silently deletes them — they feed cross-project IntelliSense via
            // the LSP WorkspaceManager, which reads ProjectFile.ProjectReferences.
            foreach (var projectReference in itemGroup.Elements("ProjectReference"))
            {
                var include = projectReference.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(include))
                {
                    project.References.Add(new ProjectReference
                    {
                        // Path keeps the raw Include verbatim so a save round-trips it
                        // unchanged; Name is just the friendly project name for display.
                        Name = Path.GetFileNameWithoutExtension(include),
                        Path = include,
                        IsProjectReference = true
                    });
                }
            }

            // NuGet package references. Parsed (and re-emitted on save) so an IDE save never
            // silently deletes them — the compiler-side build restores packages from these.
            // Version may be an attribute or a child <Version> element, matching the
            // compiler-side ProjectFile; absent version defaults to "*".
            foreach (var packageReference in itemGroup.Elements("PackageReference"))
            {
                var include = packageReference.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(include))
                {
                    var version = packageReference.Attribute("Version")?.Value
                        ?? packageReference.Element("Version")?.Value;
                    project.PackageReferences.Add(new PackageReference
                    {
                        Name = include,
                        Version = string.IsNullOrEmpty(version) ? "*" : version
                    });
                }
            }
        }

        // Remember the backend as loaded, so a preserving save can tell a real edit apart from the
        // value seeded by defaultBackendWhenOmitted for a file that carries no <TargetBackend>.
        project.BackendAtLoad = project.TargetBackend;

        // Ensure default configurations exist
        if (!project.Configurations.ContainsKey("Debug"))
        {
            project.Configurations["Debug"] = new BuildConfiguration
            {
                Name = "Debug",
                OutputPath = "bin\\Debug",
                DebugSymbols = true,
                Optimize = false
            };
        }

        if (!project.Configurations.ContainsKey("Release"))
        {
            project.Configurations["Release"] = new BuildConfiguration
            {
                Name = "Release",
                OutputPath = "bin\\Release",
                DebugSymbols = false,
                Optimize = true
            };
        }

        return project;
    }

    /// <summary>
    /// Writes the project back to <see cref="BasicLangProject.FilePath"/>.
    ///
    /// <para>When the file already exists this edits it IN PLACE, touching only the elements whose
    /// model value disagrees with what the file says (<see cref="SavePreservingAsync"/>).
    /// Everything else — unmodeled properties, item metadata, <c>&lt;Import&gt;</c>s, the
    /// <c>Sdk</c> attribute, comments, formatting — survives untouched.</para>
    ///
    /// <para>⚠ This used to rebuild the document from the model, which DESTROYED every element the
    /// serializer does not parse. The visible symptoms were a WinForms project losing
    /// <c>&lt;UseWindowsForms&gt;</c> on the first IDE save, and a VS 2022-created .blproj being
    /// flattened into <c>&lt;BasicLangProject&gt;</c> — dropping the <c>Sdk</c> attribute and both
    /// <c>&lt;Import&gt;</c>s — until VS could no longer load it. Preserving unknown elements is
    /// deliberately preferred over enumerating the known-lost ones: adding the three properties we
    /// knew about would have left the defect in place for the fourth.</para>
    /// </summary>
    public async Task SaveAsync(BasicLangProject project, CancellationToken cancellationToken = default)
    {
        if (File.Exists(project.FilePath))
        {
            // ⛔ No fallback to SaveNewAsync here. Falling back would mean that a file we could not
            // parse — a half-written .blproj, a stray '&', an unexpected root element — gets
            // REPLACED by a seven-property skeleton built from the model. That is precisely the
            // destruction this method was changed to stop, reappearing exactly when the user can
            // least afford it. Refusing to save is recoverable; overwriting is not.
            await SavePreservingAsync(project, cancellationToken);
            return;
        }

        await SaveNewAsync(project, cancellationToken);
    }

    /// <summary>Builds a project file from the model. Only for a path that is not on disk yet.</summary>
    private static async Task SaveNewAsync(BasicLangProject project, CancellationToken cancellationToken = default)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("BasicLangProject",
                new XAttribute("Version", project.Version),

                // Global PropertyGroup
                new XElement("PropertyGroup",
                    new XElement("ProjectName", project.Name),
                    new XElement("OutputType", project.OutputType.ToString()),
                    new XElement("RootNamespace", project.RootNamespace),
                    new XElement("TargetBackend", project.TargetBackend.ToString()),
                    project.Language == ProjectLanguage.Cpp
                        ? new XElement("Language", project.Language.ToString())
                        : null,
                    // Emitted whenever C++ settings carry a standard — independent of Language, so a
                    // Language=BasicLang + TargetBackend=Cpp (mixed) project also round-trips it. A
                    // BasicLang project must NOT gain a <Language> element (kept above, unconditional
                    // on CppStandard) per design decision D8.
                    !string.IsNullOrEmpty(project.CppSettings?.CppStandard)
                        ? new XElement("CppStandard", project.CppSettings!.CppStandard)
                        : null,
                    // CppToolchain: same language-independent rule as CppStandard; a null
                    // toolchain (machine probe) must not gain an element on save.
                    !string.IsNullOrEmpty(project.CppSettings?.CppToolchain)
                        ? new XElement("CppToolchain", project.CppSettings!.CppToolchain)
                        : null,
                    // .NET framework/UI properties — emitted only when the model carries one, so a
                    // project that does not set them does not gain empty elements.
                    project.TargetFramework != null ? new XElement("TargetFramework", project.TargetFramework) : null,
                    project.AssemblyName != null ? new XElement("AssemblyName", project.AssemblyName) : null,
                    project.UseWindowsForms != null
                        ? new XElement("UseWindowsForms", project.UseWindowsForms.Value ? "true" : "false")
                        : null,
                    project.UseWpf != null
                        ? new XElement("UseWPF", project.UseWpf.Value ? "true" : "false")
                        : null,
                    project.ApplicationHighDpiMode != null
                        ? new XElement("ApplicationHighDpiMode", project.ApplicationHighDpiMode)
                        : null
                )
            )
        );

        var root = doc.Root!;

        // Add configuration-specific PropertyGroups
        foreach (var config in project.Configurations.Values)
        {
            root.Add(new XElement("PropertyGroup",
                new XAttribute("Condition", $"'$(Configuration)' == '{config.Name}'"),
                new XElement("OutputPath", config.OutputPath),
                new XElement("DebugSymbols", config.DebugSymbols.ToString().ToLower()),
                new XElement("Optimize", config.Optimize.ToString().ToLower()),
                config.DefineConstants != null ? new XElement("DefineConstants", config.DefineConstants) : null
            ));
        }

        // Add Compile items
        var compileItems = project.Items.Where(i => i.ItemType == ProjectItemType.Compile).ToList();
        if (compileItems.Any())
        {
            root.Add(new XElement("ItemGroup",
                compileItems.Select(i => new XElement("Compile", new XAttribute("Include", i.Include)))
            ));
        }

        // Add Content items
        var contentItems = project.Items.Where(i => i.ItemType == ProjectItemType.Content).ToList();
        if (contentItems.Any())
        {
            root.Add(new XElement("ItemGroup",
                contentItems.Select(i => new XElement("Content", new XAttribute("Include", i.Include)))
            ));
        }

        // Add Resource items
        var resourceItems = project.Items.Where(i => i.ItemType == ProjectItemType.Resource).ToList();
        if (resourceItems.Any())
        {
            root.Add(new XElement("ItemGroup",
                resourceItems.Select(i => new XElement("Resource", new XAttribute("Include", i.Include)))
            ));
        }

        // Add assembly References. Project-to-project references are emitted separately
        // below as <ProjectReference> — mixing them would rewrite a project reference as
        // an assembly reference and lose its path.
        var assemblyReferences = project.References.Where(r => !r.IsProjectReference).ToList();
        if (assemblyReferences.Any())
        {
            root.Add(new XElement("ItemGroup",
                assemblyReferences.Select(r => new XElement("Reference",
                    new XAttribute("Include", r.Name),
                    r.Path != null ? new XElement("HintPath", r.Path) : null
                ))
            ));
        }

        // Add ProjectReferences, re-emitting the raw Include path exactly as loaded so an
        // IDE save preserves what the compiler-side ProjectFile / LSP WorkspaceManager read.
        var projectReferences = project.References.Where(r => r.IsProjectReference).ToList();
        if (projectReferences.Any())
        {
            root.Add(new XElement("ItemGroup",
                projectReferences.Select(r => new XElement("ProjectReference",
                    new XAttribute("Include", r.Path ?? r.Name)
                ))
            ));
        }

        // Add PackageReferences (Include + Version attribute), so an IDE save preserves the
        // NuGet packages the compiler-side build restores instead of dropping them.
        if (project.PackageReferences.Any())
        {
            root.Add(new XElement("ItemGroup",
                project.PackageReferences.Select(p => new XElement("PackageReference",
                    new XAttribute("Include", p.Name),
                    new XAttribute("Version", p.Version)
                ))
            ));
        }

        // Add C++ items (IncludeDir / NativeLib / Define) so IDE saves round-trip them
        if (project.CppSettings != null &&
            (project.CppSettings.IncludeDirs.Any() ||
             project.CppSettings.NativeLibs.Any() ||
             project.CppSettings.Defines.Any()))
        {
            root.Add(new XElement("ItemGroup",
                project.CppSettings.IncludeDirs.Select(d => new XElement("IncludeDir", new XAttribute("Include", d))),
                project.CppSettings.NativeLibs.Select(l => new XElement("NativeLib", new XAttribute("Include", l))),
                project.CppSettings.Defines.Select(d => new XElement("Define", new XAttribute("Include", d)))
            ));
        }

        var directory = Path.GetDirectoryName(project.FilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(project.FilePath, doc.ToString(), cancellationToken);
    }

    // ================================================================================
    // Preserve-in-place save
    // ================================================================================

    /// <summary>
    /// Edits the existing project file in place, writing ONLY what changed.
    ///
    /// <para>The mechanism is a three-way comparison rather than a dirty flag: the file on disk is
    /// re-parsed into a <em>baseline</em> model, and a property is written only where the incoming
    /// model disagrees with that baseline. A property the IDE never touched therefore compares equal
    /// and its element is left exactly as the user wrote it — which is what makes an unmodified save
    /// byte-identical, comments and indentation included.</para>
    ///
    /// <para>Throws rather than falling back when the file cannot be understood — a malformed
    /// document, or a root element this serializer does not know. There is deliberately no recovery
    /// path that rebuilds the file from the model, because that is the data loss this class exists
    /// to prevent.</para>
    /// </summary>
    private async Task SavePreservingAsync(BasicLangProject project, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(project.FilePath, cancellationToken);
        var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        var baseline = await LoadAsync(project.FilePath, project.BackendAtLoad, cancellationToken);

        var root = doc.Root
            ?? throw new InvalidOperationException($"'{project.FilePath}' has no root element.");

        // A namespaced project (an old-style MSBuild xmlns) is not something LoadAsync parses, so
        // the baseline would be empty and every comparison would look like a change. Leaving the
        // file untouched is strictly better than rewriting it from a model that never read it.
        //
        // ⛔⛔ THROW, do not return. Returning here completed the save successfully and wrote
        // nothing — the IDE reported "saved", the user believed their change was persisted, and it
        // was gone at the next reload. A save that cannot happen has to say so; this is the same
        // rule as the unparseable-file path below, which already refuses rather than falling back
        // to a rebuild.
        if (root.Name.Namespace != XNamespace.None)
        {
            throw new InvalidOperationException(
                $"'{project.FilePath}' uses an XML namespace ({root.Name.Namespace}), which this " +
                "project format does not. Saving would have to rebuild the file from a model that " +
                "never read it, discarding everything the loader does not model — so nothing was " +
                "written. Remove the xmlns from the project element to edit it here.");
        }

        var changed = false;

        changed |= UpsertProperty(root, "ProjectName", project.Name, baseline.Name);
        changed |= UpsertProperty(root, "OutputType", project.OutputType.ToString(), baseline.OutputType.ToString());
        changed |= UpsertProperty(root, "RootNamespace", project.RootNamespace, baseline.RootNamespace);

        // TargetBackend compares against the FILE's element when it has one, and otherwise against
        // the value at load — never against a freshly-defaulted parse. LoadAsync seeds this property
        // from the IDE's configured backend when the element is absent, so comparing against a
        // null-seeded re-parse would read "file says nothing, model says Cpp" as a user edit and
        // inject <TargetBackend> into every project that omits it, on every save.
        changed |= UpsertProperty(root, "TargetBackend", project.TargetBackend.ToString(),
            RawProperty(root, "TargetBackend") ?? project.BackendAtLoad?.ToString());
        changed |= UpsertProperty(root, "TargetFramework", project.TargetFramework, baseline.TargetFramework);
        changed |= UpsertProperty(root, "AssemblyName", project.AssemblyName, baseline.AssemblyName);
        changed |= UpsertProperty(root, "UseWindowsForms", BoolText(project.UseWindowsForms), BoolText(baseline.UseWindowsForms));
        changed |= UpsertProperty(root, "UseWPF", BoolText(project.UseWpf), BoolText(baseline.UseWpf));
        changed |= UpsertProperty(root, "ApplicationHighDpiMode", project.ApplicationHighDpiMode, baseline.ApplicationHighDpiMode);

        // <Language> is emitted only for a C++ project: a BasicLang project must never gain one,
        // even when TargetBackend=Cpp makes it a native build (design decision D8).
        changed |= UpsertProperty(root, "Language",
            project.Language == ProjectLanguage.Cpp ? project.Language.ToString() : null,
            baseline.Language == ProjectLanguage.Cpp ? baseline.Language.ToString() : null);
        changed |= UpsertProperty(root, "CppStandard",
            NullIfEmpty(project.CppSettings?.CppStandard), NullIfEmpty(baseline.CppSettings?.CppStandard));
        changed |= UpsertProperty(root, "CppToolchain",
            NullIfEmpty(project.CppSettings?.CppToolchain), NullIfEmpty(baseline.CppSettings?.CppToolchain));

        changed |= ReconcileConfigurations(root, project, baseline);
        changed |= ReconcileIncludeItems(root, project, baseline);
        changed |= ReconcileAssemblyReferences(root, project, baseline);
        changed |= ReconcilePackageReferences(root, project, baseline);

        if (!changed)
        {
            // The file already says exactly what the model says. Not rewriting it is what keeps a
            // no-op save byte-identical, and it also avoids a pointless mtime bump that would make
            // every Build/F5 look like an edit to file watchers and source control.
            return;
        }

        var directory = Path.GetDirectoryName(project.FilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await WriteDocumentAsync(doc, project.FilePath, text, cancellationToken);
    }

    /// <summary>
    /// Serializes <paramref name="doc"/> over <paramref name="filePath"/>, reproducing the original
    /// file's prologue and line endings.
    ///
    /// <para>⛔ Not <c>XDocument.Save(TextWriter, SaveOptions)</c>. That calls
    /// <c>WriteStartDocument()</c> unconditionally and leaves <c>OmitXmlDeclaration</c> false, so it
    /// PREPENDS <c>&lt;?xml version="1.0" encoding="utf-8"?&gt;</c> even to a document that had none —
    /// and with formatting disabled, with no newline after it. A VS 2022 project would come back as
    /// <c>&lt;?xml…?&gt;&lt;Project Sdk="Microsoft.NET.Sdk"&gt;</c> on one line, which reads as exactly the
    /// corruption this class was changed to stop.</para>
    ///
    /// <para>⛔ Line endings need the same care. The XML parser is required to normalise CRLF to LF,
    /// so by the time a document is in memory every whitespace node holds bare LF; an
    /// <c>XmlWriter</c> then re-expands them using <c>NewLineChars</c>, which defaults to CRLF.
    /// Left alone, editing one property rewrites every line ending in the file and turns a
    /// one-element change into a whole-file diff. The original text decides instead.</para>
    /// </summary>
    private static async Task WriteDocumentAsync(
        XDocument doc, string filePath, string originalText, CancellationToken cancellationToken)
    {
        var usesCrLf = originalText.Contains("\r\n", StringComparison.Ordinal);

        var settings = new XmlWriterSettings
        {
            // Pairs with LoadOptions.PreserveWhitespace — together they reproduce the original
            // layout instead of re-indenting the whole document around one edited element.
            Indent = false,
            // Always omitted here; the original prologue is restored verbatim below instead. That
            // is more faithful than letting the writer regenerate one: it keeps the author's exact
            // attribute spelling AND whatever separated it from the root element, neither of which
            // survives a round-trip through XDocument.Declaration.
            OmitXmlDeclaration = true,
            NewLineHandling = usesCrLf ? NewLineHandling.Replace : NewLineHandling.None,
            NewLineChars = usesCrLf ? "\r\n" : "\n"
        };

        var body = new StringWriter();
        using (var writer = XmlWriter.Create(body, settings))
        {
            doc.WriteTo(writer);
        }

        var content = Recombine(originalText, body.ToString());

        // Write to a sibling temp file and move it into place: opening the real file for writing
        // truncates it before serialization begins, so a failure part-way through would leave a
        // truncated .blproj.
        // BOM-less UTF-8: XDocument.Save(path) would inject a BOM and corrupt the file, the same
        // reason BlprojReferenceWriter writes through an explicit UTF8Encoding(false).
        var tempPath = filePath + ".tmp" + Environment.ProcessId;
        try
        {
            await File.WriteAllTextAsync(tempPath, content, new UTF8Encoding(false), cancellationToken);
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
        }
    }

    /// <summary>
    /// Puts the original XML declaration back in front of the serialized body, separated exactly as
    /// it was in the original file.
    ///
    /// <para>Leading newlines are stripped from the body and re-supplied from the original rather
    /// than trusting either source alone: whether <c>XDocument</c> keeps the whitespace between the
    /// declaration and the root element as a document-level text node is an implementation detail,
    /// and getting it wrong in either direction produces a spurious blank line or a declaration
    /// jammed against the root. Taking the separator from the original file is true regardless.</para>
    /// </summary>
    private static string Recombine(string originalText, string body)
    {
        if (!originalText.StartsWith("<?xml", StringComparison.Ordinal))
        {
            return body;
        }

        var end = originalText.IndexOf("?>", StringComparison.Ordinal);
        if (end < 0)
        {
            return body;
        }

        end += 2;
        var separatorStart = end;
        while (end < originalText.Length && (originalText[end] == '\r' || originalText[end] == '\n'))
        {
            end++;
        }

        var declaration = originalText.Substring(0, separatorStart);
        var separator = originalText.Substring(separatorStart, end - separatorStart);
        return declaration + separator + body.TrimStart('\r', '\n');
    }

    /// <summary>
    /// The raw text of a property element as the FILE spells it, or null when absent.
    /// Reads the LAST occurrence, matching <see cref="LoadAsync"/>'s last-wins behaviour.
    /// </summary>
    private static string? RawProperty(XElement root, string name) =>
        root.Elements("PropertyGroup")
            .Where(g => string.IsNullOrEmpty(g.Attribute("Condition")?.Value))
            .SelectMany(g => g.Elements())
            .LastOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    /// <summary>Reads one property element by name, case-insensitively (MSBuild property names are).</summary>
    private static string? PropertyValue(XElement propertyGroup, string name) =>
        propertyGroup.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static string? BoolText(bool? value) => value == null ? null : value.Value ? "true" : "false";

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// Sets, adds or removes one property element in the global (condition-less) PropertyGroup —
    /// but only when <paramref name="desired"/> differs from <paramref name="onDisk"/>.
    /// </summary>
    private static bool UpsertProperty(XElement root, string name, string? desired, string? onDisk)
    {
        if (string.Equals(desired, onDisk, StringComparison.Ordinal))
        {
            return false;
        }

        var globalGroups = root.Elements("PropertyGroup")
            .Where(g => string.IsNullOrEmpty(g.Attribute("Condition")?.Value))
            .ToList();

        // Case-insensitive: MSBuild property names are case-insensitive but XLinq lookup is not,
        // and the spellings genuinely differ in the wild — the IDE's own template emits <UseWPF>
        // while <UseWpf> is just as valid. A case-sensitive match would append a second element
        // instead of updating the one already there.
        var matches = globalGroups
            .SelectMany(g => g.Elements())
            .Where(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (desired == null)
        {
            if (matches.Count == 0)
            {
                return false;
            }

            // Remove EVERY occurrence. Removing only one would leave the property still in force,
            // so the next save would compare unequal again and rewrite the file forever.
            foreach (var match in matches)
            {
                RemoveWithLeadingWhitespace(match);
            }

            return true;
        }

        if (matches.Count > 0)
        {
            // ⚠ Write to the LAST occurrence, because that is the one LoadAsync reads: its
            // PropertyGroup loop assigns unconditionally in document order, so a later duplicate
            // wins. Writing to the first would leave the file still resolving to the old value —
            // the model and the file would disagree after a save, and every subsequent save would
            // see a difference and rewrite the file again without ever converging.
            matches[^1].Value = desired;

            // Duplicates now disagree with each other; drop the shadowed ones so the file says
            // one thing.
            for (var i = 0; i < matches.Count - 1; i++)
            {
                RemoveWithLeadingWhitespace(matches[i]);
            }

            return true;
        }

        var target = globalGroups.FirstOrDefault();
        if (target == null)
        {
            target = new XElement("PropertyGroup");
            AddPreservingIndent(root, target);
        }

        AddPreservingIndent(target, new XElement(name, desired));
        return true;
    }

    /// <summary>Configuration-specific PropertyGroups, matched on the configuration name.</summary>
    private static bool ReconcileConfigurations(XElement root, BasicLangProject project, BasicLangProject baseline)
    {
        // ⛔ If the file carries a configuration condition this serializer cannot parse — the
        // VS-standard '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' is the common one — then
        // LoadAsync never read that group, and adding our own '$(Configuration)' == 'Debug' group
        // beside it would give the project two Debug groups that disagree. Leave configurations
        // entirely alone rather than half-understand them.
        var hasUnparseableCondition = root.Elements("PropertyGroup").Any(g =>
        {
            var condition = g.Attribute("Condition")?.Value;
            return !string.IsNullOrEmpty(condition) &&
                   condition.Contains("$(Configuration)", StringComparison.OrdinalIgnoreCase) &&
                   string.IsNullOrEmpty(ExtractConfigurationName(condition));
        });

        if (hasUnparseableCondition)
        {
            return false;
        }

        var changed = false;

        foreach (var (name, config) in project.Configurations)
        {
            if (baseline.Configurations.TryGetValue(name, out var before) &&
                before.OutputPath == config.OutputPath &&
                before.DebugSymbols == config.DebugSymbols &&
                before.Optimize == config.Optimize &&
                before.DefineConstants == config.DefineConstants)
            {
                continue;
            }

            // LAST, not first — LoadAsync's `Configurations[name] = config` lets a later duplicate
            // win, so the last group is the one the file actually resolves to.
            var group = root.Elements("PropertyGroup").LastOrDefault(g =>
                string.Equals(ExtractConfigurationName(g.Attribute("Condition")?.Value ?? ""), name,
                    StringComparison.OrdinalIgnoreCase));

            if (group == null)
            {
                group = new XElement("PropertyGroup",
                    new XAttribute("Condition", $"'$(Configuration)' == '{name}'"));
                AddPreservingIndent(root, group);
            }

            // ⚠ Each child is written only when the group already carries it or when the value
            // differs from what an ABSENT element parses as. LoadAsync reads a missing
            // <DebugSymbols> as false, so writing all four unconditionally would materialise
            // <DebugSymbols>false</DebugSymbols> into a Debug group that had been relying on the
            // toolchain default — silently turning debug symbols off because the user edited
            // DefineConstants. Same "absent is not false" rule the nullable properties on
            // BasicLangProject follow.
            SetChildIfMeaningful(group, "OutputPath", config.OutputPath, $"bin\\{name}");
            SetChildIfMeaningful(group, "DebugSymbols", config.DebugSymbols ? "true" : "false", "false");
            SetChildIfMeaningful(group, "Optimize", config.Optimize ? "true" : "false", "false");
            SetChild(group, "DefineConstants", config.DefineConstants);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Sets a configuration child when the group already has it, or when <paramref name="value"/>
    /// differs from <paramref name="absentMeans"/> — the value <see cref="LoadAsync"/> would infer
    /// if the element were missing. Leaves a deliberately-absent element absent.
    /// </summary>
    private static void SetChildIfMeaningful(XElement group, string name, string value, string absentMeans)
    {
        if (group.Element(name) != null || !string.Equals(value, absentMeans, StringComparison.OrdinalIgnoreCase))
        {
            SetChild(group, name, value);
        }
    }

    private static void SetChild(XElement parent, string name, string? value)
    {
        var existing = parent.Element(name);
        if (value == null)
        {
            if (existing != null)
            {
                RemoveWithLeadingWhitespace(existing);
            }
            return;
        }

        if (existing != null)
        {
            existing.Value = value;
        }
        else
        {
            AddPreservingIndent(parent, new XElement(name, value));
        }
    }

    /// <summary>
    /// Every item kind the model represents as a bare <c>Include</c>. Item kinds absent from this
    /// table — <c>EmbeddedResource</c>, <c>None</c>, <c>BasicLangCompile</c>, <c>ProjectCapability</c>
    /// and anything a future SDK adds — are not reconciled, so they are never removed.
    /// </summary>
    private static readonly (string Element, Func<BasicLangProject, IEnumerable<string>> Includes)[] IncludeItemKinds =
    {
        ("Compile",          p => p.Items.Where(i => i.ItemType == ProjectItemType.Compile).Select(i => i.Include)),
        ("Content",          p => p.Items.Where(i => i.ItemType == ProjectItemType.Content).Select(i => i.Include)),
        ("Resource",         p => p.Items.Where(i => i.ItemType == ProjectItemType.Resource).Select(i => i.Include)),
        ("IncludeDir",       p => p.CppSettings?.IncludeDirs ?? Enumerable.Empty<string>()),
        ("NativeLib",        p => p.CppSettings?.NativeLibs ?? Enumerable.Empty<string>()),
        ("Define",           p => p.CppSettings?.Defines ?? Enumerable.Empty<string>()),
        ("ProjectReference", p => p.References.Where(r => r.IsProjectReference).Select(r => r.Path ?? r.Name)),
    };

    private static bool ReconcileIncludeItems(XElement root, BasicLangProject project, BasicLangProject baseline)
    {
        var changed = false;

        foreach (var (element, includes) in IncludeItemKinds)
        {
            var desired = includes(project).ToList();
            var onDisk = includes(baseline).ToList();

            foreach (var gone in onDisk.Except(desired, StringComparer.OrdinalIgnoreCase))
            {
                var node = FindItem(root, element, gone);
                if (node != null)
                {
                    RemoveWithLeadingWhitespace(node);
                    changed = true;
                }
            }

            foreach (var added in desired.Except(onDisk, StringComparer.OrdinalIgnoreCase))
            {
                AddPreservingIndent(ItemGroupFor(root, element), new XElement(element, new XAttribute("Include", added)));
                changed = true;
            }
        }

        return changed;
    }

    private static bool ReconcileAssemblyReferences(XElement root, BasicLangProject project, BasicLangProject baseline)
    {
        var changed = false;
        var desired = project.References.Where(r => !r.IsProjectReference).ToList();
        var onDisk = baseline.References.Where(r => !r.IsProjectReference).ToList();

        foreach (var gone in onDisk.Where(b => desired.All(d => !string.Equals(d.Name, b.Name, StringComparison.OrdinalIgnoreCase))))
        {
            var node = FindItem(root, "Reference", gone.Name);
            if (node != null)
            {
                RemoveWithLeadingWhitespace(node);
                changed = true;
            }
        }

        foreach (var reference in desired)
        {
            var node = FindItem(root, "Reference", reference.Name);
            if (node == null)
            {
                node = new XElement("Reference", new XAttribute("Include", reference.Name));
                if (reference.Path != null)
                {
                    node.Add(new XElement("HintPath", reference.Path));
                }
                AddPreservingIndent(ItemGroupFor(root, "Reference"), node);
                changed = true;
                continue;
            }

            var before = onDisk.FirstOrDefault(r => string.Equals(r.Name, reference.Name, StringComparison.OrdinalIgnoreCase));
            if (before != null && before.Path != reference.Path)
            {
                SetChild(node, "HintPath", reference.Path);
                changed = true;
            }
        }

        return changed;
    }

    private static bool ReconcilePackageReferences(XElement root, BasicLangProject project, BasicLangProject baseline)
    {
        var changed = false;

        foreach (var gone in baseline.PackageReferences.Where(b =>
                     project.PackageReferences.All(p => !string.Equals(p.Name, b.Name, StringComparison.OrdinalIgnoreCase))))
        {
            var node = FindItem(root, "PackageReference", gone.Name);
            if (node != null)
            {
                RemoveWithLeadingWhitespace(node);
                changed = true;
            }
        }

        foreach (var package in project.PackageReferences)
        {
            var node = FindItem(root, "PackageReference", package.Name);
            if (node == null)
            {
                AddPreservingIndent(ItemGroupFor(root, "PackageReference"),
                    new XElement("PackageReference",
                        new XAttribute("Include", package.Name),
                        new XAttribute("Version", package.Version)));
                changed = true;
                continue;
            }

            var before = baseline.PackageReferences.FirstOrDefault(p =>
                string.Equals(p.Name, package.Name, StringComparison.OrdinalIgnoreCase));
            if (before == null || before.Version == package.Version)
            {
                continue;
            }

            // Version may live on an attribute or a child element; update whichever the file uses.
            if (node.Element("Version") != null)
            {
                node.Element("Version")!.Value = package.Version;
            }
            else
            {
                node.SetAttributeValue("Version", package.Version);
            }
            changed = true;
        }

        return changed;
    }

    private static XElement? FindItem(XElement root, string element, string include) =>
        root.Elements("ItemGroup")
            .SelectMany(g => g.Elements(element))
            .FirstOrDefault(e => string.Equals(e.Attribute("Include")?.Value, include, StringComparison.OrdinalIgnoreCase));

    /// <summary>The ItemGroup that already holds this item kind, else the last one, else a new one.</summary>
    private static XElement ItemGroupFor(XElement root, string element)
    {
        var groups = root.Elements("ItemGroup").ToList();
        var group = groups.FirstOrDefault(g => g.Elements(element).Any()) ?? groups.LastOrDefault();
        if (group != null)
        {
            return group;
        }

        group = new XElement("ItemGroup");
        AddPreservingIndent(root, group);
        return group;
    }

    /// <summary>
    /// Appends a child, repeating the whitespace that precedes the existing last child so the new
    /// element lands at the surrounding indentation instead of being jammed against its sibling.
    /// Only cosmetic — but a save that reflows the user's project file reads as corruption.
    /// </summary>
    private static void AddPreservingIndent(XElement parent, XElement child)
    {
        var lastElement = parent.Elements().LastOrDefault();
        if (lastElement == null)
        {
            parent.Add(child);
            return;
        }

        var indent = (lastElement.PreviousNode as XText)?.Value;
        lastElement.AddAfterSelf(child);
        if (indent != null)
        {
            child.AddBeforeSelf(new XText(indent));
        }
    }

    /// <summary>
    /// Removes an element together with the indentation in front of it, so no blank line is left
    /// behind. Only whitespace-only text is removed — a text node with content would be part of
    /// mixed content and is never ours to delete.
    /// </summary>
    private static void RemoveWithLeadingWhitespace(XElement element)
    {
        var leading = element.PreviousNode as XText;
        element.Remove();
        if (leading != null && string.IsNullOrWhiteSpace(leading.Value))
        {
            leading.Remove();
        }
    }

    private static string? ExtractConfigurationName(string condition)
    {
        // Parse: '$(Configuration)' == 'Debug'
        var match = System.Text.RegularExpressions.Regex.Match(
            condition,
            @"'\$\(Configuration\)'\s*==\s*'(\w+)'",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }
}
