using System.IO;
using System.Text;
using System.Threading.Channels;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;

namespace DualSenseVoice;

// Capture only queues PCM; model loading and inference stay off the UI/audio threads.
internal sealed class LiveTranscription : IAsyncDisposable
{
    readonly object gate = new();
    readonly WaveFormat format;
    readonly MemoryStream pending = new();
    readonly Channel<byte[]> chunks = Channel.CreateUnbounded<byte[]>(new() { SingleReader = true, SingleWriter = false });
    readonly CancellationTokenSource cancellation = new();
    readonly Task<string> worker;
    bool completed;
    int silentBytes;
    int disposed;

    internal LiveTranscription(string modelPath, WaveFormat format)
        : this(format, CreateRecognizer(modelPath, format)) { }

    internal LiveTranscription(WaveFormat format, Func<byte[], CancellationToken, Task<string>> recognize)
    {
        this.format = format;
        worker = Task.Run(async () =>
        {
            var result = new StringBuilder();
            await foreach (byte[] chunk in chunks.Reader.ReadAllAsync(cancellation.Token))
                result.Append(await recognize(chunk, cancellation.Token));
            return result.ToString().Trim();
        });
    }

    static Func<byte[], CancellationToken, Task<string>> CreateRecognizer(string modelPath, WaveFormat format) =>
        async (bytes, token) =>
        {
            using var raw = new RawSourceWaveStream(new MemoryStream(bytes), format);
            ISampleProvider samples = raw.ToSampleProvider();
            if (samples.WaveFormat.Channels == 2)
                samples = new StereoToMonoSampleProvider(samples);
            using var wav = new MemoryStream();
            WaveFileWriter.WriteWavFileToStream(wav,
                new WdlResamplingSampleProvider(samples, 16000).ToWaveProvider16());
            wav.Position = 0;
            using var factory = WhisperFactory.FromPath(modelPath);
            using var processor = factory.CreateBuilder().WithLanguage("ja").Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(wav, token))
                text.Append(segment.Text);
            return text.ToString();
        };

    internal void Append(byte[] bytes, int count)
    {
        lock (gate)
        {
            if (completed || count == 0) return;
            pending.Write(bytes, 0, count);
            // Inspect a small capture block for an utterance boundary.
            using var raw = new RawSourceWaveStream(new MemoryStream(bytes, 0, count, false), format);
            var samples = raw.ToSampleProvider();
            var buffer = new float[1024];
            bool silent = true;
            int read;
            while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
                for (int i = 0; i < read; i++)
                    if (Math.Abs(buffer[i]) > 0.015f) silent = false;
            silentBytes = silent ? silentBytes + count : 0;
            if ((pending.Length >= format.AverageBytesPerSecond &&
                 silentBytes >= format.AverageBytesPerSecond / 2) ||
                pending.Length >= format.AverageBytesPerSecond * 10L)
                Flush();
        }
    }

    void Flush()
    {
        if (pending.Length == 0) return;
        chunks.Writer.TryWrite(pending.ToArray());
        pending.SetLength(0);
        silentBytes = 0;
    }

    internal Task<string> CompleteAsync()
    {
        lock (gate)
        {
            if (!completed) { completed = true; Flush(); chunks.Writer.TryComplete(); }
        }
        return worker;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lock (gate) { completed = true; chunks.Writer.TryComplete(); }
        cancellation.Cancel();
        try { await worker; } catch { /* Observed by CompleteAsync, or abandoned on close. */ }
        pending.Dispose();
        cancellation.Dispose();
    }
}
