using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Forms;

// Regenerates the designer catalog's TEST ORACLE. Usage (repo root, Windows):
//   dotnet run --project tools/WinFormsMetadataDump/WinFormsMetadataDump.csproj -c Release -- VisualGameStudio.Tests/Data/winforms-metadata.json
var output = args.Length > 0 ? args[0] : "winforms-metadata.json";
string? json = null;
Exception? failure = null;

// ⛔ STA: WinForms controls (and TypeDescriptor over them) require it, exactly as the designer does.
var thread = new Thread(() =>
{
    try { json = Dump.Run(); }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

if (failure != null)
{
    Console.Error.WriteLine(failure);
    return 1;
}

File.WriteAllText(output, json!, new UTF8Encoding(false));
Console.WriteLine($"Wrote {Path.GetFullPath(output)} ({json!.Length} chars)");
return 0;

internal static class Dump
{
    /// <summary>
    /// Every WinForms kind in FormControlCatalog, keyed by the CATALOG's Kind, plus the Form.
    /// ⛔ WinFormsCatalogParityTests.TheSnapshot_CoversEveryWinFormsKindInTheCatalog_AndTheForm fails
    /// when a catalog kind is missing here — a new catalog row forces a regeneration rather than
    /// silently escaping the oracle.
    /// </summary>
    private static readonly (string Kind, Type Type)[] Types =
    {
        ("Form", typeof(Form)),
        ("Label", typeof(Label)),
        ("TextBox", typeof(TextBox)),
        ("Button", typeof(Button)),
        ("CheckBox", typeof(CheckBox)),
        ("RadioButton", typeof(RadioButton)),
        ("ComboBox", typeof(ComboBox)),
        ("ListBox", typeof(ListBox)),
        ("Panel", typeof(Panel)),
        ("GroupBox", typeof(GroupBox)),
        ("PictureBox", typeof(PictureBox)),
        ("LinkLabel", typeof(LinkLabel)),
        ("NumericUpDown", typeof(NumericUpDown)),
        ("DateTimePicker", typeof(DateTimePicker)),
        ("TrackBar", typeof(TrackBar)),
        ("ProgressBar", typeof(ProgressBar)),
        ("CheckedListBox", typeof(CheckedListBox)),
        ("ListView", typeof(ListView)),
        ("TreeView", typeof(TreeView)),
        ("DataGridView", typeof(DataGridView)),
        ("TabControl", typeof(TabControl)),
        ("SplitContainer", typeof(SplitContainer)),
        ("FlowLayoutPanel", typeof(FlowLayoutPanel)),
        ("TableLayoutPanel", typeof(TableLayoutPanel)),
        ("Timer", typeof(System.Windows.Forms.Timer)),
        ("ToolTip", typeof(ToolTip)),
        ("ErrorProvider", typeof(ErrorProvider)),
        ("BackgroundWorker", typeof(BackgroundWorker)),
        ("MenuStrip", typeof(MenuStrip)),
        ("ToolStrip", typeof(ToolStrip)),
        ("StatusStrip", typeof(StatusStrip)),
        ("ToolStripMenuItem", typeof(ToolStripMenuItem)),
        ("ToolStripSeparator", typeof(ToolStripSeparator)),
        ("ToolStripButton", typeof(ToolStripButton)),
        ("ToolStripStatusLabel", typeof(ToolStripStatusLabel)),
    };

    /// <summary>
    /// The sentinel a PARENT is given to find out whether a child inherits the property. Only these
    /// four types: ⛔ never Boolean — a child of a disabled or hidden parent reports Enabled/Visible
    /// false too, and treating those as "ambient" would force the catalog's Enabled/Visible defaults to
    /// null, bringing back the exact display lie spec §2.7 removes.
    /// </summary>
    private static readonly Dictionary<Type, Func<object>> Sentinels = new()
    {
        [typeof(Color)] = () => Color.FromArgb(255, 1, 2, 3),
        [typeof(Font)] = () => new Font("Courier New", 13.5f, FontStyle.Italic),
        [typeof(Cursor)] = () => Cursors.Help,
        [typeof(RightToLeft)] = () => RightToLeft.Yes,
    };

    public static string Run()
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);

        var root = new
        {
            generatedBy = "tools/WinFormsMetadataDump",
            framework = RuntimeInformation.FrameworkDescription,
            windowsForms = typeof(Form).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            types = Types.Select(t => DescribeType(t.Kind, t.Type)).ToList()
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static object DescribeType(string kind, Type type)
    {
        var first = Activator.CreateInstance(type)!;
        Thread.Sleep(50); // so a clock-derived "default" (DateTimePicker.Value) reads differently twice
        var second = Activator.CreateInstance(type)!;

        var properties = TypeDescriptor.GetProperties(first).Cast<PropertyDescriptor>()
            .Where(p => p.IsBrowsable)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => DescribeProperty(type, first, second, p))
            .ToList();

        var events = TypeDescriptor.GetEvents(type).Cast<EventDescriptor>()
            .Where(e => e.IsBrowsable)
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .Select(DescribeEvent)
            .ToList();

        var described = new
        {
            name = kind,
            clrType = type.FullName,
            defaultEvent = TypeDescriptor.GetDefaultEvent(type)?.Name,
            defaultProperty = TypeDescriptor.GetDefaultProperty(type)?.Name,
            properties,
            events
        };

        (first as IDisposable)?.Dispose();
        (second as IDisposable)?.Dispose();
        return described;
    }

    /// <summary>
    /// defaultKind, in precedence order:
    ///   collection — a collection-typed property: no scalar default at all;
    ///   attribute  — a [DefaultValue] exists: that value IS the default;
    ///   ambient    — the child inherits the parent's value (measured by parenting): no static default;
    ///   volatile   — two fresh instances disagree (a clock read): no static default;
    ///   serialized — no attribute, and ShouldSerializeValue says a fresh instance WOULD be written;
    ///   reset      — no attribute, and the fresh instance's value is what Reset gives back.
    /// </summary>
    private static object DescribeProperty(Type owner, object first, object second, PropertyDescriptor p)
    {
        var t = p.PropertyType;
        var isCollection = t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t);
        var attribute = p.Attributes[typeof(DefaultValueAttribute)] as DefaultValueAttribute;

        string kind;
        string? value;

        if (isCollection)
        {
            kind = "collection";
            value = null;
        }
        else if (attribute != null)
        {
            kind = "attribute";
            value = Normalize(attribute.Value);
        }
        else if (IsAmbient(owner, p))
        {
            kind = "ambient";
            value = null;
        }
        else
        {
            var a = Normalize(Read(p, first));
            var b = Normalize(Read(p, second));

            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                kind = "volatile";
                value = null;
            }
            else if (SafeShouldSerialize(p, first))
            {
                kind = "serialized";
                value = a;
            }
            else
            {
                kind = "reset";
                value = a;
            }
        }

        return new
        {
            name = p.Name,
            category = p.Category,
            type = t.Name,
            typeFullName = t.FullName,
            isEnum = t.IsEnum,
            isFlags = t.IsEnum && t.IsDefined(typeof(FlagsAttribute), false),
            enumMembers = t.IsEnum ? Enum.GetNames(t) : null,
            isCollection,
            defaultKind = kind,
            @default = value,
            description = p.Description ?? ""
        };
    }

    private static object DescribeEvent(EventDescriptor e)
    {
        var args = e.EventType.GetMethod("Invoke")?.GetParameters().Skip(1).FirstOrDefault()?.ParameterType
                   ?? typeof(EventArgs);

        return new
        {
            name = e.Name,
            category = e.Category,
            argsType = args.Name,
            argsFullName = args.FullName,
            description = e.Description ?? ""
        };
    }

    /// <summary>
    /// Parents a fresh instance, gives the PARENT a sentinel, and asks whether the child now reports it.
    /// A Form is parented with TopLevel=false; a ToolStripItem into a ToolStrip; a component (Timer…)
    /// has no parent and is never ambient.
    /// </summary>
    private static bool IsAmbient(Type owner, PropertyDescriptor p)
    {
        if (!Sentinels.TryGetValue(p.PropertyType, out var make))
        {
            return false;
        }

        var child = Activator.CreateInstance(owner)!;
        object parent;

        switch (child)
        {
            case Form form:
                form.TopLevel = false;
                var host = new Panel();
                host.Controls.Add(form);
                parent = host;
                break;
            case Control control:
                var panel = new Panel();
                panel.Controls.Add(control);
                parent = panel;
                break;
            case ToolStripItem item:
                var strip = new ToolStrip();
                strip.Items.Add(item);
                parent = strip;
                break;
            default:
                (child as IDisposable)?.Dispose();
                return false;
        }

        try
        {
            var parentProperty = TypeDescriptor.GetProperties(parent)[p.Name];
            if (parentProperty == null || parentProperty.IsReadOnly)
            {
                return false;
            }

            var sentinel = make();
            parentProperty.SetValue(parent, sentinel);
            var seen = TypeDescriptor.GetProperties(child)[p.Name]?.GetValue(child);
            return string.Equals(Normalize(seen), Normalize(sentinel), StringComparison.Ordinal);
        }
        finally
        {
            (parent as IDisposable)?.Dispose();
        }
    }

    private static object? Read(PropertyDescriptor p, object instance)
    {
        try { return p.GetValue(instance); }
        catch { return null; }
    }

    private static bool SafeShouldSerialize(PropertyDescriptor p, object instance)
    {
        try { return p.ShouldSerializeValue(instance); }
        catch { return false; }
    }

    /// <summary>
    /// One culture-invariant text form per value type, so the test needs no WinForms types to compare.
    /// Colour: the KnownColor/system name when named, else #AARRGGBB; Empty is null. DateTime: round-trip.
    /// </summary>
    private static string? Normalize(object? v) => v switch
    {
        null => null,
        Color c => c.IsEmpty ? null : c.IsNamedColor ? c.Name : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}",
        Font f => $"{f.Name}, {f.SizeInPoints.ToString(CultureInfo.InvariantCulture)}pt" +
                  (f.Style != FontStyle.Regular ? $", style={f.Style}" : ""),
        Size s => $"{s.Width}, {s.Height}",
        Point pt => $"{pt.X}, {pt.Y}",
        Padding pd => pd.All >= 0
            ? pd.All.ToString(CultureInfo.InvariantCulture)
            : $"{pd.Left}, {pd.Top}, {pd.Right}, {pd.Bottom}",
        Cursor cur => new CursorConverter().ConvertToInvariantString(cur),
        bool b => b ? "True" : "False",
        char ch => ch == '\0' ? null : ch.ToString(),
        // ⛔ Round-trip ("o", 100ns ticks), never the IFormattable arm below: invariant "G" is SECOND
        // precision, so two instances made 50ms apart read identically and DateTimePicker.Value — a
        // clock read — was classified "reset" with a timestamp as its default (measured).
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()
    };
}
