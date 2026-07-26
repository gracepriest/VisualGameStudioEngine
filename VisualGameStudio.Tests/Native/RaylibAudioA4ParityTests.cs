using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raudio Batch audio-A4 (raw raylib low-level AudioStream passthroughs). Pure text
/// scan — no engine load. None of the 14 names collide with the legacy convenience helpers or the handle-based layer,
/// so all bind unsuffixed with raylib's exact spelling. The trailing '(' anchor keeps near-name boundaries apart
/// (Framework_IsAudioStreamValid( != Framework_IsAudioStreamPlaying( != Framework_IsAudioStreamProcessed().
///
/// This closes the raudio passthrough surface: A1 (device+Wave) + A2 (Sound) + A3 (Music) + A4 (AudioStream) = 59
/// passthroughs; the 5 AudioCallback fn-pointer fns stay deferred to a dedicated callbacks batch.
/// </summary>
[TestFixture]
public class RaylibAudioA4ParityTests
{
    // All 14 raw AudioStream names bind unsuffixed (import name == export name) — no collision.
    private static readonly string[] StreamNames =
    {
        "LoadAudioStream", "IsAudioStreamValid", "UnloadAudioStream", "UpdateAudioStream", "IsAudioStreamProcessed",
        "PlayAudioStream", "PauseAudioStream", "ResumeAudioStream", "IsAudioStreamPlaying", "StopAudioStream",
        "SetAudioStreamVolume", "SetAudioStreamPitch", "SetAudioStreamPan", "SetAudioStreamBufferSizeDefault",
    };

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (; d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "VisualGameStudioEngine.sln")))
                return d.FullName;
        throw new DirectoryNotFoundException("VisualGameStudioEngine.sln not found above " + AppContext.BaseDirectory);
    }

    [Test]
    public void Every_audio_A4_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in StreamNames)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(StreamNames.Length, Is.EqualTo(14));
        });
    }
}
