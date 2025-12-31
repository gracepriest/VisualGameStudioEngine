using System.Text.Json;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Serialization;

public class SolutionSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<BasicLangSolution> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var data = JsonSerializer.Deserialize<SolutionData>(json, JsonOptions);

        if (data == null)
        {
            throw new InvalidOperationException("Invalid solution file format");
        }

        var solution = new BasicLangSolution
        {
            FilePath = filePath,
            Name = data.Name ?? Path.GetFileNameWithoutExtension(filePath),
            Version = data.Version ?? "1.0"
        };

        if (data.Projects != null)
        {
            foreach (var projData in data.Projects)
            {
                solution.Projects.Add(new SolutionProject
                {
                    Id = projData.Id,
                    Name = projData.Name ?? "",
                    RelativePath = projData.RelativePath ?? "",
                    IsStartupProject = projData.IsStartupProject
                });
            }
        }

        if (data.Folders != null)
        {
            foreach (var folderData in data.Folders)
            {
                solution.Folders.Add(new SolutionFolder
                {
                    Id = folderData.Id,
                    Name = folderData.Name ?? "",
                    ParentId = folderData.ParentId,
                    ProjectIds = folderData.ProjectIds ?? new List<Guid>()
                });
            }
        }

        if (data.GlobalProperties != null)
        {
            foreach (var prop in data.GlobalProperties)
            {
                solution.GlobalProperties[prop.Key] = prop.Value;
            }
        }

        return solution;
    }

    public async Task SaveAsync(BasicLangSolution solution, CancellationToken cancellationToken = default)
    {
        var data = new SolutionData
        {
            Name = solution.Name,
            Version = solution.Version,
            Projects = solution.Projects.Select(p => new SolutionProjectData
            {
                Id = p.Id,
                Name = p.Name,
                RelativePath = p.RelativePath,
                IsStartupProject = p.IsStartupProject
            }).ToList(),
            Folders = solution.Folders.Select(f => new SolutionFolderData
            {
                Id = f.Id,
                Name = f.Name,
                ParentId = f.ParentId,
                ProjectIds = f.ProjectIds
            }).ToList(),
            GlobalProperties = solution.GlobalProperties
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);

        var directory = Path.GetDirectoryName(solution.FilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(solution.FilePath, json, cancellationToken);
    }

    private class SolutionData
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public List<SolutionProjectData>? Projects { get; set; }
        public List<SolutionFolderData>? Folders { get; set; }
        public Dictionary<string, string>? GlobalProperties { get; set; }
    }

    private class SolutionProjectData
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? RelativePath { get; set; }
        public bool IsStartupProject { get; set; }
    }

    private class SolutionFolderData
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public Guid? ParentId { get; set; }
        public List<Guid>? ProjectIds { get; set; }
    }
}
