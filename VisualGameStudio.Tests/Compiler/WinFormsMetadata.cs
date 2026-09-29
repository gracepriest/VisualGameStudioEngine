using System.Text.Json;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The committed WinForms reflection snapshot (spec §2.6). ⛔ A missing file FAILS — a parity gate that
/// cannot find its oracle must never report success (the FormTrayViewTests pass-by-absence lesson).
/// </summary>
internal sealed class WinFormsMetadata
{
    public string GeneratedBy { get; set; } = "";
    public string? Framework { get; set; }
    public string? WindowsForms { get; set; }
    public List<WinFormsTypeEntry> Types { get; set; } = new();

    private static WinFormsMetadata? _loaded;

    public static WinFormsMetadata Load()
    {
        if (_loaded != null)
        {
            return _loaded;
        }

        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Data", "winforms-metadata.json");
        if (!File.Exists(path))
        {
            Assert.Fail($"the WinForms oracle is missing at {path}. Regenerate it with " +
                        "tools/WinFormsMetadataDump (see its README) and check the csproj copies it.");
        }

        var loaded = JsonSerializer.Deserialize<WinFormsMetadata>(
            File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (loaded == null)
        {
            Assert.Fail($"the WinForms oracle at {path} deserialised to null (a file holding just 'null'?). " +
                        "Regenerate it with tools/WinFormsMetadataDump.");
        }

        _loaded = loaded;
        return _loaded;
    }

    public WinFormsTypeEntry? Type(string kind) =>
        Types.FirstOrDefault(t => string.Equals(t.Name, kind, StringComparison.Ordinal));
}

internal sealed class WinFormsTypeEntry
{
    public string Name { get; set; } = "";
    public string ClrType { get; set; } = "";
    public string? DefaultEvent { get; set; }
    public string? DefaultProperty { get; set; }
    public List<WinFormsPropertyEntry> Properties { get; set; } = new();
    public List<WinFormsEventEntry> Events { get; set; } = new();

    public WinFormsPropertyEntry? Property(string name) =>
        Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    public WinFormsEventEntry? Event(string name) =>
        Events.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));
}

internal sealed class WinFormsPropertyEntry
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Type { get; set; } = "";
    public string TypeFullName { get; set; } = "";
    public bool IsEnum { get; set; }
    public bool IsFlags { get; set; }
    public List<string>? EnumMembers { get; set; }
    public bool IsCollection { get; set; }

    /// <summary>
    /// One of <see cref="DefaultKinds"/> — the vocabulary is tools/WinFormsMetadataDump/README.md's
    /// "What defaultKind means" table. Only attribute/reset carry a <see cref="Default"/> to compare.
    /// </summary>
    public string DefaultKind { get; set; } = "";

    public static readonly IReadOnlyList<string> DefaultKinds = new[]
    {
        "collection", "attribute", "ambient", "unreadable", "volatile", "serialized", "reset"
    };

    public string? Default { get; set; }
    public string Description { get; set; } = "";
}

internal sealed class WinFormsEventEntry
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string ArgsType { get; set; } = "";
    public string ArgsFullName { get; set; } = "";
    public string Description { get; set; } = "";
}
