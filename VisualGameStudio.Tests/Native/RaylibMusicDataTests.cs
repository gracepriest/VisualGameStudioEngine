using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for raudio Batch audio-A3 (raw raylib Music streaming API). Like Sound, Music is backed by the
/// audio system's mixer, so every op needs an initialized device — this fixture inits one and Ignores when none is
/// present (headless CI stays green; a workstation with audio hardware exercises the real path).
///
/// The correctness-critical property is the by-VALUE <see cref="RMusic"/> marshaling, and Music adds two things Sound
/// did not: a <c>looping</c> C bool (1 byte, <see cref="UnmanagedType.I1"/>) and an opaque decoder-context tail
/// (ctxType/ctxData). raylib defaults <c>looping</c> to true and tags a WAV context as MUSIC_AUDIO_WAV (1) with a
/// non-null ctxData, so those assertions pin down the exact field offsets — a bad bool marshaling would shift
/// ctxType/ctxData into garbage. Two behaviors differ from A2's Sound and the test encodes both:
///   1. Music keeps the SOURCE stream format (8000/mono/16 here); the mixer resamples per-buffer during playback.
///      (LoadSoundFromWave, by contrast, resamples the whole clip to the device format up-front.)
///   2. raylib STREAMS music from the caller's buffer — drwav_init_memory keeps a pointer and does NOT copy — so the
///      WAV bytes must stay pinned for the whole Music lifetime, not just the load call.
/// Self-contained local [DllImport] + struct mirrors; self-skips when the engine DLL isn't staged with the A3 exports
/// (stale DLL -> EntryPointNotFound -> refresh IDE\ first). Broader audible coverage across A1-A4 is the --audio smoke's job.
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibMusicDataTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAudioStream { public IntPtr buffer, processor; public uint sampleRate, sampleSize, channels; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RMusic
    {
        public RAudioStream stream;
        public uint frameCount;
        [MarshalAs(UnmanagedType.I1)] public bool looping;
        public int ctxType;
        public IntPtr ctxData;
    }

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_InitAudioDevice();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseAudioDevice();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsAudioDeviceReady();
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern RMusic Framework_LoadMusicStreamFromMemory(string fileType, byte[] data, int dataSize);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsMusicValid(RMusic music);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadMusicStream(RMusic music);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_PlayMusicStream(RMusic music);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsMusicStreamPlaying(RMusic music);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UpdateMusicStream(RMusic music);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_StopMusicStream(RMusic music);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_PauseMusicStream(RMusic music);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ResumeMusicStream(RMusic music);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SeekMusicStream(RMusic music, float position);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetMusicVolume(RMusic music, float volume);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetMusicPitch(RMusic music, float pitch);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetMusicPan(RMusic music, float pan);
    [DllImport(DLL, CallingConvention = CC)] private static extern float Framework_GetMusicTimeLength(RMusic music);
    [DllImport(DLL, CallingConvention = CC)] private static extern float Framework_GetMusicTimePlayed(RMusic music);

    private static T Guard<T>(Func<T> call)
    {
        try { return call(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates raudio Batch audio-A3 exports; refresh IDE\\ first."); throw; }
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
    public void Music_from_memory_marshals_the_full_struct_and_playback_controls_round_trip_with_a_device()
    {
        // Soft: a headless host has no audio device -> Ignore. On audio hardware this drives the real Music API.
        bool ready = Guard(() => { Framework_InitAudioDevice(); return Framework_IsAudioDeviceReady(); });
        if (!ready)
        {
            try { Framework_CloseAudioDevice(); } catch { /* nothing to close */ }
            Assert.Ignore("No audio device in this environment; Music playback is covered by the --audio smoke.");
        }

        const int frames = 4000; // 0.5 s @ 8000 Hz mono
        var wavBytes = MakeWav(frames, 8000, 8000);
        // raylib streams music from the caller's buffer (drwav_init_memory keeps a pointer; it does NOT copy the audio
        // data, unlike LoadWaveFromMemory which decodes up-front). So pin the byte[] for the WHOLE Music lifetime and
        // free it only after UnloadMusicStream — otherwise UpdateMusicStream reads a moved/collected buffer.
        var dataPin = GCHandle.Alloc(wavBytes, GCHandleType.Pinned);
        RMusic mus = default;
        try
        {
            mus = Framework_LoadMusicStreamFromMemory(".wav", wavBytes, wavBytes.Length);
            Assert.Multiple(() =>
            {
                Assert.That(Framework_IsMusicValid(mus), Is.True, "LoadMusicStreamFromMemory yields a valid music (by-value struct marshaling)");
                // Music keeps the SOURCE stream format (the mixer resamples per-buffer at playback) — unlike Sound,
                // which resamples the whole clip to the device format on load. So assert the source 8000/mono/16.
                Assert.That(mus.frameCount, Is.EqualTo((uint)frames), "total decoded frame count is the source count (not resampled)");
                Assert.That(mus.stream.buffer, Is.Not.EqualTo(IntPtr.Zero), "the embedded AudioStream buffer pointer round-trips");
                Assert.That(mus.stream.sampleRate, Is.EqualTo(8000u), "stream keeps the source sample rate");
                Assert.That(mus.stream.channels, Is.EqualTo(1u), "stream keeps the source (mono) channel count");
                Assert.That(mus.stream.sampleSize, Is.EqualTo(16u), "stream keeps the source 16-bit sample size");
                // The fields AFTER stream+frameCount validate the new Music-only tail. raylib defaults looping to true,
                // tags a WAV context as MUSIC_AUDIO_WAV (1), and heap-allocates ctxData. Correct values here prove the
                // I1 bool offset and the trailing int/ptr offsets (a bad bool marshaling would shift them into garbage).
                Assert.That(mus.looping, Is.True, "raylib defaults music looping to true (validates the I1 bool field offset)");
                Assert.That(mus.ctxType, Is.EqualTo(1), "WAV decoder tag is MUSIC_AUDIO_WAV==1 (validates the int after the bool)");
                Assert.That(mus.ctxData, Is.Not.EqualTo(IntPtr.Zero), "decoder context pointer round-trips (validates the trailing ptr)");
            });

            float length = Framework_GetMusicTimeLength(mus);
            Assert.That(length, Is.GreaterThan(0f).And.LessThan(10f), "GetMusicTimeLength returns a sane positive duration (~0.5 s)");

            Framework_SetMusicVolume(mus, 0.02f); // keep it quiet in a unit-test run
            Framework_SetMusicPitch(mus, 1.0f);
            Framework_SetMusicPan(mus, 0.5f);

            Framework_PlayMusicStream(mus);
            Assert.That(Framework_IsMusicStreamPlaying(mus), Is.True, "PlayMusicStream sets the stream's playing flag synchronously");

            Framework_UpdateMusicStream(mus);   // feed the mixer one buffer (streams from the pinned WAV bytes)
            Framework_SeekMusicStream(mus, 0.1f);
            Assert.That(Framework_GetMusicTimePlayed(mus), Is.GreaterThanOrEqualTo(0f), "GetMusicTimePlayed is readable after a seek");

            Framework_PauseMusicStream(mus);
            Framework_ResumeMusicStream(mus);

            Framework_StopMusicStream(mus);
            Assert.That(Framework_IsMusicStreamPlaying(mus), Is.False, "StopMusicStream clears the playing flag synchronously");
        }
        finally
        {
            if (mus.stream.buffer != IntPtr.Zero) Framework_UnloadMusicStream(mus); // free the decoder while the data is still pinned
            dataPin.Free();
            try { Framework_CloseAudioDevice(); } catch { /* nothing to close */ }
        }
    }
}
