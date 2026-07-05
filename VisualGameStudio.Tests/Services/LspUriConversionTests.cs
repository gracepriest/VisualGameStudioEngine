using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class LspUriConversionTests
{
    [Test]
    public void PathToUri_SimplePath_ProducesFileUri()
    {
        var uri = LanguageService.PathToUri(@"C:\projects\test.bas");

        Assert.That(uri, Is.EqualTo("file:///C:/projects/test.bas"));
    }

    [Test]
    public void PathToUri_PathWithSpaces_EncodesSpaces()
    {
        var uri = LanguageService.PathToUri(@"C:\my projects\test.bas");

        Assert.That(uri, Is.EqualTo("file:///C:/my%20projects/test.bas"));
    }

    [Test]
    public void PathToUri_PathWithHash_EncodesHash()
    {
        var uri = LanguageService.PathToUri(@"C:\projects\game#2\main.bas");

        Assert.That(uri, Does.Contain("%232"), "the '#' must be percent-encoded, not treated as a fragment");
        Assert.That(uri, Does.Not.Contain("#"));
    }

    [Test]
    public void PathToUri_PathWithPercent_EncodesPercent()
    {
        var uri = LanguageService.PathToUri(@"C:\projects\100%done\main.bas");

        Assert.That(uri, Does.Contain("%25done"));
    }

    [Test]
    public void PathToUri_AlreadyAUri_PassesThrough()
    {
        var uri = LanguageService.PathToUri("file:///C:/projects/test.bas");

        Assert.That(uri, Is.EqualTo("file:///C:/projects/test.bas"));
    }

    [TestCase(@"C:\projects\test.bas")]
    [TestCase(@"C:\my projects\test.bas")]
    [TestCase(@"C:\projects\game#2\main.bas")]
    [TestCase(@"C:\projects\100%done\main.bas")]
    [TestCase(@"C:\prøjekt\smörgås.bas")]
    public void PathToUri_UriToPath_RoundTripsExactly(string path)
    {
        var uri = LanguageService.PathToUri(path);
        var roundTripped = LanguageService.UriToPath(uri);

        Assert.That(roundTripped, Is.EqualTo(path));
    }

    [Test]
    public void UriToPath_NonFileUri_PassesThrough()
    {
        Assert.That(LanguageService.UriToPath("untitled:Untitled-1"), Is.EqualTo("untitled:Untitled-1"));
    }

    [Test]
    public void UriToPath_LegacySpaceEncoding_StillDecodes()
    {
        var path = LanguageService.UriToPath("file:///C:/my%20projects/test.bas");

        Assert.That(path, Is.EqualTo(@"C:\my projects\test.bas"));
    }
}
