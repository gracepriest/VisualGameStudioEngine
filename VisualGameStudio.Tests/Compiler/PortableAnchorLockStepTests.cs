using System.Globalization;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 33's mirrored axis pair: the portable library's <c>VgsWriteGeometry</c> must produce the
/// same declarations as <see cref="FormAnchorCss.Positioned"/> for all sixteen anchor combinations.
/// One node process drives the library; the C# side composes the expectation.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PortableAnchorLockStepTests
{
    [Test]
    public void TheLibraryAxis_MatchesFormAnchorCss_ForAllSixteenCombinations()
    {
        var result = PortableControlNodeTests.Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Sub Main()
                    Dim f As New Form()
                    Dim p As New Panel()
                    p.Name = "panel"
                    f.Controls.Add(p)
                    Dim b As New Button()
                    b.Name = "btn"
                    p.Controls.Add(b)

                    Dim host As Element = ::document.getElementById("panel")
                    host.setAttribute("data-vgs-inset", "0")
                    Dim rawHost As Object = host
                    rawHost._rectLeft = 0
                    rawHost._rectTop = 0
                    rawHost._rectWidth = 300
                    rawHost._rectHeight = 200
                    rawHost._clientWidth = 300
                    rawHost._clientHeight = 200

                    Dim el As Element = ::document.getElementById("btn")
                    Dim raw As Object = el
                    raw._rectLeft = 30
                    raw._rectTop = 22
                    raw._rectWidth = 80
                    raw._rectHeight = 40

                    For a As Integer = 0 To 15
                        b.Anchor = CType(a, AnchorStyles)
                        Console.WriteLine(b.VgsStyleRule())
                    Next
                End Sub
            End Module
            """);

        Assert.That(result.Ok, Is.True, result.Errors);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(16));

        Assert.Multiple(() =>
        {
            for (var anchor = 0; anchor < 16; anchor++)
            {
                var names = new List<string>();
                if ((anchor & 1) != 0) names.Add("Top");
                if ((anchor & 2) != 0) names.Add("Bottom");
                if ((anchor & 4) != 0) names.Add("Left");
                if ((anchor & 8) != 0) names.Add("Right");
                var geometry = new PixelGeometry
                {
                    X = 30, Y = 22, Width = 80, Height = 40,
                    Anchor = names.Count == 0 ? "None" : string.Join(",", names)
                };
                var expected = string.Concat(
                    FormAnchorCss.Positioned(geometry, 300, 200)
                        .Select(p => p.Property + ": " + p.Value + ";"));
                Assert.That(lines[anchor], Is.EqualTo(expected), $"anchor {anchor}");
            }
        });
    }
}
