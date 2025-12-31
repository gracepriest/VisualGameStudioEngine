using System.Xml.Linq;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Serialization;

public class ProjectSerializer
{
    private const string ProjectVersion = "1.0";

    public async Task<BasicLangProject> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var doc = XDocument.Parse(content);
        var root = doc.Root;

        if (root == null || root.Name != "BasicLangProject")
        {
            throw new InvalidOperationException("Invalid project file format");
        }

        var project = new BasicLangProject
        {
            FilePath = filePath,
            Version = root.Attribute("Version")?.Value ?? ProjectVersion
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

    public async Task SaveAsync(BasicLangProject project, CancellationToken cancellationToken = default)
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
                    new XElement("TargetBackend", project.TargetBackend.ToString())
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

        // Add References
        if (project.References.Any())
        {
            root.Add(new XElement("ItemGroup",
                project.References.Select(r => new XElement("Reference",
                    new XAttribute("Include", r.Name),
                    r.Path != null ? new XElement("HintPath", r.Path) : null
                ))
            ));
        }

        var directory = Path.GetDirectoryName(project.FilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(project.FilePath, doc.ToString(), cancellationToken);
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
