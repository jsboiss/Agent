using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Agent.Frontend;

public sealed class ViteDevelopmentFrontendService(
    IWebHostEnvironment environment,
    IOptions<DevelopmentFrontendOptions> options,
    ILogger<ViteDevelopmentFrontendService> logger) : IHostedService, IDisposable
{
    private DevelopmentFrontendOptions Options { get; } = options.Value;

    private Process? ViteProcess { get; set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!Options.Enabled)
        {
            return;
        }

        if (await IsViteAvailable(cancellationToken))
        {
            logger.LogInformation("Vite development frontend is already running at {Url}.", Options.Url);

            return;
        }

        var clientAppPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, Options.ClientAppPath));
        if (!Directory.Exists(clientAppPath))
        {
            logger.LogWarning("Vite development frontend path does not exist: {ClientAppPath}", clientAppPath);

            return;
        }

        var viteEntryPoint = Path.Combine(clientAppPath, "node_modules", "vite", "bin", "vite.js");

        if (!File.Exists(viteEntryPoint))
        {
            logger.LogWarning("Vite entrypoint does not exist. Run npm install in {ClientAppPath}. Missing: {ViteEntryPoint}", clientAppPath, viteEntryPoint);

            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"\"{viteEntryPoint}\" --host 127.0.0.1 --port 5173",
            WorkingDirectory = clientAppPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.Environment["VITE_API_TARGET"] = Options.ApiTarget;

        ViteProcess = Process.Start(startInfo);
        if (ViteProcess is null)
        {
            logger.LogWarning("Vite development frontend did not start.");

            return;
        }

        _ = Task.Run(() => PipeOutput(ViteProcess.StandardOutput, LogLevel.Information, cancellationToken), CancellationToken.None);
        _ = Task.Run(() => PipeOutput(ViteProcess.StandardError, LogLevel.Warning, cancellationToken), CancellationToken.None);

        await WaitForVite(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (ViteProcess is null || ViteProcess.HasExited)
        {
            return;
        }

        try
        {
            ViteProcess.Kill(entireProcessTree: true);
            await ViteProcess.WaitForExitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TaskCanceledException)
        {
            logger.LogDebug(exception, "Vite development frontend was already stopped.");
        }
    }

    public void Dispose()
    {
        ViteProcess?.Dispose();
    }

    private async Task WaitForVite(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 80 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            if (await IsViteAvailable(cancellationToken))
            {
                logger.LogInformation("Vite development frontend is running at {Url}.", Options.Url);

                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        logger.LogWarning("Vite development frontend did not respond at {Url} before startup continued.", Options.Url);
    }

    private async Task<bool> IsViteAvailable(CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(
                new Uri(new Uri(Options.Url), "/__mainagent_vite_health"),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;

            return string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task PipeOutput(StreamReader reader, LogLevel logLevel, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return;
            }

            logger.Log(logLevel, "Vite: {Line}", line);
        }
    }
}
