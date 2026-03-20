using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Implements the task runner system. Reads tasks from .vgs/tasks.json,
/// provides auto-detected defaults, and executes tasks via Process.Start.
/// </summary>
public class TaskRunnerService : ITaskRunnerService
{
    private readonly IOutputService _outputService;
    private TasksConfig? _cachedConfig;
    private string? _cachedProjectPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public event EventHandler<TaskEventArgs>? TaskStarted;
    public event EventHandler<TaskEventArgs>? TaskCompleted;
    public event EventHandler<TaskOutputEventArgs>? TaskOutput;

    public TaskRunnerService(IOutputService outputService)
    {
        _outputService = outputService;
    }

    public string GetTasksFilePath(string projectPath)
    {
        return Path.Combine(projectPath, ".vgs", "tasks.json");
    }

    public async Task<TasksConfig> LoadTasksAsync(string projectPath)
    {
        var filePath = GetTasksFilePath(projectPath);

        if (File.Exists(filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var config = JsonSerializer.Deserialize<TasksConfig>(json, JsonOptions);
                if (config != null)
                {
                    _cachedConfig = config;
                    _cachedProjectPath = projectPath;
                    return config;
                }
            }
            catch (Exception ex)
            {
                _outputService.WriteLine($"Error loading tasks.json: {ex.Message}");
            }
        }

        // Return default config with auto-detected tasks
        var defaultConfig = CreateDefaultConfig(projectPath);
        _cachedConfig = defaultConfig;
        _cachedProjectPath = projectPath;
        return defaultConfig;
    }

    public async Task SaveTasksAsync(string projectPath, TasksConfig config)
    {
        var filePath = GetTasksFilePath(projectPath);
        var dir = Path.GetDirectoryName(filePath)!;

        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Don't save auto-detected tasks
        var configToSave = new TasksConfig
        {
            Version = config.Version,
            Tasks = config.Tasks.Where(t => !t.IsAutoDetected).ToList()
        };

        var json = JsonSerializer.Serialize(configToSave, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        _cachedConfig = config;
        _cachedProjectPath = projectPath;
    }

    public async Task<int> RunTaskAsync(TaskDefinition task, string workspaceFolder, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var args = new TaskEventArgs(task);
        TaskStarted?.Invoke(this, args);

        _outputService.WriteLine($"\r\n> Executing task: {task.Label}");
        _outputService.WriteLine($"> {task.GetFullCommand()}\r\n");

        try
        {
            var command = ResolveVariables(task.Command, workspaceFolder);
            var taskArgs = task.Args?.Select(a => ResolveVariables(a, workspaceFolder)).ToArray();
            var cwd = task.Cwd != null ? ResolveVariables(task.Cwd, workspaceFolder) : workspaceFolder;

            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = cwd
            };

            if (task.Type == "shell")
            {
                // Run through the system shell
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    startInfo.FileName = "cmd.exe";
                    var fullCmd = taskArgs != null && taskArgs.Length > 0
                        ? $"{command} {string.Join(" ", taskArgs)}"
                        : command;
                    startInfo.Arguments = $"/c {fullCmd}";
                }
                else
                {
                    startInfo.FileName = "/bin/bash";
                    var fullCmd = taskArgs != null && taskArgs.Length > 0
                        ? $"{command} {string.Join(" ", taskArgs)}"
                        : command;
                    startInfo.Arguments = $"-c \"{fullCmd.Replace("\"", "\\\"")}\"";
                }
            }
            else
            {
                // Run directly as a process
                startInfo.FileName = command;
                if (taskArgs != null && taskArgs.Length > 0)
                {
                    startInfo.Arguments = string.Join(" ", taskArgs);
                }
            }

            // Set environment variables
            if (task.Env != null)
            {
                foreach (var (key, value) in task.Env)
                {
                    startInfo.Environment[key] = ResolveVariables(value, workspaceFolder);
                }
            }

            using var process = new Process { StartInfo = startInfo };
            process.EnableRaisingEvents = true;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _outputService.WriteLine(e.Data);
                    TaskOutput?.Invoke(this, new TaskOutputEventArgs(task, e.Data));
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _outputService.WriteLine(e.Data);
                    TaskOutput?.Invoke(this, new TaskOutputEventArgs(task, e.Data, isError: true));
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for completion or cancellation
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                _outputService.WriteLine("\r\n> Task cancelled.");
                args.ExitCode = -1;
                args.Duration = stopwatch.Elapsed;
                TaskCompleted?.Invoke(this, args);
                return -1;
            }

            var exitCode = process.ExitCode;
            stopwatch.Stop();

            _outputService.WriteLine(exitCode == 0
                ? $"\r\n> Task '{task.Label}' completed successfully ({stopwatch.Elapsed.TotalSeconds:F1}s)"
                : $"\r\n> Task '{task.Label}' failed with exit code {exitCode} ({stopwatch.Elapsed.TotalSeconds:F1}s)");

            args.ExitCode = exitCode;
            args.Duration = stopwatch.Elapsed;
            TaskCompleted?.Invoke(this, args);
            return exitCode;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _outputService.WriteLine($"\r\n> Task '{task.Label}' failed: {ex.Message}");
            args.ExitCode = -1;
            args.Duration = stopwatch.Elapsed;
            TaskCompleted?.Invoke(this, args);
            return -1;
        }
    }

    public async Task<TaskDefinition?> GetDefaultBuildTaskAsync(string projectPath)
    {
        var config = await LoadTasksAsync(projectPath);
        return config.Tasks.FirstOrDefault(t =>
            string.Equals(t.Group, "build", StringComparison.OrdinalIgnoreCase) && t.IsDefault)
            ?? config.Tasks.FirstOrDefault(t =>
                string.Equals(t.Group, "build", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<TaskDefinition?> GetDefaultTestTaskAsync(string projectPath)
    {
        var config = await LoadTasksAsync(projectPath);
        return config.Tasks.FirstOrDefault(t =>
            string.Equals(t.Group, "test", StringComparison.OrdinalIgnoreCase) && t.IsDefault)
            ?? config.Tasks.FirstOrDefault(t =>
                string.Equals(t.Group, "test", StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<TaskDefinition> GetAvailableTasks(string projectPath)
    {
        if (_cachedConfig != null && string.Equals(_cachedProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedConfig.Tasks.AsReadOnly();
        }

        // Synchronous fallback — try to load from file
        var filePath = GetTasksFilePath(projectPath);
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<TasksConfig>(json, JsonOptions);
                if (config != null)
                {
                    // Merge with auto-detected tasks
                    var autoDetected = DetectTasks(projectPath);
                    foreach (var auto in autoDetected)
                    {
                        if (!config.Tasks.Any(t => t.Label == auto.Label))
                        {
                            config.Tasks.Add(auto);
                        }
                    }
                    _cachedConfig = config;
                    _cachedProjectPath = projectPath;
                    return config.Tasks.AsReadOnly();
                }
            }
            catch
            {
                // Fall through to defaults
            }
        }

        var defaultConfig = CreateDefaultConfig(projectPath);
        _cachedConfig = defaultConfig;
        _cachedProjectPath = projectPath;
        return defaultConfig.Tasks.AsReadOnly();
    }

    public async Task<string> CreateDefaultTasksFileAsync(string projectPath)
    {
        var filePath = GetTasksFilePath(projectPath);

        if (File.Exists(filePath))
        {
            return filePath;
        }

        var config = new TasksConfig
        {
            Version = "2.0.0",
            Tasks = new List<TaskDefinition>
            {
                new()
                {
                    Label = "Build",
                    Type = "shell",
                    Command = "dotnet",
                    Args = new[] { "build", "${workspaceFolder}" },
                    Group = "build",
                    IsDefault = true,
                    ProblemMatcher = "$msCompile",
                    Presentation = new TaskPresentation
                    {
                        Reveal = "always",
                        Panel = true
                    }
                },
                new()
                {
                    Label = "Test",
                    Type = "shell",
                    Command = "dotnet",
                    Args = new[] { "test", "${workspaceFolder}" },
                    Group = "test",
                    IsDefault = true,
                    ProblemMatcher = "$msCompile",
                    Presentation = new TaskPresentation
                    {
                        Reveal = "always",
                        Panel = true
                    }
                },
                new()
                {
                    Label = "Run",
                    Type = "shell",
                    Command = "dotnet",
                    Args = new[] { "run", "--project", "${workspaceFolder}" },
                    Group = "none",
                    Presentation = new TaskPresentation
                    {
                        Reveal = "always",
                        Focus = true,
                        Panel = true
                    }
                }
            }
        };

        await SaveTasksAsync(projectPath, config);
        return filePath;
    }

    /// <summary>
    /// Creates a default configuration with auto-detected tasks for the project.
    /// </summary>
    private TasksConfig CreateDefaultConfig(string projectPath)
    {
        var config = new TasksConfig { Version = "2.0.0" };

        // Try to load user tasks from file
        var filePath = GetTasksFilePath(projectPath);
        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var userConfig = JsonSerializer.Deserialize<TasksConfig>(json, JsonOptions);
                if (userConfig != null)
                {
                    config = userConfig;
                }
            }
            catch
            {
                // Ignore parse errors
            }
        }

        // Add auto-detected tasks
        var autoDetected = DetectTasks(projectPath);
        foreach (var auto in autoDetected)
        {
            if (!config.Tasks.Any(t => t.Label == auto.Label))
            {
                config.Tasks.Add(auto);
            }
        }

        return config;
    }

    /// <summary>
    /// Auto-detects tasks based on project files found in the workspace.
    /// </summary>
    private List<TaskDefinition> DetectTasks(string projectPath)
    {
        var tasks = new List<TaskDefinition>();

        // Detect BasicLang project files
        var blprojFiles = Directory.GetFiles(projectPath, "*.blproj", SearchOption.TopDirectoryOnly);
        foreach (var blproj in blprojFiles)
        {
            var name = Path.GetFileNameWithoutExtension(blproj);
            tasks.Add(new TaskDefinition
            {
                Label = $"BasicLang: Build {name}",
                Type = "shell",
                Command = "basiclang",
                Args = new[] { "build", Path.GetFileName(blproj) },
                Group = "build",
                IsDefault = !tasks.Any(t => t.Group == "build"),
                ProblemMatcher = "$msCompile",
                IsAutoDetected = true
            });

            tasks.Add(new TaskDefinition
            {
                Label = $"BasicLang: Run {name}",
                Type = "shell",
                Command = "basiclang",
                Args = new[] { "run", Path.GetFileName(blproj) },
                Group = "none",
                IsAutoDetected = true
            });
        }

        // Detect .csproj / .sln files
        var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly);
        var slnFiles = Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly);

        if (slnFiles.Length > 0)
        {
            var sln = Path.GetFileName(slnFiles[0]);
            tasks.Add(new TaskDefinition
            {
                Label = $"dotnet: build {sln}",
                Type = "shell",
                Command = "dotnet",
                Args = new[] { "build", sln },
                Group = "build",
                IsDefault = !tasks.Any(t => t.Group == "build" && t.IsDefault),
                ProblemMatcher = "$msCompile",
                IsAutoDetected = true
            });
        }
        else if (csprojFiles.Length > 0)
        {
            var proj = Path.GetFileName(csprojFiles[0]);
            tasks.Add(new TaskDefinition
            {
                Label = $"dotnet: build {proj}",
                Type = "shell",
                Command = "dotnet",
                Args = new[] { "build", proj },
                Group = "build",
                IsDefault = !tasks.Any(t => t.Group == "build" && t.IsDefault),
                ProblemMatcher = "$msCompile",
                IsAutoDetected = true
            });
        }

        // Detect package.json (npm tasks)
        var packageJson = Path.Combine(projectPath, "package.json");
        if (File.Exists(packageJson))
        {
            tasks.Add(new TaskDefinition
            {
                Label = "npm: build",
                Type = "shell",
                Command = "npm",
                Args = new[] { "run", "build" },
                Group = "build",
                IsDefault = !tasks.Any(t => t.Group == "build" && t.IsDefault),
                IsAutoDetected = true
            });

            tasks.Add(new TaskDefinition
            {
                Label = "npm: test",
                Type = "shell",
                Command = "npm",
                Args = new[] { "test" },
                Group = "test",
                IsAutoDetected = true
            });
        }

        // Detect Makefile
        var makefile = Path.Combine(projectPath, "Makefile");
        if (!File.Exists(makefile))
            makefile = Path.Combine(projectPath, "makefile");

        if (File.Exists(makefile))
        {
            tasks.Add(new TaskDefinition
            {
                Label = "make: build",
                Type = "shell",
                Command = "make",
                Group = "build",
                IsDefault = !tasks.Any(t => t.Group == "build" && t.IsDefault),
                ProblemMatcher = "$gcc",
                IsAutoDetected = true
            });

            tasks.Add(new TaskDefinition
            {
                Label = "make: clean",
                Type = "shell",
                Command = "make",
                Args = new[] { "clean" },
                Group = "none",
                IsAutoDetected = true
            });
        }

        return tasks;
    }

    /// <summary>
    /// Resolves variable placeholders like ${workspaceFolder} in a string.
    /// </summary>
    private static string ResolveVariables(string input, string workspaceFolder)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return input
            .Replace("${workspaceFolder}", workspaceFolder)
            .Replace("${workspaceFolderBasename}", Path.GetFileName(workspaceFolder))
            .Replace("${pathSeparator}", Path.DirectorySeparatorChar.ToString());
    }
}
