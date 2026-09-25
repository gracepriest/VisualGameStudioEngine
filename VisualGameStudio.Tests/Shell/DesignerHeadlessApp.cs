using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(VisualGameStudio.Tests.Shell.DesignerHeadlessApp))]

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// The Avalonia application <c>[AvaloniaTest]</c> methods run inside.
///
/// <para>⛔ Why this exists at all: the designer canvas and the toolbox drag are VIEW code, and
/// view code is where every unreachable piece of this feature hid. <c>FormCanvasControl</c> still
/// carries a "not visually verified" note saying the canvas is checked by running the IDE and
/// nothing else. That was true when it was written and is not true now — headless Avalonia drives
/// the real control, and with Skia it renders real pixels.</para>
///
/// <para>⚠ <b>Skia is required, not optional.</b> <c>UseHeadlessDrawing = true</c> is the default
/// and makes <c>CaptureRenderedFrame</c> throw — a renderless headless platform draws nothing, so a
/// pixel comparison has nothing to compare. <c>UseSkia()</c> plus
/// <c>UseHeadlessDrawing = false</c> is the combination that renders.</para>
///
/// <para>⚠ FluentTheme is needed for templated controls (a bare <c>ListBox</c> realises no item
/// containers without it), and costs nothing for the canvas, which is drawn directly.</para>
/// </summary>
public static class DesignerHeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .AfterSetup(_ => Application.Current!.Styles.Add(new FluentTheme()));
}
