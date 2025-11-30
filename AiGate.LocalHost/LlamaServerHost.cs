using System.Diagnostics;
using System.Text;

namespace AiGate.LocalHost;

public sealed class LlamaServerHost : ILocalModelHost
{
    private readonly LlamaServerOptions _options;
    private readonly object _gate = new();
    private Process? _process;

    public LlamaServerHost(LlamaServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task EnsureStartedAsync(CancellationToken ct = default)
    {
        if (_options.Mode == LocalProcessMode.External)
        {
            await WaitUntilHealthyAsync(ct);
            return;
        }

        lock (_gate)
        {
            if (_process != null && !_process.HasExited)
            {
                return;
            }

            _process = StartChildProcess();
        }

        await WaitUntilHealthyAsync(ct);
    }

    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        if (_options.Mode == LocalProcessMode.External)
        {
            return;
        }

        Process? proc;

        lock (_gate)
        {
            proc = _process;
        }

        if (proc == null || proc.HasExited)
            return;

        try
        {
            await TrySendShutdownAsync(ct);

            if (!proc.WaitForExit(2000))
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
        finally
        {
            lock (_gate)
            {
                _process?.Dispose();
                _process = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
    }


    private Process StartChildProcess()
    {
        if (string.IsNullOrWhiteSpace(_options.ExePath))
            throw new InvalidOperationException("ExePath is required for LocalProcessMode.Child.");
        if (string.IsNullOrWhiteSpace(_options.ModelPath))
            throw new InvalidOperationException("ModelPath is required for LocalProcessMode.Child.");

        var psi = new ProcessStartInfo
        {
            FileName = _options.ExePath,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
            CreateNoWindow = true
        };

        var args = new StringBuilder();
        args.Append("-m \"").Append(_options.ModelPath).Append('"');
        args.Append(" --port ").Append(_options.Port);
        if (!string.IsNullOrWhiteSpace(_options.ExtraArgs))
        {
            args.Append(' ').Append(_options.ExtraArgs);
        }

        psi.Arguments = args.ToString();

        var proc = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        proc.Start();
        return proc;
    }

    private async Task WaitUntilHealthyAsync(CancellationToken ct)
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        var baseUri = new UriBuilder("http", _options.Host, _options.Port).Uri;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var healthUri = new Uri(baseUri, "/health");
                using var resp = await http.GetAsync(healthUri, ct);
                if (resp.IsSuccessStatusCode || (int)resp.StatusCode >= 400)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                lastError = ex;
            }

            await Task.Delay(500, ct);
        }

        throw new TimeoutException(
            $"llamaserver did not become healthy at {baseUri} within timeout.",
            lastError
        );
    }

    private async Task TrySendShutdownAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
            };

            var baseUri = new UriBuilder("http", _options.Host, _options.Port).Uri;
            var shutdownUri = new Uri(baseUri, "/shutdown");

            await http.PostAsync(shutdownUri, content: null, ct);
        }
        catch
        {
        }
    }
}
