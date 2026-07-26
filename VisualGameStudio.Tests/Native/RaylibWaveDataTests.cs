using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime correctness for the HEADLESS half of raudio Batch audio-A1: the Wave PCM-data ops
/// (LoadWaveFromMemory, IsWaveValid, WaveCopy, WaveCrop, WaveFormat, LoadWaveSamples, UnloadWave*, ExportWave).
/// These decode/transform an in-RAM WAV with no audio device or window, so they run headless via P/Invoke —
/// the correctness-critical property is the by-VALUE <see cref="RWave"/> marshaling (a layout slip corrupts
/// frameCount/sampleRate) and the ByRef in-place mutation of WaveCrop/WaveFormat. Device fns
/// (Init/Close/IsAudioDeviceReady) get a SOFT lifecycle check here (Ignored when no device is present) and are
/// the --audio smoke's job. Self-contained local [DllImport] + struct mirror; self-skips when the engine DLL
/// isn't staged with these exports (stale DLL → EntryPointNotFound → refresh IDE\ first, confirm Passed not Skipped).
/// </summary>
[Category("Integration")]
[TestFixture]
public class RaylibWaveDataTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)]
    private struct RWave { public uint frameCount, sampleRate, sampleSize, channels; public IntPtr data; }

    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] private static extern RWave Framework_LoadWaveFromMemory(string fileType, byte[] fileData, int dataSize);
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsWaveValid(RWave wave);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadWave(RWave wave);
    [DllImport(DLL, CallingConvention = CC, CharSet = CharSet.Ansi)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_ExportWave(RWave wave, string fileName);
    [DllImport(DLL, CallingConvention = CC)] private static extern RWave Framework_WaveCopy(RWave wave);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_WaveCrop(ref RWave wave, int initFrame, int finalFrame);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_WaveFormat(ref RWave wave, int sampleRate, int sampleSize, int channels);
    [DllImport(DLL, CallingConvention = CC)] private static extern IntPtr Framework_LoadWaveSamples(RWave wave);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadWaveSamples(IntPtr samples);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_InitAudioDevice();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseAudioDevice();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsAudioDeviceReady();

    private static T Guard<T>(Func<T> call)
    {
        try { return call(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); throw; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates raudio Batch audio-A1 exports; refresh IDE\\ first."); throw; }
    }

    // Build a minimal valid little-endian PCM WAV: mono, 16-bit, `sampleRate` Hz, `frames` frames all set to `sampleValue`.
    private static byte[] MakeWav(int frames, int sampleRate, short sampleValue)
    {
        const int channels = 1, bitsPerSample = 16;
        int blockAlign = channels * bitsPerSample / 8;   // 2
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
    public void LoadWaveFromMemory_and_wave_data_ops_round_trip_headless()
    {
        var bytes = MakeWav(100, 8000, 16384); // constant 16384/32768 == 0.5 normalized
        var wave = Guard(() => Framework_LoadWaveFromMemory(".wav", bytes, bytes.Length));
        Assert.Multiple(() =>
        {
            Assert.That(wave.frameCount, Is.EqualTo(100u), "frameCount round-trips from the WAV (by-value marshaling)");
            Assert.That(wave.sampleRate, Is.EqualTo(8000u));
            Assert.That(wave.channels, Is.EqualTo(1u));
            Assert.That(wave.sampleSize, Is.EqualTo(16u));
            Assert.That(wave.data, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(Framework_IsWaveValid(wave), Is.True);
        });

        var copy = Framework_WaveCopy(wave);
        Assert.That(copy.frameCount, Is.EqualTo(100u));
        Assert.That(copy.data, Is.Not.EqualTo(IntPtr.Zero));
        Assert.That(copy.data, Is.Not.EqualTo(wave.data), "WaveCopy must own a distinct buffer");

        var cropped = Framework_WaveCopy(wave);
        Framework_WaveCrop(ref cropped, 0, 50);
        Assert.That(cropped.frameCount, Is.EqualTo(50u), "crop [0,50) yields 50 frames (ByRef in-place)");

        var formatted = Framework_WaveCopy(wave);
        Framework_WaveFormat(ref formatted, 8000, 16, 2);
        Assert.Multiple(() =>
        {
            Assert.That(formatted.channels, Is.EqualTo(2u), "mono -> stereo (ByRef in-place)");
            Assert.That(formatted.sampleRate, Is.EqualTo(8000u));
            Assert.That(formatted.frameCount, Is.EqualTo(100u), "same rate -> frame count unchanged");
        });

        IntPtr sp = Framework_LoadWaveSamples(wave);
        Assert.That(sp, Is.Not.EqualTo(IntPtr.Zero), "LoadWaveSamples allocates a float buffer");
        try
        {
            int n = (int)(wave.frameCount * wave.channels);
            var samples = new float[n];
            Marshal.Copy(sp, samples, 0, n);
            Assert.That(samples[0], Is.EqualTo(0.5f).Within(0.01f), "16384/32768 normalizes to 0.5");
            Assert.That(samples[n - 1], Is.EqualTo(0.5f).Within(0.01f));
        }
        finally { Framework_UnloadWaveSamples(sp); }

        Framework_UnloadWave(wave);
        Framework_UnloadWave(copy);
        Framework_UnloadWave(cropped);
        Framework_UnloadWave(formatted);
    }

    [Test]
    public void ExportWave_writes_a_wav_file()
    {
        var bytes = MakeWav(64, 8000, 8000);
        var wave = Guard(() => Framework_LoadWaveFromMemory(".wav", bytes, bytes.Length));
        var path = Path.Combine(Path.GetTempPath(), $"vgse_a1_{Guid.NewGuid():N}.wav");
        try
        {
            Assert.That(Framework_ExportWave(wave, path), Is.True, "ExportWave reports success");
            Assert.That(File.Exists(path), Is.True, "ExportWave wrote the file");
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(44), "exported file has a header + data");
        }
        finally { Framework_UnloadWave(wave); if (File.Exists(path)) File.Delete(path); }
    }

    [Test]
    public void AudioDevice_init_ready_close_when_a_device_is_present()
    {
        // Soft: a headless host may have no audio device -> IsAudioDeviceReady() false -> Ignore.
        // On a workstation with audio hardware this exercises the real device lifecycle. Full device coverage = --audio smoke.
        bool ready = Guard(() => { Framework_InitAudioDevice(); return Framework_IsAudioDeviceReady(); });
        try
        {
            if (!ready) Assert.Ignore("No audio device in this environment; device fns are covered by the --audio smoke.");
            Assert.That(ready, Is.True);
        }
        finally { try { Framework_CloseAudioDevice(); } catch { /* no device -> nothing to close */ } }
    }
}
