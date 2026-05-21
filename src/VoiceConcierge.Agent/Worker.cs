using Microsoft.Extensions.DependencyInjection;

namespace VoiceConcierge.Agent;

public sealed class ConciergeWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<ConciergeWorker> log) : BackgroundService
{
    private const string DefaultRoom = "concierge";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var rooms = ResolveRooms(config);
        log.LogInformation("Concierge host starting {Count} room session(s): {Rooms}",
            rooms.Count, string.Join(", ", rooms));

        var scopes = new List<IServiceScope>(rooms.Count);
        try
        {
            var sessions = new List<Task>(rooms.Count);
            foreach (var room in rooms)
            {
                var scope = scopeFactory.CreateScope();
                scopes.Add(scope);
                var session = scope.ServiceProvider.GetRequiredService<ConciergeSession>();
                sessions.Add(RunRoomAsync(session, room, ct));
            }
            await Task.WhenAll(sessions).ConfigureAwait(false);
        }
        finally
        {
            foreach (var scope in scopes) scope.Dispose();
        }
    }

    private async Task RunRoomAsync(ConciergeSession session, string room, CancellationToken ct)
    {
        try
        {
            await session.RunAsync(room, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            log.LogError(ex, "Room session '{Room}' terminated unexpectedly", room);
        }
    }

    private static IReadOnlyList<string> ResolveRooms(IConfiguration cfg)
    {
        var multi = cfg["LIVEKIT_ROOMS"];
        if (!string.IsNullOrWhiteSpace(multi))
        {
            var list = multi.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .ToArray();
            if (list.Length > 0) return list;
        }
        var single = cfg["LIVEKIT_ROOM"];
        return [string.IsNullOrWhiteSpace(single) ? DefaultRoom : single];
    }
}
