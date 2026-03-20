using System.Xml.Linq;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Serialization;

public class SolutionSerializer
{
    /// <summary>
    /// Loads a .blsln XML solution file and returns a BasicLangSolution model.
    /// Resolves relative project paths to absolute paths using the solution directory.
    /// </summary>
    public async Task<BasicLangSolution> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var doc = XDocument.Parse(content);
        var root = doc.Root;

        if (root == null || root.Name.LocalName != "BasicLangSolution")
        {
            throw new InvalidOperationException("Invalid solution file format: root element must be <BasicLangSolution>");
        }

        var solution = new BasicLangSolution
        {
            FilePath = filePath,
            Version = root.Attribute("Version")?.Value ?? "1.0"
        };

        var solutionDir = solution.SolutionDirectory;

        // Parse PropertyGroup
        var propertyGroup = root.Element("PropertyGroup");
        if (propertyGroup != null)
        {
            solution.SolutionName = propertyGroup.Element("SolutionName")?.Value
                ?? Path.GetFileNameWithoutExtension(filePath);
            solution.DefaultProject = propertyGroup.Element("DefaultProject")?.Value;
        }
        else
        {
            solution.SolutionName = Path.GetFileNameWithoutExtension(filePath);
        }

        // Parse Projects
        var projectsElement = root.Element("Projects");
        if (projectsElement != null)
        {
            foreach (var projElement in projectsElement.Elements("Project"))
            {
                var project = new SolutionProject
                {
                    Name = projElement.Attribute("Name")?.Value ?? "",
                    RelativePath = projElement.Attribute("Path")?.Value ?? "",
                    Type = projElement.Attribute("Type")?.Value ?? "Exe"
                };

                // Resolve absolute path from solution directory
                if (!string.IsNullOrEmpty(project.RelativePath) && !string.IsNullOrEmpty(solutionDir))
                {
                    project.AbsolutePath = Path.GetFullPath(
                        Path.Combine(solutionDir, project.RelativePath));
                }

                // Parse ProjectReference child elements
                foreach (var refElement in projElement.Elements("ProjectReference"))
                {
                    var refName = refElement.Value?.Trim();
                    if (!string.IsNullOrEmpty(refName))
                    {
                        project.ProjectReferences.Add(refName);
                    }
                }

                solution.Projects.Add(project);
            }
        }

        // Parse Folders (optional section)
        var foldersElement = root.Element("Folders");
        if (foldersElement != null)
        {
            foreach (var folderElement in foldersElement.Elements("Folder"))
            {
                var folder = new SolutionFolder
                {
                    Name = folderElement.Attribute("Name")?.Value ?? ""
                };

                var idAttr = folderElement.Attribute("Id")?.Value;
                if (!string.IsNullOrEmpty(idAttr) && Guid.TryParse(idAttr, out var folderId))
                {
                    folder.Id = folderId;
                }

                var parentAttr = folderElement.Attribute("ParentId")?.Value;
                if (!string.IsNullOrEmpty(parentAttr) && Guid.TryParse(parentAttr, out var parentId))
                {
                    folder.ParentId = parentId;
                }

                solution.Folders.Add(folder);
            }
        }

        // Parse GlobalProperties (optional section)
        var globalPropsElement = root.Element("GlobalProperties");
        if (globalPropsElement != null)
        {
            foreach (var propElement in globalPropsElement.Elements())
            {
                solution.GlobalProperties[propElement.Name.LocalName] = propElement.Value;
            }
        }

        return solution;
    }

    /// <summary>
    /// Saves a BasicLangSolution model to a .blsln XML file.
    /// </summary>
    public async Task SaveAsync(BasicLangSolution solution, CancellationToken cancellationToken = default)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("BasicLangSolution",
                new XAttribute("Version", solution.Version),

                // PropertyGroup
                new XElement("PropertyGroup",
                    new XElement("SolutionName", solution.SolutionName),
                    !string.IsNullOrEmpty(solution.DefaultProject)
                        ? new XElement("DefaultProject", solution.DefaultProject)
                        : null
                ),

                // Projects
                new XElement("Projects",
                    solution.Projects.Select(p =>
                    {
                        var projElement = new XElement("Project",
                            new XAttribute("Name", p.Name),
                            new XAttribute("Path", p.RelativePath),
                            new XAttribute("Type", p.Type));

                        foreach (var reference in p.ProjectReferences)
                        {
                            projElement.Add(new XElement("ProjectReference", reference));
                        }

                        return projElement;
                    })
                )
            )
        );

        var root = doc.Root!;

        // Add Folders if any
        if (solution.Folders.Count > 0)
        {
            root.Add(new XElement("Folders",
                solution.Folders.Select(f =>
                {
                    var folderElement = new XElement("Folder",
                        new XAttribute("Id", f.Id.ToString()),
                        new XAttribute("Name", f.Name));

                    if (f.ParentId.HasValue)
                    {
                        folderElement.Add(new XAttribute("ParentId", f.ParentId.Value.ToString()));
                    }

                    return folderElement;
                })
            ));
        }

        // Add GlobalProperties if any
        if (solution.GlobalProperties.Count > 0)
        {
            root.Add(new XElement("GlobalProperties",
                solution.GlobalProperties.Select(kvp =>
                    new XElement(kvp.Key, kvp.Value))
            ));
        }

        var directory = Path.GetDirectoryName(solution.FilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(solution.FilePath, doc.ToString(), cancellationToken);
    }
}
