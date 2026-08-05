using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Guards the JSON-RPC parameter shape the extension host speaks.
///
/// <para>Every handler in <c>Services/ExtensionHost/main.js</c> destructures its argument by NAME
/// (<c>params.extensionPath</c>, <c>params.command</c>, …), while every C# call site sent
/// POSITIONAL arguments. At runtime that produced <c>params.extensionPath === undefined</c> and
/// <c>TypeError [ERR_INVALID_ARG_TYPE]: The "path" argument must be of type string</c> — the very
/// first thing that happened once a real extension was finally activated. An audit of the whole
/// contract found 27 of 29 outbound methods affected; the only two that worked were the only two
/// that pass no arguments at all.</para>
///
/// <para><b>The non-obvious part:</b> choosing <c>NotifyAsync</c> over
/// <c>InvokeWithCancellationAsync</c> does not help. <c>NotifyAsync(string, object)</c> takes a
/// SINGLE POSITIONAL argument (StreamJsonRpc 2.18.48 documents it as
/// <c>&lt;param name="argument"&gt;Method argument&lt;/param&gt;</c>, singular), so passing an
/// anonymous object still emits <c>"params":[{…}]</c> — an object wrapped in an array. Only the
/// <c>*WithParameterObjectAsync</c> forms emit a bare object. Named-looking C# is not named JSON,
/// and the method names actively mislead here.</para>
///
/// <para>Correct forms:
/// <c>InvokeWithParameterObjectAsync&lt;T&gt;(name, argument, cancellationToken)</c> — the token is
/// the third positional parameter — and <c>NotifyWithParameterObjectAsync(name, argument)</c>,
/// which has no CancellationToken overload.</para>
///
/// <para>⛔ Two traps that follow from this: StreamJsonRpc OMITS properties whose value is null, so
/// a JS handler must treat missing as null; and NOTHING camel-cases anything on this channel —
/// property names go on the wire verbatim, so renaming a C# local from <c>extensionId</c> to
/// <c>ExtensionId</c> silently re-breaks the call with no compiler complaint.</para>
/// </summary>
[TestFixture]
public class ExtensionHostRpcShapeTests
{
    private static string ExtensionHostSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "VisualGameStudio.ProjectSystem", "Services", "ExtensionHost.cs");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        Assert.Fail("could not locate ExtensionHost.cs from the test output directory");
        return "";
    }

    /// <summary>
    /// Every outbound call that carries data must use a parameter-object form. The positional forms
    /// structurally cannot express what main.js reads: their argument-carrying overloads take an
    /// <c>IReadOnlyList</c>, so they always emit an array.
    ///
    /// <para>Zero-argument calls are exempt and are matched by the <c>cancellationToken:</c> named
    /// argument they use — with no payload there is nothing to name and nothing to mismatch. That
    /// is exactly why <c>heartbeat</c> and <c>shutdown</c> were the only two outbound methods that
    /// ever worked.</para>
    /// </summary>
    [Test]
    public void ExtensionHost_SendsNoPositionalArguments()
    {
        var offenders = ExtensionHostSource()
            .Split('\n')
            .Select((line, i) => (line: line.Trim(), no: i + 1))
            .Where(x => x.line.Contains("InvokeWithCancellationAsync") || x.line.Contains("_rpc.NotifyAsync("))
            .Where(x => !x.line.Contains("cancellationToken:"))
            .Select(x => $"  line {x.no}: {x.line}")
            .ToList();

        Assert.That(offenders, Is.Empty,
            "these call sites send positional arguments to handlers that read named properties, so "
            + "every field arrives as undefined. Use InvokeWithParameterObjectAsync / "
            + "NotifyWithParameterObjectAsync instead.\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// The two lifecycle calls declared <c>Task&lt;bool&gt;</c> while their handlers return objects
    /// (<c>{ activated, hasMain }</c> / <c>{ deactivated }</c>), which failed as
    /// "Error reading boolean. Unexpected token: StartObject". Deserializing them as <c>bool</c>
    /// must not come back.
    /// </summary>
    [Test]
    public void LifecycleCalls_DoNotDeserializeObjectResultsAsBool()
    {
        var src = ExtensionHostSource();

        // Neither lifecycle method may ask StreamJsonRpc for a bool result.
        foreach (var method in new[] { "activateExtension", "deactivateExtension" })
        {
            var idx = src.IndexOf($"\"{method}\"", StringComparison.Ordinal);
            Assert.That(idx, Is.GreaterThan(-1), $"premise: {method} is still called");

            // Look back a short window to the invoking generic argument.
            var window = src.Substring(Math.Max(0, idx - 200), Math.Min(200, idx));
            Assert.That(window, Does.Not.Contain("<bool>"),
                $"{method}'s handler returns an object; asking StreamJsonRpc for a bool throws "
                + "\"Error reading boolean. Unexpected token: StartObject\" and the activation is "
                + "reported as failed even when it succeeded");
        }
    }
}
