using System.Diagnostics;
using System.Text;

namespace ScreenshotScraper.Ocr;

internal interface IPaddleOcrTransport : IDisposable
{
    Task<string> InvokeAsync(string requestJson, CancellationToken cancellationToken);
}

internal sealed class PaddleOcrStdioTransport : IPaddleOcrTransport
{
    private readonly PaddleOcrOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;

    public PaddleOcrStdioTransport(PaddleOcrOptions options)
    {
        _options = options;
    }

    public async Task<string> InvokeAsync(string requestJson, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.TimeoutMilliseconds);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        await _lock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            EnsureProcessStarted();

            await _stdin!.WriteLineAsync(requestJson).ConfigureAwait(false);
            await _stdin.FlushAsync().ConfigureAwait(false);

            var responseTask = _stdout!.ReadLineAsync(linked.Token).AsTask();
            var response = await responseTask.ConfigureAwait(false);
            if (response is null)
            {
                throw new InvalidOperationException("PaddleOCR worker closed stdout before sending a response.");
            }

            return response;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"PaddleOCR worker timed out after {_options.TimeoutMilliseconds} ms.");
        }
        finally
        {
            if (!_options.KeepWorkerWarm)
            {
                StopProcess();
            }

            _lock.Release();
        }
    }

    private void EnsureProcessStarted()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        StopProcess();

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.PythonExecutablePath,
            Arguments = $"\"{_options.WorkerScriptPath}\" --stdio --lang {_options.Language}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start PaddleOCR python worker process.");

        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;
    }

    private void StopProcess()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
        finally
        {
            _stdin?.Dispose();
            _stdout?.Dispose();
            _process?.Dispose();
            _stdin = null;
            _stdout = null;
            _process = null;
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
        StopProcess();
    }
}
