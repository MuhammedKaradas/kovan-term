using System.Runtime.InteropServices;
using KovanTerm.Services.Dictation;

namespace KovanTerm.Interop;

/// <summary>
/// Records the default microphone with the waveIn API (winmm), as 16 kHz 16-bit mono: no audio library needed for
/// that. Buffers of 100 ms are queued to the driver and collected by a background thread.
/// </summary>
public sealed class WaveInRecorder : IAudioRecorder
{
    private const int BufferMs = 100, BufferCount = 8;
    private const int BufferBytes = SpeechModel.SampleRate * 2 * BufferMs / 1000;
    private const uint WAVE_MAPPER = 0xFFFFFFFF, CALLBACK_EVENT = 0x50000;
    private const uint WHDR_DONE = 1, WHDR_PREPARED = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

    private static readonly int HeaderSize = Marshal.SizeOf<WAVEHDR>();

    [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr handle, uint deviceId, ref WAVEFORMATEX format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr handle);

    private IntPtr _handle;
    private readonly IntPtr[] _headers = new IntPtr[BufferCount];
    private int _next;
    private List<short> _samples = [];
    private AutoResetEvent? _ready;
    private Thread? _thread;
    private volatile bool _recording;

    public void Start()
    {
        if (_recording) return;
        _samples = [];
        _next = 0;
        _ready = new AutoResetEvent(false);
        var format = new WAVEFORMATEX
        {
            wFormatTag = 1, // PCM
            nChannels = 1,
            nSamplesPerSec = SpeechModel.SampleRate,
            nAvgBytesPerSec = SpeechModel.SampleRate * 2,
            nBlockAlign = 2,
            wBitsPerSample = 16,
        };
        var result = waveInOpen(out _handle, WAVE_MAPPER, ref format, _ready.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
        if (result != 0)
        {
            _ready.Dispose();
            // 2 = MMSYSERR_BADDEVICEID: no recording device
            throw new InvalidOperationException(result == 2 ? "No microphone was found." : $"The microphone couldn't be opened (waveIn error {result}).");
        }
        for (var i = 0; i < BufferCount; i++)
        {
            var header = Marshal.AllocHGlobal(HeaderSize);
            Marshal.StructureToPtr(new WAVEHDR { lpData = Marshal.AllocHGlobal(BufferBytes), dwBufferLength = BufferBytes }, header, false);
            waveInPrepareHeader(_handle, header, HeaderSize);
            waveInAddBuffer(_handle, header, HeaderSize);
            _headers[i] = header;
        }
        _recording = true;
        waveInStart(_handle);
        _thread = new Thread(() =>
        {
            while (_recording)
            {
                _ready.WaitOne(200);
                Collect(requeue: true);
            }
        }) { IsBackground = true, Name = "Dictation recorder" };
        _thread.Start();
    }

    public float[] Stop()
    {
        if (!_recording) return [];
        _recording = false;
        _ready!.Set();
        _thread!.Join();
        // Returns the buffers still queued, marked done with what they have so far
        waveInReset(_handle);
        Collect(requeue: false);
        foreach (var header in _headers)
        {
            waveInUnprepareHeader(_handle, header, HeaderSize);
            Marshal.FreeHGlobal(Marshal.PtrToStructure<WAVEHDR>(header).lpData);
            Marshal.FreeHGlobal(header);
        }
        waveInClose(_handle);
        _ready.Dispose();
        return _samples.Select(s => s / 32768f).ToArray();
    }

    // Buffers complete in the order they were queued
    private void Collect(bool requeue)
    {
        for (var n = 0; n < BufferCount; n++)
        {
            var header = _headers[_next];
            var h = Marshal.PtrToStructure<WAVEHDR>(header);
            if ((h.dwFlags & WHDR_DONE) == 0) return;
            var bytes = new byte[h.dwBytesRecorded];
            Marshal.Copy(h.lpData, bytes, 0, bytes.Length);
            for (var i = 0; i + 1 < bytes.Length; i += 2)
                _samples.Add(BitConverter.ToInt16(bytes, i));
            if (requeue)
            {
                h.dwBytesRecorded = 0;
                h.dwFlags = WHDR_PREPARED;
                Marshal.StructureToPtr(h, header, false);
                waveInAddBuffer(_handle, header, HeaderSize);
            }
            else
            {
                // Don't read it twice
                h.dwFlags &= ~WHDR_DONE;
                Marshal.StructureToPtr(h, header, false);
            }
            _next = (_next + 1) % BufferCount;
        }
    }
}
