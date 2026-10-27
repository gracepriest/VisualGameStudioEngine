using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using BasicLang.Runtime;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Tests.Compiler.PixelLayout;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>One generated portable form and the actions its two reference runs need.</summary>
internal sealed record PortableRuntimeCase(
    string Name,
    FormDocument Document,
    string Source,
    string DesktopDriver = "",
    bool DesktopDialog = false,
    string DesktopDialogButton = "Yes",
    IReadOnlyList<PortableWebAction>? WebActions = null,
    string? DesktopSource = null,
    int ViewportWidth = 0,
    int ViewportHeight = 0)
{
    public IReadOnlyList<PortableWebAction> WebActionList => WebActions ?? Array.Empty<PortableWebAction>();
}

/// <summary>
/// A step the Edge runtime runs after the page's constructor. <c>Click</c>, <c>Type</c>, <c>SelectAll</c>
/// and <c>Key</c> go through CDP <c>Input.*</c>; <c>Focus</c> is used only where a fieldset cannot
/// receive focus and is named by the caller.
/// </summary>
internal sealed record PortableWebAction(
    string Kind, string Id = "", string Text = "", string Script = "", string Event = "");

/// <summary>
/// Task 35's shared runtime harnesses. The pixel harnesses remain layout-only; these two run generated
/// code in the same two references (a real WinForms window built by the CLI + csc, and real Edge over
/// DevTools) and return the case's emitted lines.
/// </summary>
internal static class PortableRuntimeHarnesses
{
    // ---------------------------------------------------------------- source construction

    /// <summary>
    /// Writes a portable web code-behind for <paramref name="document"/> and appends the shared case code.
    /// The constructor calls <c>Cover</c> on the web only; the desktop driver calls it after Show, where
    /// effective visibility and focus mean the same as they do on the page.
    /// </summary>
    public static string CreatePortableSource(string name, FormDocument document, string extraCode)
    {
        var scaffold = FormScaffolder.Create(name, FormTarget.Web, portable: true);
        var written = RegionWriter.Write(scaffold.CodeFileName, scaffold.CodeText, document, scaffold.DocumentFileName);
        if (written.Refused)
        {
            throw new InvalidOperationException(
                $"{name}: the region writer refused: {string.Join("; ", written.Diagnostics.Select(d => d.Format()))}");
        }

        var text = written.Text;
        const string anchor = "        Me.InitializeComponent()\n";
        if (!text.Contains(anchor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name}: the portable scaffold has no InitializeComponent call to hook");
        }

        text = text.Replace(anchor,
            anchor + "#If WEB Then\n            Me.Cover()\n#End If\n", StringComparison.Ordinal);
        return AppendBeforeEndClass(text, extraCode);
    }

    /// <summary>The WinForms-shaped scaffold used for the desktop half of the behaviour twin.</summary>
    public static string CreateWinFormsSource(string name, FormDocument document, string extraCode)
    {
        var scaffold = FormScaffolder.Create(name, FormTarget.WinForms);
        var written = RegionWriter.Write(scaffold.CodeFileName, scaffold.CodeText, document, scaffold.DocumentFileName);
        if (written.Refused)
        {
            throw new InvalidOperationException(
                $"{name}: the region writer refused: {string.Join("; ", written.Diagnostics.Select(d => d.Format()))}");
        }

        return AppendBeforeEndClass(written.Text, extraCode);
    }

    private static string AppendBeforeEndClass(string text, string extraCode)
    {
        var at = text.LastIndexOf("End Class", StringComparison.Ordinal);
        if (at < 0) throw new InvalidOperationException("the scaffold has no End Class");
        return text[..at] + extraCode.TrimEnd() + "\n\n" + text[at..];
    }

    /// <summary>Compiles the portable source through the real CLI; returns the emitted C# path.</summary>
    public static string EmitDesktopCSharp(string workDir, string name, string source)
    {
        var bas = Path.Combine(workDir, name + ".bas");
        File.WriteAllText(bas, source);
        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { bas, "--target=csharp" }, workDir, timeoutMs: 180_000);
        Assert.That(exit, Is.Zero, $"the real CLI refused {name}.\n{stdout}\n{stderr}\n--- .bas ---\n{source}");

        var cs = Path.ChangeExtension(bas, ".cs");
        Assert.That(File.Exists(cs), Is.True, $"the CLI emitted no C# for {name}.\n{stdout}");
        return cs;
    }

    // ---------------------------------------------------------------- WinForms runtime

    /// <summary>
    /// Runs every case in one real WinForms app. Skips (never passes) without Windows, dotnet, or the
    /// Windows Desktop SDK. Each case is showed, then <c>Cover</c> is called through reflection after
    /// the window has a handle; messages are delivered to real control handles.
    /// </summary>
    public static IReadOnlyDictionary<string, string> RunWinForms(string workDir, IReadOnlyList<PortableRuntimeCase> cases)
    {
        RequireDesktop();
        Assert.That(cases.Select(c => c.Name), Is.Unique, "every runtime case needs its own form name");

        var app = Path.Combine(workDir, "runtime-app");
        Directory.CreateDirectory(app);
        foreach (var @case in cases)
        {
            var cs = EmitDesktopCSharp(workDir, @case.Name, @case.DesktopSource ?? @case.Source);
            File.Copy(cs, Path.Combine(app, @case.Name + ".cs"), overwrite: true);
        }

        File.WriteAllText(Path.Combine(app, "app.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <Nullable>disable</Nullable>
                <AssemblyName>PortableRuntimeDriver</AssemblyName>
                <RootNamespace>PortableRuntimeDriver</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(app, "Driver.cs"), WinFormsDriverSource(cases));

        var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
            "dotnet", new[] { "build", "-c", "Release", "--nologo" }, app, timeoutMs: 300_000);
        Assert.That(buildExit, Is.Zero, $"the WinForms runtime app did not build.\n{buildOut}\n{buildErr}");

        var exe = Directory.GetFiles(app, "PortableRuntimeDriver.exe", SearchOption.AllDirectories).FirstOrDefault();
        Assert.That(exe, Is.Not.Null, "the runtime app built but produced no PortableRuntimeDriver.exe");

        var (runExit, runOut, runErr) = CliTestHarness.RunProcess(exe!, Array.Empty<string>(), app, timeoutMs: 180_000);
        var outputs = ParseCaseOutput(runOut, cases.Select(c => c.Name).ToList());
        Assert.That(runExit, Is.Zero, $"the WinForms runtime driver failed (exit {runExit}).\n{runOut}\n{runErr}");
        return outputs;
    }

    private static string WinFormsDriverSource(IReadOnlyList<PortableRuntimeCase> cases)
    {
        var blocks = new StringBuilder();
        foreach (var @case in cases)
        {
            var name = Token(@case.Name, "case name");
            var driver = @case.DesktopDriver.Trim();
            var watcher = @case.DesktopDialog
                ? $"                using (var answer = new DialogAnswer(\"{Token(@case.DesktopDialogButton, "dialog button")}\"))\n                {{\n"
                : "";
            var watcherEnd = @case.DesktopDialog ? "                }\n" : "";

            blocks.Append($"            using (var form = new global::GeneratedCode.{name}())\n");
            blocks.Append("            {\n");
            blocks.Append($"                Console.WriteLine(\"CASE {name}\");\n");
            blocks.Append("                form.StartPosition = FormStartPosition.Manual;\n");
            blocks.Append("                form.Location = new Point(40, 40);\n");
            blocks.Append("                form.Show();\n");
            blocks.Append("                Application.DoEvents();\n");
            blocks.Append("                Thread.Sleep(80);\n");
            blocks.Append(watcher);
            blocks.Append("                try\n");
            blocks.Append("                {\n");
            blocks.Append("                    Invoke(form, \"Cover\");\n");
            blocks.Append("                    Application.DoEvents();\n");
            if (driver.Length > 0)
            {
                foreach (var line in driver.Replace("\r\n", "\n").Split('\n'))
                {
                    blocks.Append("                    ").Append(line).Append('\n');
                }

                blocks.Append("                    Application.DoEvents();\n");
            }

            blocks.Append("                }\n");
            blocks.Append("                finally\n");
            blocks.Append("                {\n");
            blocks.Append($"                    Console.WriteLine(\"CASEEND {name}\");\n");
            blocks.Append("                }\n");
            blocks.Append(watcherEnd);
            blocks.Append("                form.Close();\n");
            blocks.Append("            }\n");
        }

        return $$"""
            using System;
            using System.Diagnostics;
            using System.Drawing;
            using System.Reflection;
            using System.Runtime.InteropServices;
            using System.Threading;
            using System.Windows.Forms;

            internal static class Driver
            {
                private const int WM_MOUSEMOVE = 0x0200;
                private const int WM_LBUTTONDOWN = 0x0201;
                private const int WM_LBUTTONUP = 0x0202;
                private const int WM_CHAR = 0x0102;
                private const int WM_KEYDOWN = 0x0100;
                private const int WM_KEYUP = 0x0101;
                private const int WM_COMMAND = 0x0111;
                private const int BM_CLICK = 0x00F5;
                private static readonly IntPtr MK_LBUTTON = (IntPtr)1;

                [STAThread]
                private static int Main()
                {
                    Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    try
                    {
            {{blocks}}
                        Console.WriteLine("DONE");
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ERROR " + ex);
                        return 1;
                    }
                }

                private static void Invoke(object target, string method)
                {
                    var info = target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
                    if (info == null) throw new MissingMethodException(target.GetType().FullName, method);
                    info.Invoke(target, Array.Empty<object>());
                }

                private static Control Find(Control root, string id)
                {
                    if (root.Name == id) return root;
                    foreach (Control child in root.Controls)
                    {
                        var found = Find(child, id);
                        if (found != null) return found;
                    }
                    return null;
                }

                private static void Click(Control control)
                {
                    // A direct WM_LBUTTONDOWN/UP does not update the process-wide MouseButtons state in a
                    // non-interactive desktop, so ButtonBase never sees a completed click. BM_CLICK is the
                    // real button message and raises Click/CheckedChanged exactly once.
                    if (control is ButtonBase)
                    {
                        control.Focus();
                        SendMessage(control.Handle, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                        Application.DoEvents();
                        return;
                    }
                    ClickAt(control, Math.Max(1, control.ClientSize.Width / 2), Math.Max(1, control.ClientSize.Height / 2));
                }

                private static void ClickAt(Control control, int x, int y)
                {
                    control.Focus();
                    var point = new Point(x, y);
                    var lparam = (IntPtr)((point.Y << 16) | point.X);
                    // A real click moves the pointer first; LinkLabel tracks the hovered link on
                    // mouse move and only then treats the button pair as a link click.
                    SendMessage(control.Handle, WM_MOUSEMOVE, IntPtr.Zero, lparam);
                    SendMessage(control.Handle, WM_LBUTTONDOWN, MK_LBUTTON, lparam);
                    SendMessage(control.Handle, WM_LBUTTONUP, IntPtr.Zero, lparam);
                    Application.DoEvents();
                }

                private static void TypeText(Control control, string text)
                {
                    control.Focus();
                    foreach (var ch in text)
                    {
                        SendMessage(control.Handle, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
                    }
                    Application.DoEvents();
                }

                private static void TypeKeys(Control control, string text)
                {
                    control.Focus();
                    if (control is TextBox textBox)
                    {
                        textBox.SelectionStart = 0;
                        textBox.SelectionLength = 0;
                    }
                    foreach (var ch in text)
                    {
                        var virtualKey = char.ToUpperInvariant(ch);
                        SendMessage(control.Handle, WM_KEYDOWN, (IntPtr)virtualKey, IntPtr.Zero);
                        SendMessage(control.Handle, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
                        SendMessage(control.Handle, WM_KEYUP, (IntPtr)virtualKey, IntPtr.Zero);
                    }
                    Application.DoEvents();
                }

                private static void SelectAllKeys(Control control)
                {
                    control.Focus();
                    keybd_event(0x11, 0, 0, UIntPtr.Zero);
                    keybd_event(0x41, 0, 0, UIntPtr.Zero);
                    keybd_event(0x41, 0, 2, UIntPtr.Zero);
                    keybd_event(0x11, 0, 2, UIntPtr.Zero);
                    Application.DoEvents();
                }

                private static void Key(Control control, int virtualKey, char key)
                {
                    control.Focus();
                    SendMessage(control.Handle, WM_KEYDOWN, (IntPtr)virtualKey, IntPtr.Zero);
                    if (key != '\0') SendMessage(control.Handle, WM_CHAR, (IntPtr)key, IntPtr.Zero);
                    SendMessage(control.Handle, WM_KEYUP, (IntPtr)virtualKey, IntPtr.Zero);
                    Application.DoEvents();
                }

                private static void RaiseEnter(Control control)
                {
                    var enter = typeof(Control).GetMethod("OnEnter", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (enter == null) throw new MissingMethodException("Control", "OnEnter");
                    enter.Invoke(control, new object[] { EventArgs.Empty });
                    Application.DoEvents();
                }

                private static void RaiseClick(Control control)
                {
                    var click = typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (click == null) throw new MissingMethodException("Control", "OnClick");
                    click.Invoke(control, new object[] { EventArgs.Empty });
                    Application.DoEvents();
                }

                private static void RaiseScroll(Control control)
                {
                    var scroll = typeof(TrackBar).GetMethod("OnScroll", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (scroll == null) throw new MissingMethodException("TrackBar", "OnScroll");
                    scroll.Invoke(control, new object[] { EventArgs.Empty });
                    Application.DoEvents();
                }

                private static void SetClientSize(Form form, int width, int height)
                {
                    form.ClientSize = new Size(width, height);
                    Application.DoEvents();
                }

                [DllImport("user32.dll")]
                private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

                [DllImport("user32.dll", CharSet = CharSet.Unicode)]
                private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

                private sealed class DialogAnswer : IDisposable
                {
                    private readonly int _button;
                    private readonly CancellationTokenSource _stop = new CancellationTokenSource();
                    private readonly Thread _thread;

                    public DialogAnswer(string button)
                    {
                        _button = button == "OK" ? 1 : button == "No" ? 7 : 6;
                        _thread = new Thread(Loop) { IsBackground = true };
                        _thread.Start();
                    }

                    private void Loop()
                    {
                        while (!_stop.IsCancellationRequested)
                        {
                            var found = false;
                            EnumWindows((hwnd, _) =>
                            {
                                GetWindowThreadProcessId(hwnd, out var pid);
                                if (pid != Environment.ProcessId) return true;
                                var cls = new System.Text.StringBuilder(64);
                                GetClassName(hwnd, cls, cls.Capacity);
                                if (cls.ToString() != "#32770") return true;
                                var button = FindWindowEx(hwnd, IntPtr.Zero, "Button", null);
                                if (button != IntPtr.Zero)
                                {
                                    SendMessage(button, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                                }
                                else
                                {
                                    SendMessage(hwnd, WM_COMMAND, (IntPtr)_button, IntPtr.Zero);
                                }
                                found = true;
                                return false;
                            }, IntPtr.Zero);
                            Thread.Sleep(found ? 50 : 20);
                        }
                    }

                    public void Dispose()
                    {
                        _stop.Cancel();
                        _thread.Join(1000);
                    }
                }

                private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

                [DllImport("user32.dll")]
                private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

                [DllImport("user32.dll", CharSet = CharSet.Unicode)]
                private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

                [DllImport("user32.dll")]
                private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

                [DllImport("user32.dll", CharSet = CharSet.Unicode)]
                private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder className, int maxCount);
            }
            """;
    }

    public static IReadOnlyDictionary<string, string> ParseCaseOutput(string output, IReadOnlyList<string> names)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n');
        var result = new Dictionary<string, string>();
        foreach (var name in names)
        {
            var start = Array.FindIndex(lines, line => line.Trim() == "CASE " + name);
            var end = Array.FindIndex(lines, line => line.Trim() == "CASEEND " + name);
            if (start < 0 || end < start)
            {
                throw new InvalidDataException($"the runtime driver never completed case {name}:\n{output}");
            }

            var error = lines.Take(end).FirstOrDefault(line => line.StartsWith("ERROR ", StringComparison.Ordinal));
            if (error != null) throw new InvalidDataException("the runtime driver failed: " + error);
            result[name] = string.Join("\n", lines.Skip(start + 1).Take(end - start - 1).Select(l => l.TrimEnd()))
                .Trim('\n');
        }

        return result;
    }

    // ---------------------------------------------------------------- web site + Edge

    /// <summary>Builds one real JavaScript project holding every case's page and code-behind.</summary>
    public static string BuildWebSite(string workDir, IReadOnlyList<PortableRuntimeCase> cases)
    {
        RequireWeb();
        var project = Path.Combine(workDir, "runtime-web");
        Directory.CreateDirectory(project);
        var items = new StringBuilder();
        foreach (var @case in cases)
        {
            Assert.That(@case.Document.Name, Is.EqualTo(@case.Name), "a web case's document name must be its case name");
            File.WriteAllText(Path.Combine(project, @case.Name + ".blwebform"), FormDocumentWriter.Create(@case.Document));
            File.WriteAllText(Path.Combine(project, @case.Name + ".bas"), @case.Source);
            items.Append($"    <Compile Include=\"{@case.Name}.blwebform\" />\n");
            items.Append($"    <Compile Include=\"{@case.Name}.bas\" />\n");
        }

        File.WriteAllText(Path.Combine(project, "App.blproj"), $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>PortableRuntime</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Main.bas" />
            {items}  </ItemGroup>
            </BasicLangProject>
            """);
        File.WriteAllText(Path.Combine(project, "Main.bas"), "Sub Main()\n    Console.WriteLine(\"App loaded\")\nEnd Sub\n");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path.Combine(project, "App.blproj") }, project, timeoutMs: 180_000);
        Assert.That(exit, Is.Zero,
            $"the real CLI did not build the portable pages.\n{stdout}\n{stderr}\n" +
            string.Join("\n", cases.Select(c => $"--- {c.Name}.bas ---\n{c.Source}")));

        var site = Path.Combine(project, "bin", "Debug", "net8.0");
        foreach (var @case in cases)
        {
            Assert.That(File.Exists(Path.Combine(site, @case.Name + ".html")), Is.True,
                $"the build wrote no {@case.Name}.html.\n{stdout}");
        }

        return site;
    }

    /// <summary>Runs every case's page in one Edge instance over CDP and returns its emitted title lines.</summary>
    public static IReadOnlyDictionary<string, string> RunEdge(string siteDir, IReadOnlyList<PortableRuntimeCase> cases)
    {
        RequireWeb();
        return EdgeLayoutHarness.WithThrowawayProfile(profile =>
        {
            using var server = new WebPreviewServer();
            var baseUrl = server.Start(siteDir);
            var edge = EdgeLayoutHarness.EdgePath()!;
            var port = FreePort();
            var start = new ProcessStartInfo(edge)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
            {
                "--headless=new", $"--user-data-dir={profile}", $"--remote-debugging-port={port}",
                "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-gpu",
                "--force-device-scale-factor=1", "--window-size=1280,1024", "about:blank"
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            // Drain Edge's own output; a full stderr pipe can block the renderer on a long run.
            var drain = Task.Run(() => { process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd(); });
            try
            {
                var websocket = WaitForPageTarget(port);
                using var session = CdpSession.Connect(websocket);
                var outputs = new Dictionary<string, string>();
                foreach (var @case in cases)
                {
                    session.DialogAnswers.Clear();
                    if (@case.DesktopDialog) session.DialogAnswers.Enqueue(true);
                    if (@case.ViewportWidth > 0 && @case.ViewportHeight > 0)
                        session.SetViewport(@case.ViewportWidth, @case.ViewportHeight);
                    session.Navigate(baseUrl + @case.Name + ".html");
                    // The page dispatcher instantiates the class during module execution, before load;
                    // one turn after load also lets a zero-time reflow listener run.
                    Thread.Sleep(120);
                    foreach (var action in @case.WebActionList)
                    {
                        session.Run(action);
                    }

                    Thread.Sleep(30);
                    var title = session.EvaluateString("document.title").Replace("\r\n", "\n");
                    var lines = title.Split('|').Skip(1).Where(l => l.Length > 0).ToList();
                    outputs[@case.Name] = string.Join("\n", lines);
                }

                session.Close();
                return outputs;
            }
            finally
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                process.WaitForExit(5000);
                drain.Wait(2000);
                server.Stop();
            }
        }).Result;
    }

    private static string WaitForPageTarget(int port)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var doc = JsonDocument.Parse(client.GetStringAsync($"http://127.0.0.1:{port}/json/list").GetAwaiter().GetResult());
                foreach (var target in doc.RootElement.EnumerateArray())
                {
                    if (target.GetProperty("type").GetString() == "page" &&
                        target.TryGetProperty("webSocketDebuggerUrl", out var ws))
                    {
                        return ws.GetString()!;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
            }

            Thread.Sleep(100);
        }

        throw new InvalidOperationException("Edge never exposed a page target over --remote-debugging-port");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ---------------------------------------------------------------- prerequisites + names

    public static void RequireDesktop()
    {
        if (!OperatingSystem.IsWindows())
            TestSkip.IgnoreEvenInsideMultiple("the portable runtime gate can only run on Windows");
        if (!CliTestHarness.DotnetOnPath())
            TestSkip.IgnoreEvenInsideMultiple("the dotnet SDK is not on PATH, so the runtime app cannot be built");
        if (!DesktopSdkPresent())
            TestSkip.IgnoreEvenInsideMultiple("the Windows Desktop SDK is not installed, so the WinForms window cannot be built");
    }

    public static void RequireWeb()
    {
        RequireDesktop();
        if (EdgeLayoutHarness.EdgePath() == null)
            TestSkip.IgnoreEvenInsideMultiple("Microsoft Edge is not installed, so the portable page cannot be run");
        if (NodeLocator.Find() == null)
            TestSkip.IgnoreEvenInsideMultiple("node is not installed, so the portable page cannot be compiled and run");
    }

    private static bool DesktopSdkPresent()
    {
        if (!OperatingSystem.IsWindows() || !CliTestHarness.DotnetOnPath()) return false;
        try
        {
            var (exit, output, _) = CliTestHarness.RunProcess(
                "dotnet", new[] { "--list-sdks" }, Environment.CurrentDirectory, timeoutMs: 30_000);
            if (exit != 0) return false;
            foreach (var line in output.Replace("\r\n", "\n").Split('\n'))
            {
                var open = line.IndexOf('[');
                var close = line.IndexOf(']');
                if (open <= 0 || close <= open + 1) continue;
                var version = line[..open].Trim();
                var root = line[(open + 1)..close];
                if (File.Exists(Path.Combine(root, version, "Sdks", "Microsoft.NET.Sdk.WindowsDesktop", "Sdk", "Sdk.props")))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string Token(string text, string what) =>
        text.All(c => char.IsLetterOrDigit(c) || c == '_')
            ? text
            : throw new ArgumentException($"{what} '{text}' must be a plain token");

    // ---------------------------------------------------------------- CDP

    /// <summary>A minimal DevTools client: one page target, JSON-RPC, dialogs answered accept=true.</summary>
    private sealed class CdpSession : IDisposable
    {
        private readonly ClientWebSocket _socket;
        private readonly SemaphoreSlim _send = new(1, 1);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
        private readonly List<(string Method, TaskCompletionSource<JsonElement> Result)> _events = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread _reader;
        private int _nextId;

        public Queue<bool> DialogAnswers { get; } = new();

        private CdpSession(ClientWebSocket socket)
        {
            _socket = socket;
            _reader = new Thread(ReadLoop) { IsBackground = true };
            _reader.Start();
            Send("Page.enable", null);
            Send("Runtime.enable", null);
            Send("Log.enable", null);
        }

        public static CdpSession Connect(string websocketUrl)
        {
            var socket = new ClientWebSocket();
            socket.ConnectAsync(new Uri(websocketUrl), CancellationToken.None).GetAwaiter().GetResult();
            return new CdpSession(socket);
        }

        private void ReadLoop()
        {
            var buffer = new byte[1024 * 1024];
            var message = new MemoryStream();
            try
            {
                while (!_stop.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    var result = _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _stop.Token)
                        .GetAwaiter().GetResult();
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;

                    var text = Encoding.UTF8.GetString(message.ToArray());
                    message.SetLength(0);
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement.Clone();
                    if (root.TryGetProperty("id", out var idElement) &&
                        _pending.TryRemove(idElement.GetInt32(), out var pending))
                    {
                        pending.SetResult(root);
                    }
                    else if (root.TryGetProperty("method", out var methodElement))
                    {
                        var method = methodElement.GetString() ?? "";
                        foreach (var listener in _events.Where(e => e.Method == method).ToList())
                        {
                            listener.Result.TrySetResult(root);
                        }

                        if (method == "Page.javascriptDialogOpening")
                        {
                            var accept = DialogAnswers.Count > 0 ? DialogAnswers.Dequeue() : true;
                            Task.Run(() => Send("Page.handleJavaScriptDialog", new { accept }));
                        }

                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
            finally
            {
                foreach (var pending in _pending.Values) pending.TrySetException(new InvalidOperationException("CDP socket closed"));
            }
        }

        public void Navigate(string url)
        {
            var load = WaitForEvent("Page.loadEventFired");
            Send("Page.navigate", new { url });
            if (!load.Task.Wait(TimeSpan.FromSeconds(20)))
            {
                throw new TimeoutException("the page never fired Page.loadEventFired: " + url);
            }
        }

        public void SetViewport(int width, int height)
        {
            Send("Emulation.setDeviceMetricsOverride", new
            {
                width, height, deviceScaleFactor = 1, mobile = false
            });
        }

        public string EvaluateString(string expression)
        {            var result = Send("Runtime.evaluate", new
            {
                expression = "(function(){ try { return " + expression + "; } catch (e) { return 'ERROR:' + e; } })()",
                returnByValue = true
            });
            if (result.TryGetProperty("exceptionDetails", out var exception))
            {
                throw new InvalidOperationException("the page threw: " + exception.GetRawText());
            }

            if (!result.TryGetProperty("result", out var remote) ||
                !remote.TryGetProperty("value", out var value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return "";
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString();
        }

        public void Run(PortableWebAction action)
        {
            switch (action.Kind)
            {
                case "click":
                    Click(action.Id);
                    break;
                case "type":
                    Click(action.Id);
                    Send("Input.insertText", new { text = action.Text });
                    break;
                case "typeKeys":
                    TypeKeys(action.Id, action.Text);
                    break;
                case "selectKeys":
                    SelectAllKeys(action.Id);
                    break;
                case "selectAll":
                    Click(action.Id);
                    Key("a", "KeyA", 65, modifiers: 2);
                    break;
                case "clear":
                    Click(action.Id);
                    Key("a", "KeyA", 65, modifiers: 2);
                    Key("Backspace", "Backspace", 8, 0);
                    break;
                case "key":
                    Key(action.Text, action.Text, 0, 0);
                    break;
                case "focus":
                    EvaluateString($"document.getElementById('{Escape(action.Id)}').focus()");
                    break;
                case "syntheticFocus":
                    EvaluateString(
                        $"document.getElementById('{Escape(action.Id)}').dispatchEvent(new FocusEvent('focusin',{{bubbles:true}}))");
                    break;
                case "eval":
                    EvaluateString(action.Script);
                    break;
                case "resize":
                    Send("Emulation.setDeviceMetricsOverride", new
                    {
                        width = int.Parse(action.Text, CultureInfo.InvariantCulture),
                        height = int.Parse(action.Script, CultureInfo.InvariantCulture),
                        deviceScaleFactor = 1,
                        mobile = false
                    });
                    break;
                case "wait":
                    Thread.Sleep(int.Parse(action.Text, CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "unknown web action");
            }
        }

        private void Click(string id)
        {
            var raw = EvaluateString($"JSON.stringify(document.getElementById('{Escape(id)}').getBoundingClientRect())");
            using var doc = JsonDocument.Parse(raw);
            var rect = doc.RootElement;
            var x = rect.GetProperty("left").GetDouble() + rect.GetProperty("width").GetDouble() / 2;
            var y = rect.GetProperty("top").GetDouble() + rect.GetProperty("height").GetDouble() / 2;
            Send("Input.dispatchMouseEvent", new { type = "mouseMoved", x, y, button = "none" });
            Send("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 });
            Send("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 });
            Thread.Sleep(30);
        }

        private void TypeKeys(string id, string text)
        {
            Click(id);
            EvaluateString($"(function(){{ var e = document.getElementById('{Escape(id)}'); e.selectionStart = 0; e.selectionEnd = 0; }})()");
            foreach (var ch in text)
            {
                var key = ch.ToString();
                var code = char.IsLetter(ch) ? "Key" + char.ToUpperInvariant(ch) : key;
                var virtualKey = char.ToUpperInvariant(ch);
                Send("Input.dispatchKeyEvent", new
                {
                    type = "keyDown", text = key, unmodifiedText = key, key, code,
                    windowsVirtualKeyCode = (int)virtualKey
                });
                Send("Input.dispatchKeyEvent", new
                {
                    type = "keyUp", key, code, windowsVirtualKeyCode = (int)virtualKey
                });
            }
            Thread.Sleep(30);
        }

        private void SelectAllKeys(string id)
        {
            Click(id);
            Send("Input.dispatchKeyEvent", new
            {
                type = "keyDown", key = "Control", code = "ControlLeft", windowsVirtualKeyCode = 17, modifiers = 0
            });
            Send("Input.dispatchKeyEvent", new
            {
                type = "keyDown", key = "a", code = "KeyA", windowsVirtualKeyCode = 65, modifiers = 2
            });
            Send("Input.dispatchKeyEvent", new
            {
                type = "keyUp", key = "a", code = "KeyA", windowsVirtualKeyCode = 65, modifiers = 2
            });
            Send("Input.dispatchKeyEvent", new
            {
                type = "keyUp", key = "Control", code = "ControlLeft", windowsVirtualKeyCode = 17, modifiers = 0
            });
            Thread.Sleep(30);
        }

        private void Key(string key, string code, int virtualKey, int modifiers)
        {
            Send("Input.dispatchKeyEvent", new
            {
                type = "rawKeyDown", key, code, windowsVirtualKeyCode = virtualKey, modifiers
            });
            Send("Input.dispatchKeyEvent", new
            {
                type = "keyUp", key, code, windowsVirtualKeyCode = virtualKey, modifiers
            });
        }

        private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("'", "\\'");

        private TaskCompletionSource<JsonElement> WaitForEvent(string method)
        {
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_events) _events.Add((method, tcs));
            return tcs;
        }

        private JsonElement Send(string method, object? parameters)
        {
            var id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            _send.Wait();
            try
            {
                _socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            finally
            {
                _send.Release();
            }

            if (!tcs.Task.Wait(TimeSpan.FromSeconds(20)))
            {
                throw new TimeoutException("CDP command timed out: " + method);
            }

            var value = tcs.Task.Result;
            if (value.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException("CDP " + method + " failed: " + error.GetRawText());
            }

            return value.TryGetProperty("result", out var result) ? result : default;
        }

        public void Close()
        {
            _stop.Cancel();
            try { _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).Wait(2000); }
            catch { /* best effort */ }
        }

        public void Dispose()
        {
            Close();
            _socket.Dispose();
            _send.Dispose();
            _stop.Dispose();
        }
    }
}
