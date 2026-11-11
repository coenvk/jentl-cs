using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

/// <summary>
/// A sprite sheet as Aseprite exports it (File, Export Sprite Sheet, with JSON data, as an array or a hash): each
/// frame's place on the image and how long it shows, and the tags as <see cref="Animation"/>s by name, playing
/// forwards, backwards or ping-pong as tagged (a tag with a repeat count plays once). Artists draw and tag; the game
/// plays "run" or "jump".
/// </summary>
public sealed class AsepriteSheet
{
    private AsepriteSheet(IReadOnlyList<Rectangle> frames, IReadOnlyList<TimeSpan> durations, IReadOnlyDictionary<string, Animation> tags, string? image)
    {
        Frames = frames;
        Durations = durations;
        Tags = tags;
        Image = image;
    }

    /// <summary>Where each frame is on the image.</summary>
    public IReadOnlyList<Rectangle> Frames { get; }

    public IReadOnlyList<TimeSpan> Durations { get; }

    /// <summary>The tagged animations by name.</summary>
    public IReadOnlyDictionary<string, Animation> Tags { get; }

    /// <summary>The image file the data was exported with, as written in it.</summary>
    public string? Image { get; }

    /// <summary>Every frame in order, looping: the sheet with no tags.</summary>
    public Animation All => new([.. Enumerable.Range(0, Frames.Count)], Durations);

    /// <exception cref="FormatException">The text is not Aseprite's sheet data.</exception>
    public static AsepriteSheet Parse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) ?? throw new FormatException("Empty sheet data.");
            var entries = root["frames"] switch
            {
                JsonArray array => array.ToList(),
                JsonObject hash => hash.Select(pair => pair.Value).ToList(),
                _ => throw new FormatException("The sheet data has no frames."),
            };
            var frames = new List<Rectangle>();
            var durations = new List<TimeSpan>();
            foreach (var entry in entries)
            {
                var frame = entry?["frame"] ?? throw new FormatException("A frame has no place on the image.");
                frames.Add(new Rectangle((int)frame["x"]!, (int)frame["y"]!, (int)frame["w"]!, (int)frame["h"]!));
                durations.Add(TimeSpan.FromMilliseconds((int?)entry!["duration"] ?? 100));
            }

            var tags = new Dictionary<string, Animation>(StringComparer.Ordinal);
            foreach (var tag in root["meta"]?["frameTags"]?.AsArray() ?? [])
            {
                var name = (string?)tag?["name"] ?? throw new FormatException("A tag has no name.");
                var from = Math.Clamp((int)tag!["from"]!, 0, frames.Count - 1);
                var to = Math.Clamp((int)tag["to"]!, from, frames.Count - 1);
                var direction = (string?)tag["direction"] ?? "forward";
                var order = Enumerable.Range(from, to - from + 1).ToList();
                if (direction is "reverse" or "pingpong_reverse")
                {
                    order.Reverse();
                }

                var mode = direction.StartsWith("pingpong", StringComparison.Ordinal) ? AnimationMode.PingPong
                    : tag["repeat"] is not null ? AnimationMode.Once
                    : AnimationMode.Loop;
                tags[name] = new Animation(order, [.. order.Select(i => durations[i])], mode);
            }

            return new AsepriteSheet(frames, durations, tags, (string?)root["meta"]?["image"]);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NullReferenceException)
        {
            throw new FormatException($"Not Aseprite sheet data: {e.Message}", e);
        }
    }

    /// <summary>Draws frame <paramref name="frame"/> of <paramref name="texture"/> (the sheet's image) into <paramref name="destination"/>.</summary>
    public void Draw(SpriteBatch batch, Texture2D texture, int frame, Rectangle destination, Color color, SpriteEffects effects = SpriteEffects.None)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Draw(texture, destination, Frames[frame], color, 0, Vector2.Zero, effects, 0);
    }
}
