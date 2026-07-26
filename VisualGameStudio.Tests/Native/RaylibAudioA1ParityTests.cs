using System;
using System.IO;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Engine⇄wrapper sync invariant for raudio Batch audio-A1 (raw raylib device + Wave passthroughs,
/// coexisting with the custom Framework_Audio_*/…H layer). Pure text scan — no engine load. SetMasterVolume
/// and GetMasterVolume are part of the raudio device group but were bound in an earlier batch; they are
/// asserted present here (parity) but were not (re)added by A1. Trailing '(' anchors near-name boundaries
/// (Framework_LoadWave( != Framework_LoadWaveFromMemory(; Framework_LoadWaveSamples( != Framework_UnloadWaveSamples().
/// </summary>
[TestFixture]
public class RaylibAudioA1ParityTests
{
    private static readonly string[] BatchA1 =
    {
        // Device (SetMasterVolume/GetMasterVolume pre-existing but part of the surface)
        "InitAudioDevice", "CloseAudioDevice", "IsAudioDeviceReady", "SetMasterVolume", "GetMasterVolume",
        // Wave
        "LoadWave", "LoadWaveFromMemory", "IsWaveValid", "UnloadWave", "ExportWave", "ExportWaveAsCode",
        "WaveCopy", "WaveCrop", "WaveFormat", "LoadWaveSamples", "UnloadWaveSamples",
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
    public void Every_audio_A1_export_has_a_matching_wrapper_import()
    {
        var root = RepoRoot();
        var header = File.ReadAllText(Path.Combine(root, "VisualGameStudioEngine", "framework.h"));
        var wrapper = File.ReadAllText(Path.Combine(root, "RaylibWrapper", "RaylibWrapper.vb"));

        Assert.Multiple(() =>
        {
            foreach (var name in BatchA1)
            {
                Assert.That(header.Contains($"Framework_{name}("), Is.True, $"framework.h missing export Framework_{name}");
                Assert.That(wrapper.Contains($"Framework_{name}("), Is.True, $"RaylibWrapper.vb missing import Framework_{name}");
            }
            Assert.That(BatchA1, Has.Length.EqualTo(16));
        });
    }
}
