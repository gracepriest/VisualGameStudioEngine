using System.Collections.Generic;
using NUnit.Framework;
using VisualGameStudio.Shell;

namespace VisualGameStudio.Tests;

/// <summary>
/// Pins the pure add/remove decision behind <see cref="ThemeManager.Register"/> and the global
/// per-window Loaded hook: in High Contrast the <c>highContrast</c> style class is stamped on a
/// window's class list (idempotently), and it is removed when leaving High Contrast. The decision is
/// factored onto <see cref="IList{T}"/> precisely so it can be proven here without an Avalonia
/// platform (the suite is headless and cannot construct a <see cref="Avalonia.Controls.Window"/>).
/// The wiring — one class handler per window plus the two early-registration sites — is covered by
/// the manual/screenshot smoke, since it needs a live window.
/// </summary>
[TestFixture]
public class HighContrastWindowClassTests
{
    [Test]
    public void ApplyHighContrastClass_WhenHighContrastAndAbsent_AddsClass()
    {
        var classes = new List<string>();

        ThemeManager.ApplyHighContrastClass(classes, highContrast: true);

        Assert.That(classes, Does.Contain("highContrast"));
    }

    [Test]
    public void ApplyHighContrastClass_WhenHighContrastAndAlreadyPresent_IsIdempotent()
    {
        var classes = new List<string> { "highContrast" };

        ThemeManager.ApplyHighContrastClass(classes, highContrast: true);

        Assert.That(classes.FindAll(c => c == "highContrast").Count, Is.EqualTo(1),
            "the class must not be duplicated when it is already present");
    }

    [Test]
    public void ApplyHighContrastClass_WhenNotHighContrastAndPresent_RemovesClass()
    {
        var classes = new List<string> { "someOtherClass", "highContrast" };

        ThemeManager.ApplyHighContrastClass(classes, highContrast: false);

        Assert.That(classes, Does.Not.Contain("highContrast"));
        Assert.That(classes, Does.Contain("someOtherClass"),
            "removing the HC class must leave unrelated classes untouched");
    }

    [Test]
    public void ApplyHighContrastClass_WhenNotHighContrastAndAbsent_IsNoOp()
    {
        var classes = new List<string> { "someOtherClass" };

        ThemeManager.ApplyHighContrastClass(classes, highContrast: false);

        Assert.That(classes, Is.EqualTo(new[] { "someOtherClass" }));
    }
}
