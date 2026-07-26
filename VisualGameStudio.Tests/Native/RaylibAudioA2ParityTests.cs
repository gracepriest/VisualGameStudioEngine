using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raudio Batch audio-A2 (raw raylib Sound passthroughs, coexisting with the
/// handle-based Framework_*SoundH layer). Pure text scan — no engine load. The raw engine C-ABI export names in
/// framework.h are always unsuffixed raylib names. On the VB side, four of those names (LoadSound, PlaySound,
/// StopSound, SetSoundVolume) were already taken by legacy handle-based "Sound Convenience Functions", so the raw
/// struct bindings there carry a "Raw" suffix and bind their export via <c>EntryPoint:="Framework_&lt;name&gt;"</c>;
/// this test verifies those four by their EntryPoint, and the other eleven by their unsuffixed import name. The
/// trailing '(' / '"' anchors keep near-name boundaries apart (Framework_LoadSound( != Framework_LoadSoundH( /
/// Framework_LoadSoundFromWave(; Framework_UnloadSound( != Framework_UnloadSoundAlias().
/// </summary>
[TestFixture]
public class RaylibAudioA2ParityTests
{
    // Raw names whose VB binding is unsuffixed (import name == export name).
    private static readonly string[] Unsuffixed =
    {
        "LoadSoundFromWave", "LoadSoundAlias", "IsSoundValid", "UpdateSound", "UnloadSound",
        "UnloadSoundAlias", "PauseSound", "ResumeSound", "IsSoundPlaying", "SetSoundPitch", "SetSoundPan",
    };

    // Raw names the legacy handle layer already squats: engine export is unsuffixed, VB binds it via EntryPoint.
    private static readonly string[] EntryPointBound =
    {
        "LoadSound", "PlaySound", "StopSound", "SetSoundVolume",
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
    public void Every_audio_A2_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            // Every raw name is an unsuffixed export in framework.h.
            foreach (var name in Unsuffixed)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            foreach (var name in EntryPointBound)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"EntryPoint:=\"Framework_{name}\""), Is.True,
                    $"RaylibWrapper.vb missing EntryPoint binding for Framework_{name} (legacy handle helper squats the plain name)");
            }
            Assert.That(Unsuffixed.Length + EntryPointBound.Length, Is.EqualTo(15));
        });
    }
}
