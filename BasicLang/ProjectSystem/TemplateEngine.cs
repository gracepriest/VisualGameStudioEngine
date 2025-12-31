using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>
    /// Template engine for creating new BasicLang projects
    /// </summary>
    public class TemplateEngine
    {
        private readonly string _templatesPath;
        private readonly Dictionary<string, ProjectTemplate> _templates;

        public TemplateEngine()
        {
            // Templates are stored in the application directory or user's .basiclang folder
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            _templatesPath = Path.Combine(appDir, "templates");

            // Also check user's home directory
            var userTemplatesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".basiclang", "templates");

            _templates = new Dictionary<string, ProjectTemplate>(StringComparer.OrdinalIgnoreCase);

            // Register built-in templates
            RegisterBuiltInTemplates();

            // Load custom templates from disk
            if (Directory.Exists(_templatesPath))
                LoadTemplatesFromDirectory(_templatesPath);
            if (Directory.Exists(userTemplatesPath))
                LoadTemplatesFromDirectory(userTemplatesPath);
        }

        private void RegisterBuiltInTemplates()
        {
            // Console application template
            _templates["console"] = new ProjectTemplate
            {
                Name = "console",
                DisplayName = "Console Application",
                Description = "A project for creating a command-line application",
                ShortName = "console",
                DefaultProjectName = "ConsoleApp",
                Files = new Dictionary<string, string>
                {
                    ["{{ProjectName}}.blproj"] = @"<Project Sdk=""BasicLang.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>{{ProjectName}}</RootNamespace>
    <AssemblyName>{{ProjectName}}</AssemblyName>
  </PropertyGroup>
</Project>",
                    ["Program.bas"] = @"' {{ProjectName}} - Console Application
' Created: {{Date}}

Sub Main()
    PrintLine ""Hello, World!""
End Sub
",
                    [".gitignore"] = @"# Build results
bin/
obj/

# IDE files
.vs/
*.user

# Packages
packages/
"
                }
            };

            // Class library template
            _templates["classlib"] = new ProjectTemplate
            {
                Name = "classlib",
                DisplayName = "Class Library",
                Description = "A project for creating a class library",
                ShortName = "classlib",
                DefaultProjectName = "ClassLibrary",
                Files = new Dictionary<string, string>
                {
                    ["{{ProjectName}}.blproj"] = @"<Project Sdk=""BasicLang.Sdk"">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>{{ProjectName}}</RootNamespace>
    <AssemblyName>{{ProjectName}}</AssemblyName>
  </PropertyGroup>
</Project>",
                    ["Class1.bas"] = @"' {{ProjectName}} - Class Library
' Created: {{Date}}

Namespace {{ProjectName}}
    Public Class Class1
        Public Function GetMessage() As String
            Return ""Hello from {{ProjectName}}!""
        End Function
    End Class
End Namespace
"
                }
            };

            // Empty project template
            _templates["empty"] = new ProjectTemplate
            {
                Name = "empty",
                DisplayName = "Empty Project",
                Description = "An empty BasicLang project",
                ShortName = "empty",
                DefaultProjectName = "EmptyProject",
                Files = new Dictionary<string, string>
                {
                    ["{{ProjectName}}.blproj"] = @"<Project Sdk=""BasicLang.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>{{ProjectName}}</RootNamespace>
    <AssemblyName>{{ProjectName}}</AssemblyName>
  </PropertyGroup>
</Project>"
                }
            };

            // Web API template (minimal)
            _templates["webapi"] = new ProjectTemplate
            {
                Name = "webapi",
                DisplayName = "Web API",
                Description = "A project for creating a RESTful HTTP API",
                ShortName = "webapi",
                DefaultProjectName = "WebApi",
                Files = new Dictionary<string, string>
                {
                    ["{{ProjectName}}.blproj"] = @"<Project Sdk=""BasicLang.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>{{ProjectName}}</RootNamespace>
    <AssemblyName>{{ProjectName}}</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Microsoft.AspNetCore.App"" Version=""8.0.0"" />
  </ItemGroup>
</Project>",
                    ["Program.bas"] = @"' {{ProjectName}} - Web API
' Created: {{Date}}

Using Microsoft.AspNetCore.Builder
Using Microsoft.Extensions.Hosting

Sub Main()
    Dim builder = WebApplication.CreateBuilder()
    Dim app = builder.Build()

    app.MapGet(""/"", Function() ""Hello from {{ProjectName}}!"")
    app.MapGet(""/api/hello"", Function() New With { .message = ""Hello, World!"" })

    app.Run()
End Sub
"
                }
            };

            // Solution template
            _templates["sln"] = new ProjectTemplate
            {
                Name = "sln",
                DisplayName = "Solution File",
                Description = "Create a solution file for multiple projects",
                ShortName = "sln",
                DefaultProjectName = "Solution",
                Files = new Dictionary<string, string>
                {
                    ["{{ProjectName}}.blsln"] = @"BasicLang Solution File, Format Version 1.0
# {{ProjectName}}
# Created: {{Date}}

# Add projects with:
# Project ""{project-guid}"" = ""ProjectName"", ""path/to/project.blproj""
"
                }
            };

            // Test project template
            _templates["test"] = new ProjectTemplate
            {
                Name = "test",
                DisplayName = "Test Project",
                Description = "A project for creating unit tests",
                ShortName = "test",
                DefaultProjectName = "TestProject",
                Files = new Dictionary<string, string>
                {
                    ["{{ProjectName}}.blproj"] = @"<Project Sdk=""BasicLang.Sdk"">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>{{ProjectName}}</RootNamespace>
    <AssemblyName>{{ProjectName}}</AssemblyName>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""xunit"" Version=""2.6.0"" />
    <PackageReference Include=""xunit.runner.visualstudio"" Version=""2.5.0"" />
  </ItemGroup>
</Project>",
                    ["UnitTest1.bas"] = @"' {{ProjectName}} - Unit Tests
' Created: {{Date}}

Using Xunit

Namespace {{ProjectName}}
    Public Class UnitTest1
        <Fact>
        Public Sub Test1()
            ' Arrange
            Dim expected = 4

            ' Act
            Dim actual = 2 + 2

            ' Assert
            Assert.Equal(expected, actual)
        End Sub
    End Class
End Namespace
"
                }
            };
        }

        private void LoadTemplatesFromDirectory(string path)
        {
            if (!Directory.Exists(path))
                return;

            foreach (var templateDir in Directory.GetDirectories(path))
            {
                var templateName = Path.GetFileName(templateDir);
                var templateJsonPath = Path.Combine(templateDir, "template.json");

                if (File.Exists(templateJsonPath))
                {
                    try
                    {
                        var template = LoadTemplateFromJson(templateJsonPath);
                        template.SourceDirectory = templateDir;
                        _templates[template.ShortName] = template;
                    }
                    catch
                    {
                        // Skip invalid templates
                    }
                }
            }
        }

        private ProjectTemplate LoadTemplateFromJson(string jsonPath)
        {
            var json = File.ReadAllText(jsonPath);
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return System.Text.Json.JsonSerializer.Deserialize<ProjectTemplate>(json, options);
        }

        /// <summary>
        /// List all available templates
        /// </summary>
        public void ListTemplates()
        {
            Console.WriteLine("Available templates:");
            Console.WriteLine();
            Console.WriteLine($"{"Template",-15} {"Short Name",-12} {"Description"}");
            Console.WriteLine(new string('-', 70));

            foreach (var template in _templates.Values.OrderBy(t => t.Name))
            {
                Console.WriteLine($"{template.DisplayName,-15} {template.ShortName,-12} {template.Description}");
            }

            Console.WriteLine();
            Console.WriteLine("Usage: basiclang new <template> [options]");
            Console.WriteLine("       basiclang new console -n MyApp");
            Console.WriteLine("       basiclang new classlib --name MyLibrary --output ./libs");
        }

        /// <summary>
        /// Create a new project from a template
        /// </summary>
        public bool CreateProject(string templateName, string projectName = null, string outputPath = null)
        {
            if (!_templates.TryGetValue(templateName, out var template))
            {
                Console.WriteLine($"Template '{templateName}' not found.");
                Console.WriteLine("Use 'basiclang new --list' to see available templates.");
                return false;
            }

            // Use default project name if not specified
            projectName ??= template.DefaultProjectName;

            // Use current directory if output not specified
            outputPath ??= Path.Combine(Directory.GetCurrentDirectory(), projectName);

            // Create output directory
            if (Directory.Exists(outputPath))
            {
                Console.WriteLine($"Directory '{outputPath}' already exists.");
                return false;
            }

            Directory.CreateDirectory(outputPath);

            // Template variables
            var variables = new Dictionary<string, string>
            {
                ["ProjectName"] = projectName,
                ["Date"] = DateTime.Now.ToString("yyyy-MM-dd"),
                ["Year"] = DateTime.Now.Year.ToString(),
                ["Author"] = Environment.UserName,
                ["Guid"] = Guid.NewGuid().ToString()
            };

            // Create files from template
            if (template.Files != null)
            {
                // Use inline file definitions
                foreach (var file in template.Files)
                {
                    var fileName = ReplaceVariables(file.Key, variables);
                    var content = ReplaceVariables(file.Value, variables);
                    var filePath = Path.Combine(outputPath, fileName);

                    var fileDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(fileDir))
                        Directory.CreateDirectory(fileDir);

                    File.WriteAllText(filePath, content);
                }
            }
            else if (!string.IsNullOrEmpty(template.SourceDirectory))
            {
                // Copy files from template directory
                CopyTemplateDirectory(template.SourceDirectory, outputPath, variables);
            }

            Console.WriteLine($"Created new {template.DisplayName} at {outputPath}");
            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine($"  cd {projectName}");
            Console.WriteLine($"  basiclang build");
            Console.WriteLine($"  basiclang run");

            return true;
        }

        private void CopyTemplateDirectory(string sourceDir, string targetDir, Dictionary<string, string> variables)
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                // Skip template.json
                if (Path.GetFileName(file) == "template.json")
                    continue;

                var relativePath = Path.GetRelativePath(sourceDir, file);
                var targetPath = Path.Combine(targetDir, ReplaceVariables(relativePath, variables));

                var targetFileDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetFileDir))
                    Directory.CreateDirectory(targetFileDir);

                var content = File.ReadAllText(file);
                content = ReplaceVariables(content, variables);
                File.WriteAllText(targetPath, content);
            }
        }

        private string ReplaceVariables(string input, Dictionary<string, string> variables)
        {
            var result = input;
            foreach (var kvp in variables)
            {
                result = Regex.Replace(result, @"\{\{" + kvp.Key + @"\}\}", kvp.Value, RegexOptions.IgnoreCase);
            }
            return result;
        }

        /// <summary>
        /// Get a template by name
        /// </summary>
        public ProjectTemplate GetTemplate(string name)
        {
            _templates.TryGetValue(name, out var template);
            return template;
        }

        /// <summary>
        /// Get all available templates
        /// </summary>
        public IEnumerable<ProjectTemplate> GetAllTemplates()
        {
            return _templates.Values;
        }
    }

    /// <summary>
    /// Represents a project template
    /// </summary>
    public class ProjectTemplate
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string ShortName { get; set; }
        public string DefaultProjectName { get; set; }
        public string SourceDirectory { get; set; }
        public Dictionary<string, string> Files { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }
}
