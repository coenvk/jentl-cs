using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jentl.Net;

/// <summary>What a player's device sends to the server. Each message is one JSON text frame with a <c>type</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CreateRoom), "create")]
[JsonDerivedType(typeof(JoinRoom), "join")]
[JsonDerivedType(typeof(StartGame), "start")]
[JsonDerivedType(typeof(PlayerAction), "action")]
[JsonDerivedType(typeof(LeaveRoom), "leave")]
public abstract record ClientMessage;

/// <summary>Opens a room for <paramref name="Game"/> with <paramref name="Seats"/> places; the creator hosts it.</summary>
public sealed record CreateRoom(string Game, int Seats) : ClientMessage;

/// <summary>Takes a free seat in the room with this code.</summary>
public sealed record JoinRoom(string Code) : ClientMessage;

/// <summary>The host begins the game; bots take the seats nobody joined.</summary>
public sealed record StartGame : ClientMessage;

/// <summary>A move or command for the game, in the game's own JSON.</summary>
public sealed record PlayerAction(JsonElement Action) : ClientMessage;

public sealed record LeaveRoom : ClientMessage;

/// <summary>What the server sends to a player's device.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RoomJoined), "joined")]
[JsonDerivedType(typeof(LobbyChanged), "lobby")]
[JsonDerivedType(typeof(GameStarted), "started")]
[JsonDerivedType(typeof(GameState), "state")]
[JsonDerivedType(typeof(ServerError), "error")]
[JsonDerivedType(typeof(RoomClosed), "closed")]
public abstract record ServerMessage;

/// <summary>You are in room <paramref name="Code"/>, in seat <paramref name="Seat"/>.</summary>
public sealed record RoomJoined(string Code, string Game, int Seat, bool IsHost) : ServerMessage;

/// <summary>Who sits where, before and during the game.</summary>
public sealed record LobbyChanged(IReadOnlyList<SeatInfo> Seats) : ServerMessage;

public sealed record GameStarted : ServerMessage;

/// <summary>The game as your seat may see it, in the game's own JSON (with what happened since the last state).</summary>
public sealed record GameState(JsonElement State) : ServerMessage;

/// <summary>A request was refused; the connection stays open.</summary>
public sealed record ServerError(string Text) : ServerMessage;

/// <summary>The room is gone (the host left before starting, or everyone left).</summary>
public sealed record RoomClosed(string Reason) : ServerMessage;

public enum SeatKind
{
    /// <summary>Nobody yet.</summary>
    Open,

    /// <summary>A person on a device.</summary>
    Human,

    /// <summary>A computer player: an empty seat at the start, or a person who left.</summary>
    Bot,
}

public sealed record SeatInfo(int Seat, string Name, SeatKind Kind, bool Connected);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ClientMessage))]
[JsonSerializable(typeof(ServerMessage))]
internal sealed partial class ProtocolJson : JsonSerializerContext;

/// <summary>Turns messages into JSON text and back.</summary>
public static class Protocol
{
    /// <summary>The largest message accepted, in bytes; anything bigger closes the connection.</summary>
    public const int MaxMessageSize = 64 * 1024;

    public static string Write(ClientMessage message) => JsonSerializer.Serialize(message, ProtocolJson.Default.ClientMessage);

    public static string Write(ServerMessage message) => JsonSerializer.Serialize(message, ProtocolJson.Default.ServerMessage);

    /// <summary>The client message in <paramref name="json"/>, or null if it isn't one.</summary>
    public static ClientMessage? ReadClient(string json) => Read(json, ProtocolJson.Default.ClientMessage);

    public static ServerMessage? ReadServer(string json) => Read(json, ProtocolJson.Default.ServerMessage);

    private static T? Read<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
