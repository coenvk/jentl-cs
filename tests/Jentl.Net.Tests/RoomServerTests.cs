using System.Text.Json;

namespace Jentl.Net.Tests;

public class RoomServerTests
{
    private readonly RoomServer _server = new(new Dictionary<string, Func<IRoomGame>> { [CountingGame.Name] = () => new CountingGame() }, seed: 5);

    [Fact]
    public void Messages_RoundTripAsJson()
    {
        var json = Protocol.Write(new CreateRoom("counting", 3));

        json.Should().Contain("\"type\":\"create\"");
        Protocol.ReadClient(json).Should().Be(new CreateRoom("counting", 3));
        Protocol.ReadServer(Protocol.Write(new RoomJoined("1234", "counting", 1, false))).Should().Be(new RoomJoined("1234", "counting", 1, false));
        Protocol.ReadClient("{\"type\":\"nonsense\"}").Should().BeNull();
        Protocol.ReadClient("not json").Should().BeNull();
    }

    [Fact]
    public void CreatingARoom_SeatsTheHost_WithAFourDigitCode()
    {
        var host = new FakeConnection();

        _server.Received(host, Protocol.Write(new CreateRoom(CountingGame.Name, 3)));

        var joined = host.Single<RoomJoined>();
        joined.Code.Should().MatchRegex("^[0-9]{4}$");
        joined.Seat.Should().Be(0);
        joined.IsHost.Should().BeTrue();
        host.Last<LobbyChanged>().Seats.Select(s => s.Kind).Should().Equal(SeatKind.Human, SeatKind.Open, SeatKind.Open);
        _server.RoomCount.Should().Be(1);
    }

    [Fact]
    public void Joining_TakesTheNextSeat_AndTellsEveryone()
    {
        var (host, code) = Host(3);
        var guest = new FakeConnection();

        _server.Received(guest, Protocol.Write(new JoinRoom(code)));

        guest.Single<RoomJoined>().Should().Be(new RoomJoined(code, CountingGame.Name, 1, false));
        host.Last<LobbyChanged>().Seats[1].Kind.Should().Be(SeatKind.Human);
    }

    [Theory]
    [InlineData("chess", 2, "doesn't host")]
    [InlineData(CountingGame.Name, 1, "2 to 4")]
    [InlineData(CountingGame.Name, 9, "2 to 4")]
    public void BadRooms_AreRefused(string game, int seats, string reason)
    {
        var player = new FakeConnection();

        _server.Received(player, Protocol.Write(new CreateRoom(game, seats)));

        player.Single<ServerError>().Text.Should().Contain(reason);
        _server.RoomCount.Should().Be(0);
    }

    [Fact]
    public void JoiningAMissingFullOrStartedRoom_IsRefused()
    {
        var (_, code) = Host(2);
        var stranger = new FakeConnection();
        _server.Received(stranger, Protocol.Write(new JoinRoom(code == "0000" ? "9999" : "0000")));
        stranger.Last<ServerError>().Text.Should().Contain("no room");

        _server.Received(new FakeConnection(), Protocol.Write(new JoinRoom(code)));
        _server.Received(stranger, Protocol.Write(new JoinRoom(code)));
        stranger.Last<ServerError>().Text.Should().Contain("full");

        var (otherHost, other) = Host(3);
        _server.Received(otherHost, Protocol.Write(new StartGame()));
        _server.Received(stranger, Protocol.Write(new JoinRoom(other)));
        stranger.Last<ServerError>().Text.Should().Contain("already started");
    }

    [Fact]
    public void OnlyTheHost_Starts_AndBotsFillTheEmptySeats()
    {
        var (host, code) = Host(4);
        var guest = Join(code);

        _server.Received(guest, Protocol.Write(new StartGame()));
        guest.Last<ServerError>().Text.Should().Contain("Only the host");

        _server.Received(host, Protocol.Write(new StartGame()));

        host.All<GameStarted>().Should().ContainSingle();
        host.Last<LobbyChanged>().Seats.Select(s => s.Kind).Should().Equal(SeatKind.Human, SeatKind.Human, SeatKind.Bot, SeatKind.Bot);
        State(host).Seat.Should().Be(0);
        State(guest).Seat.Should().Be(1);
    }

    [Fact]
    public void EachPlayer_IsSentOnlyTheirOwnSecrets()
    {
        var (host, code) = Host(2);
        var guest = Join(code);
        _server.Received(host, Protocol.Write(new StartGame()));

        var mine = State(host).Secret;
        var theirs = State(guest).Secret;

        mine.Should().NotBe(theirs);
        string.Concat(host.Raw).Should().NotContain(theirs.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PlayingOutOfTurn_IsRefused_AndALegalMoveIsPlayed()
    {
        var (host, code) = Host(2);
        var guest = Join(code);
        _server.Received(host, Protocol.Write(new StartGame()));

        _server.Received(guest, Act(2));
        guest.Last<ServerError>().Text.Should().Contain("not your turn");

        _server.Received(host, Act(2));

        State(guest).Total.Should().Be(2, "everyone hears what was played");
        State(guest).Events.Should().Contain("0+2");
    }

    [Fact]
    public void AnIllegalMove_IsRefused()
    {
        var (host, _) = Host(2);
        _server.Received(host, Protocol.Write(new StartGame()));

        _server.Received(host, Act(7));

        host.Last<ServerError>().Text.Should().Contain("1, 2 or 3");
    }

    [Fact]
    public void BotsPlayTheirTurns_AsTimePasses()
    {
        var (host, _) = Host(2);
        _server.Received(host, Protocol.Write(new StartGame()));
        _server.Received(host, Act(3));

        _server.Update(TimeSpan.FromSeconds(1));

        State(host).Total.Should().Be(4);
        State(host).Turn.Should().Be(0, "the bot in seat 1 has played");
    }

    [Fact]
    public void APlayerWhoLeavesMidGame_IsReplacedByABot()
    {
        var (host, code) = Host(2);
        var guest = Join(code);
        _server.Received(host, Protocol.Write(new StartGame()));

        _server.Disconnected(guest);

        host.Last<LobbyChanged>().Seats[1].Kind.Should().Be(SeatKind.Bot);
        State(host).Names[1].Should().EndWith("(bot)");
        _server.RoomCount.Should().Be(1);
    }

    [Fact]
    public void TheHostLeavingTheLobby_ClosesTheRoom()
    {
        var (host, code) = Host(3);
        var guest = Join(code);

        _server.Received(host, Protocol.Write(new LeaveRoom()));

        guest.Last<RoomClosed>().Reason.Should().Contain("host");
        _server.RoomCount.Should().Be(0);
    }

    [Fact]
    public void ARoomNobodyIsInAnyMore_Closes()
    {
        var (host, _) = Host(2);
        _server.Received(host, Protocol.Write(new StartGame()));

        _server.Disconnected(host);

        _server.RoomCount.Should().Be(0);
    }

    [Fact]
    public void NonsenseIsAnsweredWithAnError()
    {
        var player = new FakeConnection();

        _server.Received(player, "{\"type\":\"teleport\"}");
        _server.Received(player, Protocol.Write(new StartGame()));

        player.All<ServerError>().Should().HaveCount(2);
    }

    private static string Act(int amount) => Protocol.Write(new PlayerAction(JsonSerializer.SerializeToElement(amount)));

    private static CountingGame.State State(FakeConnection player) => CountingGame.Read(player.Last<GameState>());

    private (FakeConnection Host, string Code) Host(int seats)
    {
        var host = new FakeConnection();
        _server.Received(host, Protocol.Write(new CreateRoom(CountingGame.Name, seats)));
        return (host, host.Single<RoomJoined>().Code);
    }

    private FakeConnection Join(string code)
    {
        var guest = new FakeConnection();
        _server.Received(guest, Protocol.Write(new JoinRoom(code)));
        return guest;
    }

    private sealed class FakeConnection : IConnection
    {
        public List<string> Raw { get; } = [];

        public void Send(string message) => Raw.Add(message);

        public void Close()
        {
        }

        public IEnumerable<T> All<T>()
            where T : ServerMessage => Raw.Select(Protocol.ReadServer).OfType<T>();

        public T Single<T>()
            where T : ServerMessage => All<T>().Single();

        public T Last<T>()
            where T : ServerMessage => All<T>().Last();
    }
}
