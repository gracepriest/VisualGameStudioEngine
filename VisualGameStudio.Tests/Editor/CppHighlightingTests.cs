using AvaloniaEdit.Highlighting;
using NUnit.Framework;
using VisualGameStudio.Editor.Highlighting;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class CppHighlightingTests
{
    [Test]
    public void RegisterHighlighting_RegistersThemedCppDefinition()
    {
        HighlightingLoader.RegisterHighlighting();

        var byName = HighlightingManager.Instance.GetDefinition("C++");
        Assert.That(byName, Is.Not.Null);

        // Ours (VS Code dark palette) shadows AvaloniaEdit's light-only built-in:
        // the built-in's Comment color is Green (#FF008000); ours is #6A9955.
        var comment = byName!.GetNamedColor("Comment");
        Assert.That(comment, Is.Not.Null, "themed definition must define a Comment color");
        Assert.That(comment!.Foreground!.ToString(), Does.Contain("6A9955").IgnoreCase);
    }

    [TestCase(".cpp")]
    [TestCase(".h")]
    [TestCase(".hpp")]
    [TestCase(".cc")]
    [TestCase(".cxx")]
    public void ExtensionLookup_ResolvesCpp(string ext)
    {
        HighlightingLoader.RegisterHighlighting();
        var def = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Name, Is.EqualTo("C++"));
    }

    [Test]
    public void UpdateForTheme_Light_RegistersLightCppPalette()
    {
        HighlightingLoader.RegisterHighlighting();
        try
        {
            HighlightingLoader.UpdateForTheme(isDark: false);

            var def = HighlightingManager.Instance.GetDefinition("C++");
            Assert.That(def, Is.Not.Null);

            // CppLight.xshd mirrors BasicLangLight's Comment hex (#008000).
            // AvaloniaEdit prints known named colors by name: #008000 == Green.
            var comment = def!.GetNamedColor("Comment");
            Assert.That(comment, Is.Not.Null, "light C++ definition must define a Comment color");
            Assert.That(comment!.Foreground!.ToString(),
                Does.Contain("008000").IgnoreCase.Or.EqualTo("Green"));
        }
        finally
        {
            // HighlightingManager is process-global; restore the dark default
            // so other fixtures see the same registrations they started with.
            HighlightingLoader.UpdateForTheme(isDark: true);
        }
    }
}
