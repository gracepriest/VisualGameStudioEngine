namespace BasicLang.Forms;

/// <summary>
/// The method/property rows the portable Forms library must implement. Task 34/35 consume
/// this list for WebUnavailableMember and method coverage; Task 29 owns the first rows.
/// </summary>
public enum PortableMemberKind
{
    Method,
    Property
}

/// <summary>
/// One public member of the portable library. <see cref="SampleArguments"/> contains one
/// BasicLang expression per parameter, used by the later coverage gate.
/// </summary>
public sealed record PortableMember(
    string DeclaringType,
    string Name,
    PortableMemberKind Kind,
    string WinFormsSignature,
    IReadOnlyList<string> SampleArguments);

public static class PortableMembers
{
    /// <summary>
    /// Task 29's public surface. Catalog-backed members (Text, Enabled, events, ...) are
    /// deliberately absent: they are verified against the catalog, not this method manifest.
    /// </summary>
    public static IReadOnlyList<PortableMember> Manifest { get; } = new PortableMember[]
    {
        new("Control", "Show", PortableMemberKind.Method,
            "Sub Show()", Array.Empty<string>()),
        new("Control", "Hide", PortableMemberKind.Method,
            "Sub Hide()", Array.Empty<string>()),
        new("Control", "Focus", PortableMemberKind.Method,
            "Sub Focus()", Array.Empty<string>()),
        new("Control", "BringToFront", PortableMemberKind.Method,
            "Sub BringToFront()", Array.Empty<string>()),

        new("ControlCollection", "Add", PortableMemberKind.Method,
            "Sub Add(child As Control)", new[] { "New Control()" }),
        new("ControlCollection", "Remove", PortableMemberKind.Method,
            "Sub Remove(child As Control)", new[] { "New Control()" }),
        // BasicLang has no indexed properties. Item is a Function with the same call shape;
        // WinForms' indexed property is represented by this method row.
        new("ControlCollection", "Item", PortableMemberKind.Method,
            "Function Item(index As Integer) As Control", new[] { "0" }),
        new("ControlCollection", "Count", PortableMemberKind.Property,
            "ReadOnly Property Count As Integer", Array.Empty<string>()),

        new("MessageBox", "Show", PortableMemberKind.Method,
            "Shared Function Show(text As String, Optional caption As String = \"\", Optional buttons As MessageBoxButtons = MessageBoxButtons.OK) As DialogResult",
            new[] { "\"hello\"", "\"\"", "MessageBoxButtons.OK" }),

        new("Form", "Close", PortableMemberKind.Method,
            "Sub Close()", Array.Empty<string>()),

        new("TextBox", "Clear", PortableMemberKind.Method,
            "Sub Clear()", Array.Empty<string>()),
        new("TextBox", "AppendText", PortableMemberKind.Method,
            "Sub AppendText(s As String)", new[] { "\"x\"" }),
        new("TextBox", "SelectAll", PortableMemberKind.Method,
            "Sub SelectAll()", Array.Empty<string>()),
        new("Button", "PerformClick", PortableMemberKind.Method,
            "Sub PerformClick()", Array.Empty<string>())
    };
}
