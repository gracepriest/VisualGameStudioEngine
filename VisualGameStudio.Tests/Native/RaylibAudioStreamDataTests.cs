using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for raudio Batch audio-A4 (raw raylib low-level AudioStream API). Like Sound/Music, an
/// AudioStream is backed by the audio mixer, so every op needs an initialized device — this fixture inits one and
/// Ignores when none is present (headless CI stays green; a workstation with audio hardware exercises the real path).
///
/// AudioStream is the cleanest by-value marshaling check of the batch: <see cref="Framework_LoadAudioStream"/> stores
/// the requested sampleRate/sampleSize/channels VERBATIM (no device resample at this layer, unlike LoadSoundFromWave),
/// so the struct fields must round-trip exactly — a layout slip corrupts them or the buffer/processor pointers, and
/// the assertions catch it directly (no embedding Sound/Music). Playback state (Play -> playing, Stop -> not) is read
/// via the synchronous buffer flag, so it is deterministic. A freshly loaded stream starts with its sub-buffers marked
/// processed, so IsAudioStreamProcessed is true and UpdateAudioStream can fill one. Self-contained local [DllImport] +
/// struct mirror; self-skips when the engine DLL isn't staged with the A4 exports (stale DLL -> EntryPointNotFound ->
/// refresh IDE\ first). Broader audible coverage across A1-A4 is the --audio smoke's job.
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibAudioStreamDataTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAudioStream { public IntPtr buffer, processor; public uint sampleRate, sampleSize, channels; }

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_InitAudioDevice();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseAudioDevice();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsAudioDeviceReady();
    [DllImport(DLL, CallingConvention = CC)] private static extern RAudioStream Framework_LoadAudioStream(uint sampleRate, uint sampleSize, uint channels);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsAudioStreamValid(RAudioStream stream);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadAudioStream(RAudioStream stream);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UpdateAudioStream(RAudioStream stream, IntPtr data, int frameCount);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsAudioStreamProcessed(RAudioStream stream);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_PlayAudioStream(RAudioStream stream);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_PauseAudioStream(RAudioStream stream);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_ResumeAudioStream(RAudioStream stream);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsAudioStreamPlaying(RAudioStream stream);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_StopAudioStream(RAudioStream stream);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetAudioStreamVolume(RAudioStream stream, float volume);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetAudioStreamPitch(RAudioStream stream, float pitch);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetAudioStreamPan(RAudioStream stream, float pan);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetAudioStreamBufferSizeDefault(int size);

    private static T Guard<T>(Func<T> call)
    {
        try { return call(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates raudio Batch audio-A4 exports; refresh IDE\\ first."); throw; }
    }

    [Test]
    public void AudioStream_lifecycle_and_playback_controls_round_trip_with_a_device()
    {
        // Soft: a headless host has no audio device -> Ignore. On audio hardware this drives the real AudioStream API.
        bool ready = Guard(() => { Framework_InitAudioDevice(); return Framework_IsAudioDeviceReady(); });
        if (!ready)
        {
            try { Framework_CloseAudioDevice(); } catch { /* nothing to close */ }
            Assert.Ignore("No audio device in this environment; AudioStream playback is covered by the --audio smoke.");
        }

        // SetAudioStreamBufferSizeDefault sets the default sub-buffer size for streams created AFTERWARD — call it
        // before LoadAudioStream so the created stream uses it. Harmless global; exercises the passthrough.
        Framework_SetAudioStreamBufferSizeDefault(4096);

        const uint sampleRate = 22050, sampleSize = 16, channels = 1;
        RAudioStream stream = default;
        try
        {
            stream = Framework_LoadAudioStream(sampleRate, sampleSize, channels);
            Assert.Multiple(() =>
            {
                Assert.That(Framework_IsAudioStreamValid(stream), Is.True, "LoadAudioStream yields a valid stream (by-value struct marshaling)");
                // LoadAudioStream stores the REQUESTED format verbatim (no device resample at this layer) — a direct
                // round-trip check of the AudioStream marshaling (buffer/processor ptrs + the 3 trailing uints).
                Assert.That(stream.buffer, Is.Not.EqualTo(IntPtr.Zero), "the audio buffer pointer round-trips");
                Assert.That(stream.sampleRate, Is.EqualTo(sampleRate), "requested sample rate round-trips exactly");
                Assert.That(stream.sampleSize, Is.EqualTo(sampleSize), "requested sample size round-trips exactly");
                Assert.That(stream.channels, Is.EqualTo(channels), "requested channel count round-trips exactly");
            });

            // A freshly loaded stream starts with its sub-buffers marked processed, so a first UpdateAudioStream fits.
            Assert.That(Framework_IsAudioStreamProcessed(stream), Is.True, "fresh stream reports a processed sub-buffer");

            // UpdateAudioStream: feed one sub-buffer of silence (exercises the const void* data -> IntPtr passthrough).
            // 16-bit mono => `short` samples, frameCount frames; raylib clamps to the sub-buffer size. Pin the buffer.
            const int frameCount = 1024;
            var silent = new short[frameCount * (int)channels];
            var pin = GCHandle.Alloc(silent, GCHandleType.Pinned);
            try { Framework_UpdateAudioStream(stream, pin.AddrOfPinnedObject(), frameCount); }
            finally { pin.Free(); }

            Framework_SetAudioStreamVolume(stream, 0.02f); // keep it quiet in a unit-test run
            Framework_SetAudioStreamPitch(stream, 1.0f);
            Framework_SetAudioStreamPan(stream, 0.5f);

            Framework_PlayAudioStream(stream);
            Assert.That(Framework_IsAudioStreamPlaying(stream), Is.True, "PlayAudioStream sets the playing flag synchronously");

            Framework_PauseAudioStream(stream);
            Framework_ResumeAudioStream(stream);

            Framework_StopAudioStream(stream);
            Assert.That(Framework_IsAudioStreamPlaying(stream), Is.False, "StopAudioStream clears the playing flag synchronously");
        }
        finally
        {
            if (stream.buffer != IntPtr.Zero) Framework_UnloadAudioStream(stream);
            try { Framework_CloseAudioDevice(); } catch { /* nothing to close */ }
        }
    }
}
