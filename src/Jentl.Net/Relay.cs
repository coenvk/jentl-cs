using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jentl.Net;

/// <summary>
/// What a runner (the program that runs a bot on its owner's computer) and the game server say to each other. Each
/// message is one JSON text frame with a <c>type</c>; the bot's own lines travel inside <see cref="RelayLine"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RelayHello), "hello")]
[JsonDerivedType(typeof(RelayLine), "line")]
[JsonDerivedType(typeof(RelayExited), "exited")]
[JsonDerivedType(typeof(RelayWelcome), "welcome")]
[JsonDerivedType(typeof(RelayRefused), "refused")]
[JsonDerivedType(typeof(RelayStart), "start")]
[JsonDerivedType(typeof(RelayStop), "stop")]
[JsonDerivedType(typeof(RelayStatus), "status")]
public abstract record RelayMessage;

/// <summary>Runner to server, first: the bot's secret token and the <see cref="Relay.Version"/> the runner speaks.</summary>
public sealed record RelayHello(string Token, int Version) : RelayMessage;

/// <summary>Either way: one line the agent wrote (runner to server), or one line for the agent (server to runner).</summary>
public sealed record RelayLine(string Text) : RelayMessage;

/// <summary>Runner to server: the agent ended by itself; <paramref name="Problem"/> is its <see cref="IAgent.Problem"/>.</summary>
public sealed record RelayExited(string? Problem) : RelayMessage;

/// <summary>Server to runner: the token is good; the bot plays as <paramref name="Name"/>.</summary>
public sealed record RelayWelcome(string Name) : RelayMessage;

/// <summary>Server to runner: the hello was turned away, and why; the server then closes the connection.</summary>
public sealed record RelayRefused(string Reason) : RelayMessage;

/// <summary>Server to runner: start a fresh agent process (stopping one that still runs).</summary>
public sealed record RelayStart : RelayMessage;

/// <summary>Server to runner: stop the agent.</summary>
#pragma warning disable CA1716 // Stop is the protocol's word; it is only a keyword in Visual Basic.
public sealed record RelayStop : RelayMessage;
#pragma warning restore CA1716

/// <summary>Server to runner: something for the runner to show its owner, in the game's own JSON.</summary>
public sealed record RelayStatus(JsonElement Status) : RelayMessage;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(RelayMessage))]
internal sealed partial class RelayJson : JsonSerializerContext;

/// <summary>Turns relay messages into JSON text and back.</summary>
public static class Relay
{
    /// <summary>The version of the relay protocol this side speaks, sent in <see cref="RelayHello"/>.</summary>
    public const int Version = 1;

    public static string Write(RelayMessage message) => JsonSerializer.Serialize(message, RelayJson.Default.RelayMessage);

    /// <summary>The message in <paramref name="text"/>, or null for bad JSON, an unknown type or a missing field.</summary>
    public static RelayMessage? Read(string text)
    {
        try
        {
            return JsonSerializer.Deserialize(text, RelayJson.Default.RelayMessage);
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
