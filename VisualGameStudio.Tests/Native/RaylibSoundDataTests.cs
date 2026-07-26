using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for raudio Batch audio-A2 (raw raylib Sound API). Unlike Wave (pure RAM data), Sound is
/// backed by the audio system's mixer buffers, so every op needs an initialized device — this fixture inits one
/// and Ignores when none is present (headless CI stays green; a workstation with audio hardware exercises the
/// real path). The correctness-critical property is the by-VALUE <see cref="RSound"/> / <see cref="RAudioStream"/>
/// marshaling: a layout slip corrupts frameCount or the buffer pointer and IsSoundValid/round-trip assertions catch
/// it. Playback state (PlaySound -> playing, StopSound -> not) is read via the synchronous buffer flags, not the
/// mixer thread, so it is deterministic. Self-contained local [DllImport] + struct mirrors; self-skips when the
/// engine DLL isn't staged with the A2 exports (stale DLL -> EntryPointNotFound -> refresh IDE\ first).
/// Broader audible coverage across A1-A4 is the --audio smoke's job.
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibSoundDataTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)]
    private struct RWave { public uint frameCount, sampleRate, sampleSize, channels; public IntPtr data; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RAudioStream { public IntPtr buffer, processor; public uint sampleRate, sampleSize, channels; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RSound { public RAudioStream stream; public uint frameCount; }

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_InitAudioDevice();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseAudioDevice();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsAudioDeviceReady();
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern RWave Framework_LoadWaveFromMemory(string fileType, byte[] fileData, int dataSize);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadWave(RWave wave);
    [DllImport(DLL, CallingConvention = CC)] private static extern RSound Framework_LoadSoundFromWave(RWave wave);
    [DllImport(DLL, CallingConvention = CC)] private static extern RSound Framework_LoadSoundAlias(RSound source);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsSoundValid(RSound sound);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UpdateSound(RSound sound, IntPtr data, int sampleCount);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_PlaySound(RSound sound);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_StopSound(RSound sound);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_PauseSound(RSound sound);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ResumeSound(RSound sound);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsSoundPlaying(RSound sound);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetSoundVolume(RSound sound, float volume);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetSoundPitch(RSound sound, float pitch);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetSoundPan(RSound sound, float pan);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadSound(RSound sound);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadSoundAlias(RSound alias);

    private static T Guard<T>(Func<T> call)
    {
        try { return call(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates raudio Batch audio-A2 exports; refresh IDE\\ first."); throw; }
    }

    // Minimal valid little-endian PCM WAV: mono, 16-bit, `sampleRate` Hz, `frames` frames all `sampleValue`.
    private static byte[] MakeWav(int frames, int sampleRate, short sampleValue)
    {
        const int channels = 1, bitsPerSample = 16;
        int blockAlign = channels * bitsPerSample / 8;
        int byteRate = sampleRate * blockAlign;
        int dataSize = frames * blockAlign;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataSize); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(sampleRate); w.Write(byteRate); w.Write((short)blockAlign); w.Write((short)bitsPerSample);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataSize);
        for (int i = 0; i < frames; i++) w.Write(sampleValue);
        w.Flush();
        return ms.ToArray();
    }

    [Test]
    public void Sound_from_wave_alias_and_playback_controls_round_trip_with_a_device()
    {
        // Soft: a headless host has no audio device -> Ignore. On audio hardware this drives the real Sound API.
        bool ready = Guard(() => { Framework_InitAudioDevice(); return Framework_IsAudioDeviceReady(); });
        if (!ready)
        {
            try { Framework_CloseAudioDevice(); } catch { /* nothing to close */ }
            Assert.Ignore("No audio device in this environment; Sound playback is covered by the --audio smoke.");
        }

        const int frames = 4000; // 0.5 s @ 8000 Hz
        var wavBytes = MakeWav(frames, 8000, 8000);
        var wave = Framework_LoadWaveFromMemory(".wav", wavBytes, wavBytes.Length);
        RSound snd = default, alias = default;
        try
        {
            snd = Framework_LoadSoundFromWave(wave);
            Assert.Multiple(() =>
            {
                Assert.That(Framework_IsSoundValid(snd), Is.True, "LoadSoundFromWave yields a valid sound (by-value struct marshaling)");
                // raylib resamples/reformats the wave into the audio device's native mixing format on load, so the
                // sound's stream reflects the DEVICE format (not the source 8000 Hz / mono / 16-bit). Assert those
                // device-consistent invariants — sane, non-zero fields at the right offsets prove the by-value layout.
                Assert.That(snd.frameCount, Is.GreaterThan(0u), "resampled frame count is positive");
                Assert.That(snd.stream.buffer, Is.Not.EqualTo(IntPtr.Zero), "the embedded AudioStream buffer pointer round-trips");
                Assert.That(snd.stream.sampleRate, Is.GreaterThan(0u), "stream carries the device sample rate");
                Assert.That(snd.stream.channels, Is.GreaterThanOrEqualTo(1u), "stream carries the device channel count");
                Assert.That(snd.stream.sampleSize, Is.EqualTo(32u), "raylib loads sounds in the device's 32-bit float format");
            });

            alias = Framework_LoadSoundAlias(snd);
            Assert.Multiple(() =>
            {
                Assert.That(Framework_IsSoundValid(alias), Is.True, "an alias is a valid, playable sound");
                Assert.That(alias.frameCount, Is.EqualTo(snd.frameCount), "the alias reports the source frame count");
                Assert.That(alias.stream.sampleRate, Is.EqualTo(snd.stream.sampleRate), "the alias shares the source stream format (both structs marshal identically)");
                Assert.That(alias.stream.channels, Is.EqualTo(snd.stream.channels));
            });

            // UpdateSound: replace the buffer with silence (exercises the const void* data -> IntPtr passthrough).
            // The sound's stream is the device format (32-bit float), so size the buffer to frameCount*channels floats —
            // a short-sized buffer would make raylib over-read. A zero-filled managed array keeps it silent.
            var silent = new float[(int)snd.frameCount * (int)snd.stream.channels];
            var pin = GCHandle.Alloc(silent, GCHandleType.Pinned);
            try { Framework_UpdateSound(snd, pin.AddrOfPinnedObject(), (int)snd.frameCount); }
            finally { pin.Free(); }

            Framework_SetSoundVolume(snd, 0.02f); // keep it quiet in a unit-test run
            Framework_SetSoundPitch(snd, 1.0f);
            Framework_SetSoundPan(snd, 0.5f);

            Framework_PlaySound(snd);
            Assert.That(Framework_IsSoundPlaying(snd), Is.True, "PlaySound sets the buffer's playing flag synchronously");

            Framework_PauseSound(snd);
            Framework_ResumeSound(snd);

            Framework_StopSound(snd);
            Assert.That(Framework_IsSoundPlaying(snd), Is.False, "StopSound clears the playing flag synchronously");
        }
        finally
        {
            if (alias.stream.buffer != IntPtr.Zero) Framework_UnloadSoundAlias(alias); // frees the alias buffer, not the shared data
            if (snd.stream.buffer != IntPtr.Zero) Framework_UnloadSound(snd);
            Framework_UnloadWave(wave);
            try { Framework_CloseAudioDevice(); } catch { /* nothing to close */ }
        }
    }
}
