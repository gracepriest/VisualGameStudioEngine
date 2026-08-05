using System.Text.Json;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Binds the exact JSON the Node host returns, under the exact serializer the channel now uses.
///
/// <para>This exists because of a real regression. The channel originally ran the Newtonsoft
/// formatter, which matches property names case-INSENSITIVELY, so PascalCase DTO members bound the
/// host's camelCase JSON for free. Swapping in <c>SystemTextJsonFormatter</c> — necessary so that
/// <c>JsonElement</c> parameters and results stop arriving empty — changed the matching to
/// case-SENSITIVE. Every property silently fell back to its default: <c>Activated</c> became
/// <c>false</c>, and a successful activation was reported to the user as
/// "JS activation failed".</para>
///
/// <para>Nothing threw and the whole suite stayed green, because the only observable difference was
/// a boolean flipping in a log line. These tests use the literal payloads from
/// <c>main.js</c> so a future formatter or naming-policy change fails here instead of in the
/// Output pane.</para>
/// </summary>
[TestFixture]
public class ExtensionHostResultBindingTests
{
    /// <summary>Default options — deliberately NOT case-insensitive, matching the live channel.</summary>
    private static readonly JsonSerializerOptions ChannelDefaults = new();

    /// <summary>main.js:233 — the success path for an extension with a JS entry point.</summary>
    [Test]
    public void ActivationResult_BindsTheHostsSuccessPayload()
    {
        var result = JsonSerializer.Deserialize<ExtensionHost.ActivationResult>(
            """{ "activated": true, "hasMain": true }""", ChannelDefaults);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Activated, Is.True,
            "a false here is reported to the user as 'JS activation failed' even though the "
            + "extension activated successfully");
        Assert.That(result.HasMain, Is.True);
    }

    /// <summary>main.js:156/171 — a static-only extension activates with no JS entry point.</summary>
    [Test]
    public void ActivationResult_BindsTheStaticOnlyPayload()
    {
        var result = JsonSerializer.Deserialize<ExtensionHost.ActivationResult>(
            """{ "activated": true, "hasMain": false }""", ChannelDefaults);

        Assert.That(result!.Activated, Is.True);
        Assert.That(result.HasMain, Is.False);
    }

    /// <summary>main.js:241 — the host's own failure message must survive, or the user sees nothing useful.</summary>
    [Test]
    public void ActivationResult_CarriesTheHostsErrorMessage()
    {
        var result = JsonSerializer.Deserialize<ExtensionHost.ActivationResult>(
            """{ "activated": false, "error": "Cannot find module 'vscode'" }""", ChannelDefaults);

        Assert.That(result!.Activated, Is.False);
        Assert.That(result.Error, Is.EqualTo("Cannot find module 'vscode'"),
            "without the host's message the user gets a bare 'activation failed' and no cause");
    }

    /// <summary>main.js:271/273.</summary>
    [Test]
    public void DeactivationResult_BindsTheHostsPayloads()
    {
        var ok = JsonSerializer.Deserialize<ExtensionHost.DeactivationResult>(
            """{ "deactivated": true }""", ChannelDefaults);
        var failed = JsonSerializer.Deserialize<ExtensionHost.DeactivationResult>(
            """{ "deactivated": false, "error": "Extension not loaded" }""", ChannelDefaults);

        Assert.That(ok!.Deactivated, Is.True);
        Assert.That(failed!.Deactivated, Is.False);
        Assert.That(failed.Error, Is.EqualTo("Extension not loaded"));
    }
}
