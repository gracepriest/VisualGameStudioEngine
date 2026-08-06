using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Guards the JS→C# half of the extension-host contract.
///
/// <para>The outbound half was positional-vs-named (fixed in b60aa6e). This half fails differently:
/// the Node side already sends correct NAMED objects, and StreamJsonRpc binds them to handler
/// parameters by EXACT, CASE-SENSITIVE NAME. So a handler whose parameter names or arity disagree
/// simply never binds — and because these are NOTIFICATIONS, no error response is produced and
/// nothing is logged. The call vanishes.</para>
///
/// <para><c>languages/registerProvider</c> is the one that matters most: all 30
/// <c>registerXxxProvider</c> entry points funnel through it, and until it binds
/// <c>ProviderRegistered</c> never fires, <c>_extensionProviderLanguages</c> stays empty,
/// <c>HasExtensionProviders</c> is permanently false, and the IDE never even ASKS an extension for
/// hover or completion. Every downstream fix is invisible until this one lands.</para>
/// </summary>
[TestFixture]
public class ExtensionHostInboundBindingTests
{
    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, Path.Combine(parts));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        Assert.Fail($"could not locate {Path.Combine(parts)}");
        return "";
    }

    private static string HostSource() =>
        Source("VisualGameStudio.ProjectSystem", "Services", "ExtensionHost.cs");

    /// <summary>
    /// The handler's parameters must match the five keys languages.js actually sends:
    /// <c>{ type, id, extensionId, selector, metadata }</c>. The old signature took four
    /// parameters in a different order with two different names, so it never bound.
    /// </summary>
    [Test]
    public void RegisterProvider_BindsTheShapeTheHostSends()
    {
        var src = HostSource();

        Assert.That(src, Does.Not.Contain("new Action<string, string, string?, string?>(OnRegisterProvider)"),
            "the old 4-parameter registration cannot bind the 5-key notification languages.js sends");

        // Names only — deliberately NOT pinning types here. An earlier version of this test asserted
        // "string id", which baked in the wrong contract: provider-registry.js sends a NUMERIC id,
        // so that assertion was green while the wire stayed broken. Types are pinned by
        // RegisterProvider_TakesTheIdAsANumber, against the JS that actually produces the value.
        foreach (var name in new[] { "type", "id", "extensionId", "selector", "metadata" })
        {
            Assert.That(SignatureOf("OnRegisterProvider("), Does.Contain(name),
                $"OnRegisterProvider must accept '{name}' — StreamJsonRpc binds named arguments by "
                + "exact parameter name, so a rename silently unbinds the whole call");
        }
    }

    /// <summary>
    /// languages.js sends <c>metadata: metadata || undefined</c>, which OMITS the key entirely when
    /// there is no metadata. A parameter with no default fails binding when its key is absent — the
    /// same defect that kills treeView/create — so the metadata parameter must be optional.
    /// </summary>
    [Test]
    public void RegisterProvider_ToleratesAnOmittedMetadataKey()
    {
        var src = HostSource();
        var idx = src.IndexOf("OnRegisterProvider(", StringComparison.Ordinal);
        Assert.That(idx, Is.GreaterThan(-1), "premise: the handler still exists");

        var signature = src.Substring(idx, Math.Min(400, src.Length - idx));
        var close = signature.IndexOf(')');
        Assert.That(close, Is.GreaterThan(-1));
        signature = signature.Substring(0, close);

        Assert.That(signature, Does.Contain("metadata"),
            "premise: the handler takes metadata");
        Assert.That(signature, Does.Contain("= default").Or.Contain("= null"),
            "metadata must have a default: languages.js omits the key when there is no metadata, "
            + "and a required parameter whose key is absent fails to bind — silently, because this "
            + "is a notification");
    }

    /// <summary>
    /// The channel is constructed with <c>new HeaderDelimitedMessageHandler(stdin, stdout)</c>, the
    /// no-formatter overload, which defaults to the NEWTONSOFT formatter. Newtonsoft cannot
    /// materialize a <c>System.Text.Json.JsonElement</c>: it yields <c>default(JsonElement)</c>
    /// silently rather than throwing. Every JsonElement parameter and every
    /// <c>JsonElement?</c> result on this channel therefore arrives EMPTY — which covers the
    /// diagnostics payload, applyEdit, all 12 provider results and both tree-view results.
    /// </summary>
    [Test]
    public void TheChannelUsesASystemTextJsonFormatter()
    {
        var src = HostSource();

        Assert.That(src, Does.Contain("SystemTextJsonFormatter"),
            "the message handler must be constructed with an explicit SystemTextJsonFormatter; the "
            + "default is Newtonsoft, which cannot materialize System.Text.Json.JsonElement and "
            + "silently substitutes an empty one");
    }

    /// <summary>
    /// A <c>JsonSerializerOptions</c> configured for camelCase and case-insensitive matching sat in
    /// this file, unreferenced — System.Text.Json configuration on a Newtonsoft channel. It is
    /// doubly inert and actively misleading, because the wire binding it appears to describe is
    /// case-SENSITIVE.
    /// </summary>
    [Test]
    public void NoInertSerializerOptionsAdvertiseCaseInsensitiveBinding()
    {
        // Matches the ASSIGNMENT, not the bare identifier: the explanatory comment left in its
        // place names the setting deliberately, and prose should not fail a test about code.
        Assert.That(HostSource(), Does.Not.Contain("PropertyNameCaseInsensitive ="),
            "named-argument binding on this channel is case-SENSITIVE; leaving unreferenced options "
            + "that claim otherwise invites a rename that silently unbinds a handler");
    }

    /// <summary>
    /// <c>languages/publishDiagnostics</c> is what carries an extension's diagnostics to the
    /// Problems panel — the whole point of running ESLint. <c>languages.js:227</c> sends the key
    /// <c>collection</c>; the handler named it <c>collectionName</c>, and since that parameter had
    /// no default the ENTIRE invocation failed to bind, taking <c>uri</c> and <c>diagnostics</c>
    /// with it. It is a notification, so nothing was logged and nothing failed visibly.
    ///
    /// <para>Fired from every DiagnosticCollection mutation (set/delete/clear), so no extension
    /// diagnostic has ever reached the IDE.</para>
    /// </summary>
    [Test]
    public void PublishDiagnostics_BindsTheKeyTheHostSends()
    {
        var signature = SignatureOf("OnPublishDiagnostics(");

        Assert.That(signature, Does.Contain("collection"),
            "languages.js sends `collection`; a parameter named anything else fails to bind");
        Assert.That(signature, Does.Not.Contain("collectionName"),
            "`collectionName` is the name that never bound — StreamJsonRpc matches named arguments "
            + "by exact parameter name");
        Assert.That(signature, Does.Contain("= null").Or.Contain("= default"),
            "a collection-less publish must still bind; a required parameter whose key is absent "
            + "fails the whole invocation");
    }

    /// <summary>
    /// The only inbound mismatch that fails LOUDLY — and it kills extensions outright.
    /// <c>window.js:81-87</c> sends five keys <c>{ type, message, options, items, extensionId }</c>
    /// against a four-parameter handler with different names, producing
    /// <c>-32602 ... supplies 5</c>. Because this is a REQUEST rather than a notification the JS
    /// promise REJECTS, so <c>vscode.window.showInformationMessage(...)</c> throws inside the
    /// extension — and an unhandled rejection inside <c>activate()</c> aborts activation.
    /// </summary>
    [TestCase("type")]
    [TestCase("message")]
    [TestCase("options")]
    [TestCase("items")]
    [TestCase("extensionId")]
    public void ShowMessage_BindsEveryKeyTheHostSends(string key)
    {
        Assert.That(SignatureOf("OnShowMessageAsync("), Does.Contain(key),
            $"window.js sends '{key}'; a handler missing it cannot bind, and because showMessage is "
            + "a request the rejection propagates into the extension and aborts its activation");
    }

    /// <summary>
    /// Names are not enough — TYPES bind too, and a type mismatch fails just as silently.
    ///
    /// <para><c>provider-registry.js:243</c> allocates ids with <c>const id = _nextId++</c>, so
    /// <c>id</c> arrives as a JSON NUMBER (the file's own comment at :251 calls it a "numeric id").
    /// Declared as <c>string</c>, StreamJsonRpc cannot bind it and drops the whole notification —
    /// no error, no log, and <c>ProviderRegistered</c> never fires.</para>
    ///
    /// <para>This got past the first fix because that fix, and its guard, checked only that the
    /// five parameter NAMES matched. Verified at runtime: the extension logged
    /// "registerHoverProvider returned" while the IDE never logged "Provider registered".</para>
    /// </summary>
    [Test]
    public void RegisterProvider_TakesTheIdAsANumber()
    {
        var signature = SignatureOf("OnRegisterProvider(");

        Assert.That(signature, Does.Not.Contain("string id"),
            "provider-registry.js allocates ids as numbers; a string parameter cannot bind one, and "
            + "a notification that fails to bind is dropped in silence");
        Assert.That(signature, Does.Match(@"\b(int|long|double|JsonElement)\s+id\b"),
            "id must accept a JSON number");
    }

    /// <summary>Extracts a method's parameter list so assertions cannot match unrelated code.</summary>
    private static string SignatureOf(string methodPrefix)
    {
        var src = HostSource();
        var idx = src.IndexOf(methodPrefix, StringComparison.Ordinal);
        Assert.That(idx, Is.GreaterThan(-1), $"premise: {methodPrefix} still exists");

        var close = src.IndexOf(')', idx);
        Assert.That(close, Is.GreaterThan(-1));
        return src.Substring(idx, close - idx);
    }
}
