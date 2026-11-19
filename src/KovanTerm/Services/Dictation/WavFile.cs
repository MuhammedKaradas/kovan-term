using System.IO;

namespace KovanTerm.Services.Dictation;

/// <summary>Reads a PCM WAV file as 16 kHz mono samples.</summary>
public static class WavFile
{
    public static float[] Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a WAV file.");
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAV file.");
        int channels = 0, rate = 0, bits = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                var format = reader.ReadInt16();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                if (format != 1 || bits != 16) throw new InvalidDataException("Only 16-bit PCM WAV files are supported.");
                stream.Seek(size - 16, SeekOrigin.Current);
            }
            else if (id == "data")
            {
                if (channels == 0) throw new InvalidDataException("The WAV file has no format chunk.");
                var frames = Math.Min(size, (int)(stream.Length - stream.Position)) / (2 * channels);
                var mono = new float[frames];
                for (var i = 0; i < frames; i++)
                {
                    float sum = 0;
                    for (var c = 0; c < channels; c++) sum += reader.ReadInt16() / 32768f;
                    mono[i] = sum / channels;
                }
                return Resample(mono, rate, SpeechModel.SampleRate);
            }
            else
            {
                stream.Seek(size + (size & 1), SeekOrigin.Current);
            }
        }
        throw new InvalidDataException("The WAV file has no data.");
    }

    /// <summary>Linear resampling; enough for speech.</summary>
    public static float[] Resample(float[] samples, int from, int to)
    {
        if (from == to || samples.Length == 0) return samples;
        var result = new float[(int)((long)samples.Length * to / from)];
        for (var i = 0; i < result.Length; i++)
        {
            var pos = (double)i * from / to;
            var j = (int)pos;
            var next = Math.Min(j + 1, samples.Length - 1);
            result[i] = (float)(samples[j] + (samples[next] - samples[j]) * (pos - j));
        }
        return result;
    }
}

/// <summary>Plays back a WAV file as if it had been recorded (<c>KOVAN_DICTATION_WAV</c>, for testing without a microphone).</summary>
public sealed class WavFileRecorder(string path) : IAudioRecorder
{
    public void Start() { }

    public float[] Stop()
    {
        using var file = File.OpenRead(path);
        return WavFile.Read(file);
    }
}
