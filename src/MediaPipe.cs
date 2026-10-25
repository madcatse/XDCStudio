using System.Diagnostics;

namespace XdcConverter;

// FFmpeg's bounded OS pipe is the only transport for decoded media.
public sealed class MediaPipe : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> errors;
    private readonly CancellationTokenRegistration registration;
    public Stream Stream => process.StandardOutput.BaseStream;

    public MediaPipe(IEnumerable<string> arguments, CancellationToken ct)
    {
        process = Processes.Start(Data.Tool(Converter.Ffmpeg), arguments, binary: true);
        errors = process.StandardError.ReadToEndAsync();
        registration = ct.Register(() => Processes.Kill(process));
    }

    public async Task ReadVideo(byte[] buffer, CancellationToken ct)
    {
        int read = await Stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        if (read != buffer.Length)
        {
            ct.ThrowIfCancellationRequested();
            await process.WaitForExitAsync(ct);
            throw new IOException(L.T("FFmpeg завершил видеопоток раньше времени. ", "FFmpeg ended the video stream early. ") + await errors);
        }
    }

    public async Task Complete(CancellationToken ct)
    {
        await Stream.CopyToAsync(System.IO.Stream.Null, ct);
        await process.WaitForExitAsync(ct);
        ct.ThrowIfCancellationRequested();
        string error = await errors;
        if (process.ExitCode != 0) throw new IOException("FFmpeg: " + error[^Math.Min(error.Length, 5000)..]);
    }

    public async ValueTask DisposeAsync()
    {
        registration.Dispose();
        Processes.Kill(process);
        await process.WaitForExitAsync();
        await errors;
        process.Dispose();
    }
}
