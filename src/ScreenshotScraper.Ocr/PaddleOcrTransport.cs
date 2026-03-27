using System.Diagnostics;
using System.Text;

namespace ScreenshotScraper.Ocr;

internal interface IPaddleOcrTransport : IDisposable
{
    Task<string> InvokeAsync(string requestJson, CancellationToken cancellationToken);

    Task SelfTestAsync(CancellationToken cancellationToken);
}

internal sealed class PaddleOcrStdioTransport : IPaddleOcrTransport
{
    private readonly PaddleOcrOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _stderrSync = new();
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private string? _resolvedPythonPath;
    private string? _resolvedWorkerScriptPath;
    private readonly StringBuilder _stderrBuffer = new();

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
            ThrowIfWorkerExited("before request write");

            await _stdin!.WriteLineAsync(requestJson).ConfigureAwait(false);
            await _stdin.FlushAsync().ConfigureAwait(false);
            try
            {
                await _stdin!.WriteLineAsync(requestJson).ConfigureAwait(false);
                await _stdin.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException ioEx)
            {
                throw BuildWorkerIoException("failed while writing request to worker stdin", ioEx);
            }

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

    public async Task SelfTestAsync(CancellationToken cancellationToken)
    {
        const string pingJson = "{\"image_base64\":\"\"}";
        _ = await InvokeAsync(pingJson, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureProcessStarted()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        StopProcess();

        _resolvedPythonPath = ResolvePath(_options.PythonExecutablePath);
        _resolvedWorkerScriptPath = ResolvePath(_options.WorkerScriptPath);
        var workerScriptExists = File.Exists(_resolvedWorkerScriptPath);

        Debug.WriteLine($"[PaddleOCR] Python executable path (configured): {_options.PythonExecutablePath}");
        Debug.WriteLine($"[PaddleOCR] Python executable path (resolved): {_resolvedPythonPath}");
        Debug.WriteLine($"[PaddleOCR] Worker script path (configured): {_options.WorkerScriptPath}");
        Debug.WriteLine($"[PaddleOCR] Worker script path (resolved): {_resolvedWorkerScriptPath}");
        Debug.WriteLine($"[PaddleOCR] Worker script exists: {workerScriptExists}");

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

        _process.ErrorDataReceived += OnWorkerErrorDataReceived;
        _process.BeginErrorReadLine();

        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;

        // Guardrail: worker stdout is reserved strictly for JSON RPC responses.
        // Any diagnostics/debug output from Python must go to stderr to avoid protocol corruption.
        if (_process.HasExited)
        {
            throw BuildWorkerExitedException("immediately after startup");
        }
    }

    private void OnWorkerErrorDataReceived(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Data))
        {
            return;
        }

        lock (_stderrSync)
        {
            if (_stderrBuffer.Length > 0)
            {
                _stderrBuffer.AppendLine();
            }

            _stderrBuffer.Append(args.Data);
        }

        Debug.WriteLine($"[PaddleOCR][stderr] {args.Data}");
    }

    private void ThrowIfWorkerExited(string lifecycleStage)
    {
        if (_process is { HasExited: true })
        {
            throw BuildWorkerExitedException(lifecycleStage);
        }
    }

    private Exception BuildWorkerIoException(string lifecycleStage, IOException innerException)
    {
        var exitCode = _process?.HasExited == true ? _process.ExitCode.ToString() : "running";
        var details = BuildCommonWorkerDetails();
        return new IOException(
            $"PaddleOCR worker {lifecycleStage}. " +
            $"Process exit state: {exitCode}. {details}",
            innerException);
    }

    private Exception BuildWorkerExitedException(string lifecycleStage)
    {
        var exitCode = _process?.HasExited == true ? _process.ExitCode.ToString() : "unknown";
        var stdout = ReadRemainingStdout();
        var stderr = GetCollectedStderr();
        var details = BuildCommonWorkerDetails();
        return new InvalidOperationException(
            $"PaddleOCR worker exited {lifecycleStage}. Exit code: {exitCode}. {details} " +
            $"Captured stdout: {NormalizeForMessage(stdout)}. Captured stderr: {NormalizeForMessage(stderr)}");
    }

    private string BuildCommonWorkerDetails()
    {
        return $"Python path: {_resolvedPythonPath ?? _options.PythonExecutablePath}; " +
               $"Worker script path: {_resolvedWorkerScriptPath ?? _options.WorkerScriptPath}.";
    }

    private string GetCollectedStderr()
    {
        lock (_stderrSync)
        {
            return _stderrBuffer.ToString();
        }
    }

    private string ReadRemainingStdout()
    {
        try
        {
            return _stdout?.ReadToEnd() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeForMessage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        const int maxLength = 3000;
        return value.Length <= maxLength ? value : value[..maxLength] + "...<truncated>";
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        var fromBase = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
        if (File.Exists(fromBase))
        {
            return fromBase;
        }

        return Path.GetFullPath(path);
    }

    private void StopProcess()
    {
        try
        {
            if (_process is not null)
            {
                _process.ErrorDataReceived -= OnWorkerErrorDataReceived;
            }

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
            lock (_stderrSync)
            {
                _stderrBuffer.Clear();
            }
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
        StopProcess();
    }
}