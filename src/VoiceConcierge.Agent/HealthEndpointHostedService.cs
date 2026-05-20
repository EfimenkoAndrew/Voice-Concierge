using System.Net;

namespace VoiceConcierge.Agent;

public sealed class HealthEndpointHostedService(ILogger<HealthEndpointHostedService> log)
    : IHostedService
{
    private const string HealthPath = "/healthz";
    private const string ListenerPrefix = "http://*:9090/";
    private static readonly TimeSpan StopDrainTimeout = TimeSpan.FromSeconds(2);

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public Task StartAsync(CancellationToken ct)
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(ListenerPrefix);
            _listener.Start();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "healthz listener not started on :9090");
            return Task.CompletedTask;
        }
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptLoop = Task.Run(() => AcceptAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().WaitAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception) { continue; }
            try
            {
                ctx.Response.StatusCode = ctx.Request.Url?.AbsolutePath == HealthPath ? 200 : 404;
                ctx.Response.Close();
            }
            catch { }
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        if (_acceptLoop is not null)
            try { await _acceptLoop.WaitAsync(StopDrainTimeout, ct); } catch { }
        _listener?.Close();
        _cts?.Dispose();
    }
}
