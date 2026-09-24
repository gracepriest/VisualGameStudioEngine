using System;
using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests;

/// <summary>
/// Skipping a test from code that may run INSIDE an <c>Assert.Multiple</c> block.
///
/// <para>⛔ NUnit refuses <c>Assert.Ignore</c> inside a multiple-assertion block ("Assert.Ignore
/// may not be used in a multiple assertion block") — it FAILS the test instead of skipping it.
/// Helpers that discover a missing prerequisite mid-block (no <c>ilasm</c>, no restored raylib
/// header) must go through here.</para>
/// </summary>
internal static class TestSkip
{
    /// <summary>
    /// Skip the current test. Outside a block this is plain <c>Assert.Ignore</c>. Inside one, the
    /// skip is raised as a bare <c>IgnoreException</c>, which NUnit records as Ignored — unless an
    /// assertion earlier in the same block already FAILED: then those failures are thrown instead,
    /// so a skip can never hide a real failure. Assertions after the call in the block do not run.
    /// </summary>
    [DoesNotReturn]
    public static void IgnoreEvenInsideMultiple(string message)
    {
        // NUnit 4 keeps the "am I inside Assert.Multiple" level internal, so ask Assert.Ignore:
        // outside a block it throws IgnoreException (let it go); inside one it throws a plain
        // Exception refusing to run, which is the case handled below.
        try
        {
            Assert.Ignore(message);
        }
        catch (Exception ex) when (ex is not IgnoreException)
        {
            var result = NUnit.Framework.Internal.TestExecutionContext.CurrentContext.CurrentResult;
            if (result.PendingFailures > 0)
                throw new MultipleAssertException(result);
            throw new IgnoreException(message);
        }
        throw new InvalidOperationException("unreachable: Assert.Ignore returned");
    }
}
