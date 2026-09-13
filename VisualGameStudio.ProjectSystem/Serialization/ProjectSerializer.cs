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
                var targetFramework = propertyGroup.Element("TargetFramework")?.Value.Trim();
                if (!string.IsNullOrEmpty(targetFramework))
                {
                    project.TargetFramework = targetFramework;
                }

                var assemblyName = propertyGroup.Element("AssemblyName")?.Value.Trim();
                if (!string.IsNullOrEmpty(assemblyName))
                {
                    project.AssemblyName = assemblyName;
                }

                if (bool.TryParse(propertyGroup.Element("UseWindowsForms")?.Value, out var useWinForms))
                {
                    project.UseWindowsForms = useWinForms;
                }

                if (bool.TryParse(propertyGroup.Element("UseWpf")?.Value, out var useWpf))
                {
                    project.UseWpf = useWpf;
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
    /// model value disagrees with what the file says (<see cref="TrySavePreservingAsync"/>).
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
        if (File.Exists(project.FilePath) &&
            await TrySavePreservingAsync(project, cancellationToken))
        {
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
                        ? new XElement("UseWpf", project.UseWpf.Value ? "true" : "false")
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
    /// <para>Returns false when the document cannot be understood well enough to edit safely, so the
    /// caller can fall back to building a fresh file.</para>
    /// </summary>
    private async Task<bool> TrySavePreservingAsync(BasicLangProject project, CancellationToken cancellationToken)
    {
        XDocument doc;
        BasicLangProject baseline;
        try
        {
            var text = await File.ReadAllTextAsync(project.FilePath, cancellationToken);
            doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            if (doc.Root == null)
            {
                return false;
            }

            baseline = await LoadAsync(project.FilePath, null, cancellationToken);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or IOException)
        {
            return false;
        }

        var root = doc.Root!;

        // A namespaced project (an old-style MSBuild xmlns) is not something LoadAsync parses, so
        // the baseline would be empty and every comparison would look like a change. Leaving the
        // file untouched is strictly better than rewriting it from a model that never read it.
        if (root.Name.Namespace != XNamespace.None)
        {
            return true;
        }

        var changed = false;

        changed |= UpsertProperty(root, "ProjectName", project.Name, baseline.Name);
        changed |= UpsertProperty(root, "OutputType", project.OutputType.ToString(), baseline.OutputType.ToString());
        changed |= UpsertProperty(root, "RootNamespace", project.RootNamespace, baseline.RootNamespace);
        changed |= UpsertProperty(root, "TargetBackend", project.TargetBackend.ToString(), baseline.TargetBackend.ToString());
        changed |= UpsertProperty(root, "TargetFramework", project.TargetFramework, baseline.TargetFramework);
        changed |= UpsertProperty(root, "AssemblyName", project.AssemblyName, baseline.AssemblyName);
        changed |= UpsertProperty(root, "UseWindowsForms", BoolText(project.UseWindowsForms), BoolText(baseline.UseWindowsForms));
        changed |= UpsertProperty(root, "UseWpf", BoolText(project.UseWpf), BoolText(baseline.UseWpf));

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
            return true;
        }

        var directory = Path.GetDirectoryName(project.FilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // BOM-less UTF-8: XDocument.Save(path) would inject a BOM and corrupt the file, the same
        // reason BlprojReferenceWriter writes through an explicit UTF8Encoding(false).
        // DisableFormatting pairs with PreserveWhitespace above — together they reproduce the
        // original layout instead of re-indenting the whole document around one edited element.
        await using var stream = new FileStream(project.FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        doc.Save(writer, SaveOptions.DisableFormatting);
        await writer.FlushAsync(cancellationToken);
        return true;
    }

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

        var existing = globalGroups.SelectMany(g => g.Elements(name)).FirstOrDefault();

        if (desired == null)
        {
            if (existing == null)
            {
                return false;
            }

            RemoveWithLeadingWhitespace(existing);
            return true;
        }

        if (existing != null)
        {
            existing.Value = desired;
            return true;
        }

        var target = globalGroups.FirstOrDefault();
        if (target == null)
        {
            target = new XElement("PropertyGroup");
            root.Add(target);
        }

        AddPreservingIndent(target, new XElement(name, desired));
        return true;
    }

    /// <summary>Configuration-specific PropertyGroups, matched on the configuration name.</summary>
    private static bool ReconcileConfigurations(XElement root, BasicLangProject project, BasicLangProject baseline)
    {
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

            var group = root.Elements("PropertyGroup").FirstOrDefault(g =>
                string.Equals(ExtractConfigurationName(g.Attribute("Condition")?.Value ?? ""), name,
                    StringComparison.OrdinalIgnoreCase));

            if (group == null)
            {
                group = new XElement("PropertyGroup",
                    new XAttribute("Condition", $"'$(Configuration)' == '{name}'"));
                AddPreservingIndent(root, group);
            }

            SetChild(group, "OutputPath", config.OutputPath);
            SetChild(group, "DebugSymbols", config.DebugSymbols ? "true" : "false");
            SetChild(group, "Optimize", config.Optimize ? "true" : "false");
            SetChild(group, "DefineConstants", config.DefineConstants);
            changed = true;
        }

        return changed;
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

    /// <summary>Removes an element together with the whitespace in front of it, so no blank line is left behind.</summary>
    private static void RemoveWithLeadingWhitespace(XElement element)
    {
        var leading = element.PreviousNode as XText;
        element.Remove();
        leading?.Remove();
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
