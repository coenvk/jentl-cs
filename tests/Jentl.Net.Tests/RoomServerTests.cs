using System.Text.Json;
using Uno;

namespace Jentl.Net.Tests;

public class RoomServerTests
{
    private static readonly JsonSerializerOptions CardJson = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly RoomServer _server = new(new Dictionary<string, Func<IRoomGame>> { [UnoOnline.Game] = () => new UnoRoomGame() }, seed: 5);

    [Fact]
    public void Messages_RoundTripAsJson()
    {
        var json = Protocol.Write(new CreateRoom("uno", 3));

        json.Should().Contain("\"type\":\"create\"");
        Protocol.ReadClient(json).Should().Be(new CreateRoom("uno", 3));
        Protocol.ReadServer(Protocol.Write(new RoomJoined("1234", "uno", 1, false))).Should().Be(new RoomJoined("1234", "uno", 1, false));
        Protocol.ReadClient("{\"type\":\"nonsense\"}").Should().BeNull();
        Protocol.ReadClient("not json").Should().BeNull();
    }

    [Fact]
    public void CreatingARoom_SeatsTheHost_WithAFourDigitCode()
    {
        var host = new FakeConnection();

        _server.Received(host, Protocol.Write(new CreateRoom("uno", 3)));

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

        guest.Single<RoomJoined>().Should().Be(new RoomJoined(code, "uno", 1, false));
        host.Last<LobbyChanged>().Seats[1].Kind.Should().Be(SeatKind.Human);
    }

    [Theory]
    [InlineData("chess", 2, "doesn't host")]
    [InlineData("uno", 1, "2 to 4")]
    [InlineData("uno", 9, "2 to 4")]
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
        var (host, code) = Host(2);
        var stranger = new FakeConnection();
        _server.Received(stranger, Protocol.Write(new JoinRoom("0000" == code ? "9999" : "0000")));
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
        State(host).View.Seat.Should().Be(0);
        State(guest).View.Seat.Should().Be(1);
    }

    [Fact]
    public void EachPlayer_SeesOnlyTheirOwnHand()
    {
        var (host, code) = Host(2);
        var guest = Join(code);
        _server.Received(host, Protocol.Write(new StartGame()));

        var mine = State(host).View;
        var theirs = State(guest).View;

        mine.Hand.Should().HaveCount(mine.HandCounts[0]);
        theirs.Hand.Should().HaveCount(theirs.HandCounts[1]);
        mine.Hand.Should().NotIntersectWith(theirs.Hand, "no card is in two hands, and nobody is sent another's");
        var sentToHost = string.Concat(host.Raw);
        foreach (var card in theirs.Hand)
        {
            sentToHost.Should().NotContain(Json(card), "the host is never sent a card from the guest's hand");
        }
    }

    [Fact]
    public void PlayingOutOfTurn_IsRefused_AndALegalMoveIsPlayed()
    {
        var (host, code) = Host(2);
        var guest = Join(code);
        _server.Received(host, Protocol.Write(new StartGame()));
        var view = State(host).View;
        var (turn, waiting) = view.CurrentSeat == 0 ? (host, guest) : (guest, host);

        _server.Received(waiting, Act(new UnoPlay(DrawCard.Instance)));
        waiting.Last<ServerError>().Text.Should().Contain("not your turn");

        var move = State(turn).View.LegalMoves[0];
        _server.Received(turn, Act(new UnoPlay(move)));

        State(waiting).Events.Should().NotBeEmpty("everyone hears what was played");
    }

    [Fact]
    public void AnIllegalMove_IsRefused()
    {
        var (host, _) = Host(2);
        _server.Received(host, Protocol.Write(new StartGame()));
        var view = State(host).View;
        for (var i = 0; i < 10 && view.CurrentSeat != 0; i++)
        {
            _server.Update(TimeSpan.FromSeconds(1));
            view = State(host).View;
        }

        view.CurrentSeat.Should().Be(0);

        var unplayable = view.Hand.FirstOrDefault(c => !view.CanPlay(c));
        if (unplayable == default)
        {
            return;
        }

        _server.Received(host, Act(new UnoPlay(new PlayCard(unplayable))));

        host.Last<ServerError>().Text.Should().Contain("can't do that");
    }

    [Fact]
    public void BotsPlayTheirTurns_AsTimePasses()
    {
        var (host, _) = Host(2);
        _server.Received(host, Protocol.Write(new StartGame()));

        // Keep drawing or playing on our turns; the bot must answer each time.
        for (var i = 0; i < 20; i++)
        {
            var view = State(host).View;
            if (view.IsOver)
            {
                break;
            }

            if (view.CurrentSeat == 0)
            {
                _server.Received(host, Act(new UnoPlay(view.LegalMoves[0])));
            }
            else
            {
                _server.Update(TimeSpan.FromSeconds(1));
            }
        }

        host.All<GameState>().Select(s => UnoOnline.ReadState(s.State)!).SelectMany(s => s.Events).OfType<CardPlayed>()
            .Should().Contain(p => p.Seat == 1, "the bot in seat 1 played");
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

    private static string Act(UnoAction action) => Protocol.Write(new PlayerAction(UnoOnline.Write(action)));

    private static string Json(UnoCard card) => JsonSerializer.Serialize(card, CardJson);

    private static UnoOnlineState State(FakeConnection player) => UnoOnline.ReadState(player.Last<GameState>().State)!;

    private (FakeConnection Host, string Code) Host(int seats)
    {
        var host = new FakeConnection();
        _server.Received(host, Protocol.Write(new CreateRoom("uno", seats)));
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

        public bool Closed { get; private set; }

        public void Send(string message) => Raw.Add(message);

        public void Close() => Closed = true;

        public IEnumerable<T> All<T>()
            where T : ServerMessage => Raw.Select(Protocol.ReadServer).OfType<T>();

        public T Single<T>()
            where T : ServerMessage => All<T>().Single();

        public T Last<T>()
            where T : ServerMessage => All<T>().Last();
    }
}
