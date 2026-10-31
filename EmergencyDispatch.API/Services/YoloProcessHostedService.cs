using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace EmergencyDispatch.API.Services;

/// <summary>
/// Starts the local Python YOLO service when explicitly enabled. Only its own child process is stopped.
/// Tự động chạy YOLO local; chỉ dừng tiến trình Python do service này tạo.
/// </summary>
public sealed class YoloProcessHostedService(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<YoloProcessHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Model loading must not block the API host or the other background workers.
        await Task.Yield();

        if (!configuration.GetValue("YoloService:Enabled", false)
            || !configuration.GetValue("YoloService:AutoStart", false))
        {
            return;
        }

        Process? ownedProcess = null;
        try
        {
            var address = configuration["YoloService:BaseUrl"] ?? "http://127.0.0.1:8001";
            if (!Uri.TryCreate(address, UriKind.Absolute, out var baseUri)
                || baseUri.Scheme != Uri.UriSchemeHttp
                || !baseUri.IsLoopback
                || baseUri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(baseUri.Query)
                || !string.IsNullOrEmpty(baseUri.UserInfo))
            {
                logger.LogWarning("YOLO AutoStart requires a local HTTP BaseUrl without a path. "
                    + "Tự khởi động chỉ hỗ trợ HTTP local; configure AutoStart=false for remote/container services.");
                return;
            }

            using var client = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false
            }) { Timeout = TimeSpan.FromSeconds(2) };
            var healthUri = new Uri(baseUri, "api/yolo/health");

            var existing = await ProbeAsync(client, healthUri, stoppingToken);
            if (existing != null)
            {
                LogHealth(existing, reused: true);
                return; // Do not own or stop a manually started service.
            }

            if (await IsPortInUseAsync(baseUri, stoppingToken))
            {
                logger.LogWarning("YOLO port {Port} is occupied but the health endpoint is not valid. "
                    + "Cổng đang được sử dụng; no second Python process was started.", baseUri.Port);
                return;
            }

            var configuredDirectory = configuration["YoloService:WorkingDirectory"];
            var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.Combine(environment.ContentRootPath, "..", "YoloService")
                : Path.Combine(environment.ContentRootPath, configuredDirectory));
            if (!File.Exists(Path.Combine(directory, "main.py")))
            {
                throw new FileNotFoundException("YOLO main.py was not found / Không tìm thấy main.py; configure YoloService:WorkingDirectory.");
            }

            var executable = configuration["YoloService:PythonExecutable"];
            if (string.IsNullOrWhiteSpace(executable))
            {
                var virtualEnvironmentPython = OperatingSystem.IsWindows()
                    ? Path.Combine(directory, ".venv", "Scripts", "python.exe")
                    : Path.Combine(directory, ".venv", "bin", "python");
                executable = File.Exists(virtualEnvironmentPython)
                    ? virtualEnvironmentPython
                    : OperatingSystem.IsWindows() ? "python" : "python3";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("-u");
            startInfo.ArgumentList.Add("main.py");
            startInfo.Environment["YOLO_SERVICE_PORT"] = baseUri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            startInfo.Environment["PYTHONUNBUFFERED"] = "1";
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

            ownedProcess = new Process { StartInfo = startInfo };
            ownedProcess.OutputDataReceived += (_, args) => LogPythonOutput(args.Data);
            ownedProcess.ErrorDataReceived += (_, args) => LogPythonOutput(args.Data);
            if (!ownedProcess.Start()) throw new InvalidOperationException("Could not start Python YOLO.");
            ownedProcess.BeginOutputReadLine();
            ownedProcess.BeginErrorReadLine();
            logger.LogInformation("Starting local YOLO / Đang khởi động YOLO local (PID {Pid}, port {Port}). "
                + "Wait for the ready message before testing AI / Chờ thông báo sẵn sàng trước khi test AI.",
                ownedProcess.Id, baseUri.Port);

            var timeout = Math.Clamp(configuration.GetValue("YoloService:StartupTimeoutSeconds", 120), 5, 600);
            var timer = Stopwatch.StartNew();
            var nextProgress = TimeSpan.FromSeconds(15);
            while (true)
            {
                stoppingToken.ThrowIfCancellationRequested();
                if (ownedProcess.HasExited)
                    throw new InvalidOperationException($"Python YOLO exited with code {ownedProcess.ExitCode}.");

                var health = await ProbeAsync(client, healthUri, stoppingToken);
                if (health != null)
                {
                    LogHealth(health, reused: false);
                    break;
                }
                if (timer.Elapsed >= TimeSpan.FromSeconds(timeout))
                    throw new TimeoutException($"YOLO did not become ready within {timeout} seconds.");
                if (timer.Elapsed >= nextProgress)
                {
                    logger.LogInformation("Waiting for YOLO models / Đang chờ nạp model YOLO ({Seconds}s).", (int)timer.Elapsed.TotalSeconds);
                    nextProgress += TimeSpan.FromSeconds(15);
                }
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }

            await ownedProcess.WaitForExitAsync(stoppingToken);
            logger.LogError("Python YOLO stopped unexpectedly / YOLO dừng đột ngột (exit {ExitCode}). "
                + "The API remains running; inspect the Python logs and restart the backend or YOLO.", ownedProcess.ExitCode);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "YOLO AutoStart failed / Tự khởi động YOLO thất bại. "
                + "The API remains running. Check Python, requirements.txt, model files, and YoloService settings.");
        }
        finally
        {
            if (ownedProcess != null) await StopOwnedProcessAsync(ownedProcess);
        }
    }

    private async Task StopOwnedProcessAsync(Process process)
    {
        try
        {
            // A failed Process.Start has no process ID/handle and must not be inspected as a running child.
            if (process.Id > 0 && !process.HasExited)
            {
                logger.LogInformation("Stopping owned YOLO / Đang dừng YOLO do Backend khởi tạo (PID {Pid}).", process.Id);
                process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch (InvalidOperationException)
        {
            // The child already exited, or Process.Start failed before obtaining a handle.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to stop the owned YOLO process / Không thể dừng tiến trình YOLO.");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static async Task<YoloHealth?> ProbeAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
            if (json == null || json.RootElement.ValueKind != JsonValueKind.Object) return null;
            var root = json.RootElement;
            if (!root.TryGetProperty("status", out var status)
                || status.ValueKind != JsonValueKind.String
                || status.GetString() is not ("healthy" or "degraded" or "unhealthy")
                || !root.TryGetProperty("is_accident_fire_ready", out var accidentFire)
                || accidentFire.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("is_jam_ready", out var jam)
                || jam.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            return new YoloHealth(accidentFire.GetBoolean(), jam.GetBoolean());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
    }

    private static async Task<bool> IsPortInUseAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var socket = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            await socket.ConnectAsync(uri.DnsSafeHost, uri.Port, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (SocketException) { return false; }
    }

    private void LogHealth(YoloHealth health, bool reused)
    {
        var source = reused ? "existing service / dịch vụ có sẵn (not owned)" : "child process / tiến trình con";
        if (health.AccidentFireReady && health.JamReady)
            logger.LogInformation("YOLO ready / YOLO sẵn sàng: both models loaded / đã nạp cả hai model ({Source}).", source);
        else
            logger.LogWarning("YOLO is reachable but models are incomplete / YOLO thiếu model "
                + "(accident-fire={AccidentFire}, jam={Jam}, {Source}). Check Python startup logs.",
                health.AccidentFireReady, health.JamReady, source);
    }

    private void LogPythonOutput(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line)) logger.LogInformation("YOLO | {Line}", line);
    }

    private sealed record YoloHealth(bool AccidentFireReady, bool JamReady);
}
