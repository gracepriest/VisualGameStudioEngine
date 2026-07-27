using System;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Runtime ABI-smoke for the raudio half of the deferred callbacks batch (the 5 AudioCallback setters) under a live audio
/// device — [Category("Integration")] + [NonParallelizable]. The callback FIRING is on the audio thread and timing-dependent,
/// so (the rcore file-callback fixture already proves callbacks actually fire) this test proves the audio-specific marshaling:
/// the AudioCallback delegate converts to a native fn-pointer and AudioStream passes BY VALUE without a crash, for the
/// whole-pipeline processor (Attach/DetachAudioMixedProcessor) and the per-stream callback/processor (SetAudioStreamCallback,
/// Attach/DetachAudioStreamProcessor). Delegates are GC.KeepAlive'd for the whole test and the stream callback is reset before
/// unload. Self-Ignores when there is no audio device or the DLL predates the exports.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class RaylibAudioCallbacksTests
{
    private const string DLL = "VisualGameStudioEngine.dll";
    private const CallingConvention CC = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStream { public IntPtr buffer; public IntPtr processor; public uint sampleRate; public uint sampleSize; public uint channels; }

    [UnmanagedFunctionPointer(CC)] private delegate void AudioCb(IntPtr bufferData, uint frames);

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_InitAudioDevice();
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_CloseAudioDevice();
    [DllImport(DLL, CallingConvention = CC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool Framework_IsAudioDeviceReady();
    [DllImport(DLL, CallingConvention = CC)] private static extern AudioStream Framework_LoadAudioStream(uint sampleRate, uint sampleSize, uint channels);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_UnloadAudioStream(AudioStream stream);

    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_SetAudioStreamCallback(AudioStream stream, AudioCb callback);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_AttachAudioStreamProcessor(AudioStream stream, AudioCb processor);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DetachAudioStreamProcessor(AudioStream stream, AudioCb processor);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_AttachAudioMixedProcessor(AudioCb processor);
    [DllImport(DLL, CallingConvention = CC)] private static extern void Framework_DetachAudioMixedProcessor(AudioCb processor);

    [Test]
    public void Audio_callback_delegates_marshal_under_a_device()
    {
        try { Framework_InitAudioDevice(); }
        catch (DllNotFoundException) { Assert.Ignore($"{DLL} not staged next to the test binary; refresh IDE\\ first."); return; }
        catch (EntryPointNotFoundException) { Assert.Ignore($"{DLL} predates the deferred-callback exports; refresh IDE\\ first."); return; }

        if (!Framework_IsAudioDeviceReady())
        {
            try { Framework_CloseAudioDevice(); } catch { /* nothing to tear down */ }
            Assert.Ignore("No audio device available in this environment.");
            return;
        }

        AudioCb mixedProc = (buf, frames) => { };
        AudioCb streamCb = (buf, frames) => { };
        AudioCb streamProc = (buf, frames) => { };
        AudioStream stream = default;
        bool streamLoaded = false;
        try
        {
            Assert.DoesNotThrow(() =>
            {
                // Whole-pipeline processor (AudioCallback only).
                Framework_AttachAudioMixedProcessor(mixedProc);
                Framework_DetachAudioMixedProcessor(mixedProc);

                // Per-stream callback + processor (AudioStream BY VALUE + AudioCallback).
                stream = Framework_LoadAudioStream(44100, 16, 2);
                streamLoaded = true;
                Framework_SetAudioStreamCallback(stream, streamCb);
                Framework_AttachAudioStreamProcessor(stream, streamProc);
                Framework_DetachAudioStreamProcessor(stream, streamProc);
            }, "AudioCallback delegates marshal to fn-pointers and AudioStream passes by value under a live device");
        }
        finally
        {
            if (streamLoaded)
            {
                try { Framework_SetAudioStreamCallback(stream, null); } catch { /* detach the stream callback before unload */ }
                try { Framework_UnloadAudioStream(stream); } catch { /* free the stream */ }
            }
            Framework_CloseAudioDevice();
            GC.KeepAlive(mixedProc);
            GC.KeepAlive(streamCb);
            GC.KeepAlive(streamProc);
        }
    }
}
