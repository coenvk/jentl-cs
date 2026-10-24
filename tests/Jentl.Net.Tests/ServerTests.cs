using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Jentl.Net.Tests;

/// <summary>A room server behind a real ASP.NET Core WebSocket endpoint (in memory), with real <see cref="NetClient"/>s.</summary>
public sealed class ServerTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private WebApplication? _app;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _app = builder.Build();
        var rooms = new RoomServer(new Dictionary<string, Func<IRoomGame>> { [CountingGame.Name] = () => new CountingGame() });
        _app.UseWebSockets();
        _app.Map("/play", async (HttpContext context) =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var connection = new SocketConnection(socket, rooms);
            await connection.RunAsync(context.RequestAborted);
        });
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task TwoPlayers_MeetInARoom_AndPlay()
    {
        using var host = Connect();
        using var guest = Connect();

        host.Send(new CreateRoom(CountingGame.Name, 2));
        var code = (await Next<RoomJoined>(host)).Code;
        guest.Send(new JoinRoom(code));
        (await Next<RoomJoined>(guest)).Seat.Should().Be(1);

        host.Send(new StartGame());
        await Next<GameStarted>(guest);
        CountingGame.Read(await Next<GameState>(host)).Seat.Should().Be(0);
        CountingGame.Read(await Next<GameState>(guest)).Seat.Should().Be(1);

        host.Send(new PlayerAction(JsonSerializer.SerializeToElement(3)));

        CountingGame.Read(await Next<GameState>(guest)).Total.Should().Be(3);
    }

    [Fact]
    public async Task ABadServerAddress_EndsInAnError()
    {
        using var client = new NetClient(_ => throw new System.Net.WebSockets.WebSocketException("refused"));

        var deadline = DateTime.UtcNow + Patience;
        while (client.Status != NetStatus.Closed && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        client.Status.Should().Be(NetStatus.Closed);
        client.Error.Should().Contain("Could not reach");
    }

    private NetClient Connect() => new(async cancel =>
        await _app!.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/play"), cancel));

    /// <summary>Waits for the next message of a kind, skipping others.</summary>
    private static async Task<T> Next<T>(NetClient client)
        where T : ServerMessage
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            while (client.TryReceive(out var message))
            {
                if (message is T wanted)
                {
                    return wanted;
                }
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"No {typeof(T).Name} arrived (status {client.Status}, {client.Error}).");
    }
}
