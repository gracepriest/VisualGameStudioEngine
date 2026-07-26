using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raudio Batch audio-A3 (raw raylib Music streaming passthroughs, coexisting with
/// the handle-based Framework_*MusicH layer). Pure text scan — no engine load. Unlike A2 (Sound), NONE of these names
/// collide with the legacy convenience helpers: raylib's raw Music names carry a "Stream" suffix
/// (LoadMusicStream/PlayMusicStream/StopMusicStream) while the un-suffixed Framework_LoadMusic/PlayMusic/StopMusic
/// helpers keep their handle-based (As Integer) meaning — so all 16 bindings use raylib's exact spelling with no
/// "Raw" suffix / EntryPoint aliasing. The trailing '(' anchor keeps near-name boundaries apart
/// (Framework_LoadMusicStream( != Framework_LoadMusicStreamFromMemory(; Framework_SetMusicVolume( !=
/// Framework_SetMusicVolumeH(; Framework_IsMusicValid( != Framework_IsMusicValidH().
/// </summary>
[TestFixture]
public class RaylibAudioA3ParityTests
{
    // All 16 raw Music names bind unsuffixed (import name == export name) — no legacy collision (the "Stream" suffix
    // distinguishes them from the un-suffixed handle-based convenience helpers).
    private static readonly string[] MusicNames =
    {
        "LoadMusicStream", "LoadMusicStreamFromMemory", "IsMusicValid", "UnloadMusicStream", "PlayMusicStream",
        "IsMusicStreamPlaying", "UpdateMusicStream", "StopMusicStream", "PauseMusicStream", "ResumeMusicStream",
        "SeekMusicStream", "SetMusicVolume", "SetMusicPitch", "SetMusicPan", "GetMusicTimeLength", "GetMusicTimePlayed",
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
    public void Every_audio_A3_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in MusicNames)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(MusicNames.Length, Is.EqualTo(16));
        });
    }
}
