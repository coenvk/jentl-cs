namespace Jentl.Collect;

/// <summary>
/// Names made up for players, such as "Swift Otter 27": an adjective, an animal and a number from 10 to 99. A player
/// gets one without typing, can ask for another, and a server can check that a name is one of these, so nobody can
/// put words of their own on a public leaderboard.
/// </summary>
public static class PlayerNames
{
    private static readonly string[] Adjectives =
    [
        "Brave", "Bright", "Bold", "Calm", "Clever", "Cosy", "Daring", "Eager", "Fast", "Fearless", "Gentle", "Golden",
        "Happy", "Jolly", "Keen", "Kind", "Lively", "Lucky", "Merry", "Mighty", "Nimble", "Noble", "Plucky", "Proud",
        "Quick", "Quiet", "Rapid", "Shiny", "Silver", "Sly", "Speedy", "Steady", "Sunny", "Swift", "Tiny", "Witty",
    ];

    private static readonly string[] Animals =
    [
        "Badger", "Bear", "Beaver", "Bison", "Crane", "Deer", "Dolphin", "Eagle", "Falcon", "Ferret", "Fox", "Gecko",
        "Hare", "Hawk", "Heron", "Ibis", "Koala", "Lark", "Lemur", "Lion", "Lynx", "Marten", "Mole", "Moose", "Newt",
        "Otter", "Owl", "Panda", "Puffin", "Robin", "Seal", "Stork", "Swan", "Tiger", "Toad", "Wolf",
    ];

    /// <summary>A new name.</summary>
    public static string Make(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return $"{Adjectives[random.Next(Adjectives.Length)]} {Animals[random.Next(Animals.Length)]} {random.Next(10, 100)}";
    }

    /// <summary>Whether <paramref name="name"/> is one that <see cref="Make"/> could have made.</summary>
    public static bool IsValid(string? name)
    {
        var parts = name?.Split(' ');
        return parts is [var adjective, var animal, var number]
            && Adjectives.Contains(adjective, StringComparer.Ordinal)
            && Animals.Contains(animal, StringComparer.Ordinal)
            && number.Length == 2 && number.All(char.IsAsciiDigit) && number[0] != '0';
    }
}
