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
    // ⛔ FIRST, before anything touches a descriptor: PropertyDescriptor/EventDescriptor.Category and
    // .Description localise through CurrentUICulture, and the WindowsDesktop runtime ships de/fr/ja/…
    // satellite resources — a regeneration on a German Windows would write "Verhalten". Invariant
    // falls back to the neutral (English) resources, which is what the catalog is written against.
    CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
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
    ///   unreadable — the getter or ShouldSerializeValue THREW on a fresh instance: nothing was measured,
    ///                so no default is recorded (never a confident "reset" of null);
    ///   volatile   — two fresh instances disagree (a clock read): no static default;
    ///   serialized — no attribute, and ShouldSerializeValue says a fresh instance WOULD be written;
    ///   reset      — no attribute, and ShouldSerializeValue is FALSE on a fresh instance: the designer
    ///                would not write it, so its current value is the type's default. (Measured through
    ///                ShouldSerializeValue; ResetValue is never called.)
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
            var readA = TryRead(p, first, out var rawA);
            var readB = TryRead(p, second, out var rawB);
            var a = Normalize(rawA);
            var b = Normalize(rawB);

            // ⛔ Either read throwing is unreadable, not only both: one throw and one value would
            // otherwise compare unequal and be recorded as a clock read.
            if (!readA || !readB)
            {
                kind = "unreadable";
                value = null;
            }
            else if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                kind = "volatile";
                value = null;
            }
            else if (!TryShouldSerialize(p, first, out var wouldWrite))
            {
                kind = "unreadable";
                value = null;
            }
            else if (wouldWrite)
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
    /// A control goes into a Panel; a ToolStripItem into a ToolStrip; a component (Timer…) has no parent
    /// and is never ambient.
    ///
    /// ⛔ The Form is NEVER parented (review decision I2): the designer's root Form is TOP-LEVEL, and that
    /// is what Visual Studio's Properties window shows for it — a top-level form has no parent to inherit
    /// Font/ForeColor/Cursor/RightToLeft from, so its values are its own. Parenting it (TopLevel=false)
    /// measured a situation the designer never produces. It is also required, not only chosen: a
    /// top-level Form falling through to the Control arm would throw adding itself to a Panel.
    /// </summary>
    private static bool IsAmbient(Type owner, PropertyDescriptor p)
    {
        if (!Sentinels.TryGetValue(p.PropertyType, out var make) || typeof(Form).IsAssignableFrom(owner))
        {
            return false;
        }

        var child = Activator.CreateInstance(owner)!;
        object parent;

        switch (child)
        {
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

    /// <summary>False when the getter throws — the caller records "unreadable", never a null default.</summary>
    private static bool TryRead(PropertyDescriptor p, object instance, out object? value)
    {
        try
        {
            value = p.GetValue(instance);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    /// <summary>False when ShouldSerializeValue throws — the caller records "unreadable".</summary>
    private static bool TryShouldSerialize(PropertyDescriptor p, object instance, out bool wouldWrite)
    {
        try
        {
            wouldWrite = p.ShouldSerializeValue(instance);
            return true;
        }
        catch
        {
            wouldWrite = false;
            return false;
        }
    }

    /// <summary>
    /// One culture-invariant text form per value type, so the test needs no WinForms types to compare.
    /// Colour: the KnownColor/system name when named, else #AARRGGBB; Empty is null. DateTime: round-trip.
    /// ⛔ Every interpolation is FormattableString.Invariant — the thread culture is pinned too, but a
    /// value's text must not depend on a pin set 300 lines away.
    /// </summary>
    private static string? Normalize(object? v) => v switch
    {
        null => null,
        Color c => c.IsEmpty ? null : c.IsNamedColor ? c.Name : FormattableString.Invariant($"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"),
        Font f => FormattableString.Invariant($"{f.Name}, {f.SizeInPoints}pt") +
                  (f.Style != FontStyle.Regular ? $", style={f.Style}" : ""),
        Size s => FormattableString.Invariant($"{s.Width}, {s.Height}"),
        Point pt => FormattableString.Invariant($"{pt.X}, {pt.Y}"),
        Padding pd => pd.All >= 0
            ? pd.All.ToString(CultureInfo.InvariantCulture)
            : FormattableString.Invariant($"{pd.Left}, {pd.Top}, {pd.Right}, {pd.Bottom}"),
        Cursor cur => new CursorConverter().ConvertToInvariantString(cur),
        bool b => b ? "True" : "False",
        char ch => ch == '\0' ? null : ch.ToString(),
        // ⛔ Round-trip ("o", 100ns ticks), never the IFormattable arm below: invariant "G" is SECOND
        // precision, so two instances made 50ms apart read identically and DateTimePicker.Value — a
        // clock read — was classified "reset" with a timestamp as its default (measured).
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IConvertible cv => cv.ToString(CultureInfo.InvariantCulture),
        // ⛔ A complex value this table does not know (FlatAppearance, LinkArea, …) has no text form a
        // catalog Default could equal; its ToString is usually the TYPE NAME, which would read as a
        // confident default. Null says "no scalar value recorded".
        _ => null
    };
}
