using System.Text.Json;

namespace Jentl.Cards.Rulesets;

/// <summary>
/// Reads a <see cref="CardRuleset"/> from JSON, strictly: an unknown field or value is an error naming where it is
/// (<c>piles[2].build.rank: 'upp' isn't one of up, down, updown, same, any</c>), so a typo never quietly changes a game.
/// </summary>
internal static class RulesetReader
{
    public static CardRuleset Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The ruleset isn't valid JSON: {exception.Message}", exception);
        }

        using (document)
        {
            var root = new Node(document.RootElement, "ruleset");
            var ruleset = ReadRuleset(root);
            Check(ruleset);
            return ruleset;
        }
    }

    private static CardRuleset ReadRuleset(Node root)
    {
        root.Allow("name", "description", "deck", "jokers", "players", "piles", "effects", "turns", "win", "script", "pairs");
        var (min, max, preferred) = root.Get("players") is { } players ? ReadPlayers(players) : (1, 1, 1);
        var deck = ReadDeck(root.Get("deck") ?? throw root.Error("needs a deck (\"standard\", \"uno\" or one of its own)"), root.Get("jokers"));
        var piles = root.Require("piles").Items().Select((pile, _) => ReadPile(pile)).ToList();
        return new CardRuleset(root.Require("name").Text(), deck, piles, ReadWin(root.Get("win"), max > 1))
        {
            Description = root.Get("description")?.Text() ?? "",
            MinPlayers = min,
            MaxPlayers = max,
            DefaultPlayers = preferred,
            Effects = root.Get("effects")?.Items().Select(ReadEffect).ToList() ?? [],
            Turns = root.Get("turns") is { } turns ? ReadTurns(turns) : new TurnRules(),
            Script = root.Get("script") is { } script ? script.Source() : null,
            Pairs = root.Get("pairs") is { } pairs ? ReadPairs(pairs, piles) : null,
        };
    }

    private static PairRule ReadPairs(Node node, IReadOnlyList<PileRule> piles)
    {
        node.Allow("sum", "from", "to", "alone");
        var names = piles.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        string Known(Node name) => names.Contains(name.Text()) ? name.Text() : throw name.Error($"'{name.Text()}' isn't one of the piles");
        var from = node.Require("from").Items().Select(Known).ToList();
        if (from.Count == 0)
        {
            throw node.Error("pairs need piles to come \"from\"");
        }

        return new PairRule(node.Require("sum").Whole(2, 200), from, Known(node.Require("to")))
        {
            Alone = node.Get("alone")?.Items().Select(a => a.Whole(1, 200)).ToList() ?? [],
        };
    }

    private static (int Min, int Max, int Default) ReadPlayers(Node node)
    {
        if (node.Element.ValueKind == JsonValueKind.Number)
        {
            var count = node.Whole(1, 10);
            return (count, count, count);
        }

        node.Allow("min", "max", "default");
        var min = node.Require("min").Whole(1, 10);
        var max = node.Require("max").Whole(min, 10);
        var preferred = node.Get("default")?.Whole(min, max) ?? min;
        return (min, max, preferred);
    }

    private static CardDeck ReadDeck(Node node, Node? jokers)
    {
        CardDeck deck;
        if (node.Element.ValueKind == JsonValueKind.String)
        {
            deck = node.Text() switch
            {
                "standard" => CardDeck.Standard(),
                "uno" => CardDeck.Uno(),
                var other => throw node.Error($"'{other}' isn't a deck Jentl knows (\"standard\" or \"uno\"); describe its suits and ranks instead"),
            };
        }
        else if (node.Get("standard") is { } standard)
        {
            node.Allow("standard", "suits");
            var suits = node.Get("suits")?.Items().Select(s => s.Choice("suit", "clubs", "diamonds", "hearts", "spades")).Select(i => (Suit)i).ToList();
            deck = CardDeck.Standard(standard.Whole(1, 8), suits);
        }
        else
        {
            node.Allow("name", "suits", "ranks", "extra");
            var suits = node.Require("suits").Items().Select(ReadSuit).ToList();
            var ranks = node.Require("ranks").Items().Select(ReadRank).ToList();
            var extra = node.Get("extra")?.Items().Select(ReadExtra).ToList() ?? [];
            try
            {
                deck = CardDeck.Custom(node.Get("name")?.Text() ?? "custom", suits, ranks, extra);
            }
            catch (ArgumentException exception)
            {
                throw node.Error(exception.Message);
            }
        }

        if (jokers is not null)
        {
            var joker = new CardFace("joker", null) { Label = "JK", Order = 0, Points = 50, Tags = ["wild", "joker"], Ink = "#7a2a9a", Code = "joker" };
            deck = deck.With([(joker, jokers.Whole(0, 8))]);
        }

        return deck;
    }

    private static SuitDesign ReadSuit(Node node)
    {
        node.Allow("name", "symbol", "colour", "ink", "fill");
        return new SuitDesign(node.Require("name").Text())
        {
            Symbol = node.Get("symbol")?.Text() ?? "",
            Colour = node.Get("colour")?.Text(),
            Ink = node.Get("ink")?.Colour() ?? "#1c1e28",
            Fill = node.Get("fill")?.Colour(),
        };
    }

    private static RankDesign ReadRank(Node node, int index)
    {
        node.Allow("name", "order", "label", "points", "copies", "tags");
        return new RankDesign(node.Require("name").Text(), node.Get("order")?.Whole(-1000, 1000) ?? index + 1)
        {
            Label = node.Get("label")?.Text(),
            Points = node.Get("points")?.Whole(-1000, 1000) ?? 0,
            Copies = node.Get("copies")?.Whole(1, 20) ?? 1,
            Tags = node.Get("tags")?.Items().Select(t => t.Text()).ToList() ?? [],
        };
    }

    private static (CardFace Face, int Count) ReadExtra(Node node)
    {
        node.Allow("rank", "label", "order", "points", "count", "tags", "ink", "fill");
        var rank = node.Require("rank").Text();
        var face = new CardFace(rank, null)
        {
            Label = node.Get("label")?.Text() ?? rank,
            Order = node.Get("order")?.Whole(-1000, 1000) ?? 0,
            Points = node.Get("points")?.Whole(-1000, 1000) ?? 0,
            Tags = node.Get("tags")?.Items().Select(t => t.Text()).ToList() ?? [],
            Ink = node.Get("ink")?.Colour() ?? "#1c1e28",
            Fill = node.Get("fill")?.Colour(),
        };
        return (face, node.Get("count")?.Whole(1, 50) ?? 1);
    }

    private static PileRule ReadPile(Node node)
    {
        node.Allow("name", "count", "perPlayer", "goal", "deal", "build", "take", "click", "complete", "flipTop", "at", "step", "fan", "coveredBy", "places");
        var count = node.Get("count")?.Whole(1, 52) ?? 1;
        var goal = node.Get("goal")?.Flag() ?? false;
        var build = node.Get("build") is { } b ? ReadBuild(b) : BuildRule.None;
        if (goal && node.Get("build")?.Get("runs") is null)
        {
            // Foundations take one card at a time unless they say otherwise.
            build = build with { Runs = false };
        }

        return new PileRule(node.Require("name").Name())
        {
            Count = count,
            PerPlayer = node.Get("perPlayer")?.Flag() ?? false,
            Goal = goal,
            Deal = node.Get("deal") is { } deal ? ReadDeal(deal, count) : new DealRule(),
            Build = build,
            Take = node.Get("take") is { } take ? ReadTake(take, build) : new TakeRule(TakeKind.None),
            Click = node.Get("click") is { } click ? ReadClick(click) : null,
            Complete = node.Get("complete") is { } complete ? ReadComplete(complete) : null,
            FlipTop = node.Get("flipTop")?.Flag() ?? true,
            At = node.Get("at")?.Pair() ?? (0, 0),
            Step = node.Get("step")?.Pair() ?? (1, 0),
            Fan = node.Get("fan") is { } fan ? (PileFan)fan.Choice("fan", "stacked", "down", "right") : PileFan.Stacked,
            CoveredBy = node.Get("coveredBy") is { } covered ? ReadCovers(covered, count) : null,
            Places = node.Get("places") is { } places ? [.. places.Items().Select(p => p.Pair())] : null,
        };
    }

    private static List<IReadOnlyList<int>> ReadCovers(Node node, int count)
    {
        var covers = node.Items().Select(item => (IReadOnlyList<int>)[.. item.Items().Select(i => i.Whole(0, count - 1))]).ToList();
        if (covers.Count != count)
        {
            throw node.Error($"needs a list for each of the {count} piles");
        }

        return covers;
    }

    private static DealRule ReadDeal(Node node, int count)
    {
        node.Allow("cards", "rest", "faceUp", "faceDown", "not");
        var faceUp = node.Get("faceUp");
        return new DealRule
        {
            Counts = node.Get("cards") is { } cards ? cards.Counts(count) : [0],
            Rest = node.Get("rest")?.Flag() ?? false,
            FaceDown = node.Get("faceDown")?.Counts(count),
            FaceUp = faceUp is null || (faceUp.Element.ValueKind == JsonValueKind.String ? faceUp.Choice("faceUp", "top", "all") >= 0 : faceUp.Flag()),
            TopOnly = faceUp?.Element.ValueKind == JsonValueKind.String && faceUp.Text() == "top",
            Open = faceUp?.Element.ValueKind == JsonValueKind.String && faceUp.Text() == "all",
            Not = node.Get("not")?.Text(),
        };
    }

    private static BuildRule ReadBuild(Node node)
    {
        if (node.Element.ValueKind == JsonValueKind.String)
        {
            return node.Choice("build", "none", "any") == 0 ? BuildRule.None : new BuildRule();
        }

        node.Allow("rank", "wrap", "suit", "match", "wild", "empty", "max", "from", "runs", "check");
        var match = node.Get("match");
        return new BuildRule
        {
            Rank = node.Get("rank") is { } rank ? ReadStep(rank) : RankStep.Any,
            Wrap = node.Get("wrap")?.Flag() ?? false,
            Suit = node.Get("suit") is { } suit ? ReadSuitMatch(suit) : SuitMatch.Any,
            MatchSuitOrRank = match is not null && match.Choice("match", "suitOrRank") == 0,
            Empty = node.Get("empty")?.Text() is { } empty && empty != "any" ? empty : null,
            Max = node.Get("max")?.Whole(0, 200) ?? 0,
            From = node.Get("from")?.Items().Select(f => f.Text()).ToList() ?? [],
            Runs = node.Get("runs")?.Flag() ?? true,
            Wild = node.Get("wild")?.Items().Select(w => w.Text()).ToList() ?? [],
            Check = node.Get("check")?.Text(),
        };
    }

    private static RankStep ReadStep(Node node) => (RankStep)node.Choice("rank", "any", "up", "down", "updown", "same");

    private static SuitMatch ReadSuitMatch(Node node) => (SuitMatch)node.Choice("suit", "any", "same", "alternate", "samecolour", "different");

    private static TakeRule ReadTake(Node node, BuildRule build)
    {
        // A run keeps to the pile's own building order unless it says otherwise.
        var fallbackRank = build.Rank is RankStep.Down or RankStep.Up ? build.Rank : RankStep.Down;
        if (node.Element.ValueKind == JsonValueKind.String)
        {
            return new TakeRule((TakeKind)node.Choice("take", "none", "top", "run", "any", "one")) { RunRank = fallbackRank, RunSuit = build.Suit };
        }

        node.Allow("kind", "rank", "suit", "limit");
        return new TakeRule((TakeKind)node.Require("kind").Choice("kind", "none", "top", "run", "any", "one"))
        {
            RunRank = node.Get("rank") is { } rank ? ReadStep(rank) : fallbackRank,
            RunSuit = node.Get("suit") is { } suit ? ReadSuitMatch(suit) : build.Suit,
            Limit = node.Get("limit") is { } limit ? (RunLimit)limit.Choice("limit", "none", "freecells") : RunLimit.None,
        };
    }

    private static ClickRule ReadClick(Node node)
    {
        node.Allow("deal", "to", "each", "faceUp", "requireFilled", "redealFrom", "redeals");
        return new ClickRule
        {
            Deal = node.Get("deal")?.Whole(1, 52) ?? 1,
            To = node.Require("to").Text(),
            Each = node.Get("each")?.Flag() ?? false,
            FaceUp = node.Get("faceUp")?.Flag() ?? true,
            RequireFilled = node.Get("requireFilled")?.Flag() ?? false,
            RedealFrom = node.Get("redealFrom")?.Text(),
            Redeals = node.Get("redeals")?.Whole(-1, 100) ?? -1,
        };
    }

    private static CompleteRule ReadComplete(Node node)
    {
        node.Allow("length", "to", "rank", "suit");
        return new CompleteRule(node.Require("length").Whole(2, 52), node.Require("to").Text())
        {
            Rank = node.Get("rank") is { } rank ? ReadStep(rank) : RankStep.Down,
            Suit = node.Get("suit") is { } suit ? ReadSuitMatch(suit) : SuitMatch.Same,
        };
    }

    private static CardEffect ReadEffect(Node node)
    {
        node.Allow("rank", "suit", "tag", "skip", "reverse", "draw", "chooseSuit", "playAgain", "script");
        var selector = new CardSelector { Rank = node.Get("rank")?.Text(), Suit = node.Get("suit")?.Text(), Tag = node.Get("tag")?.Text() };
        if (selector is { Rank: null, Suit: null, Tag: null })
        {
            throw node.Error("needs a rank, suit or tag to say which cards it is for");
        }

        return new CardEffect(selector)
        {
            Skip = node.Get("skip")?.Whole(0, 9) ?? 0,
            Reverse = node.Get("reverse")?.Flag() ?? false,
            Draw = node.Get("draw")?.Whole(0, 20) ?? 0,
            ChooseSuit = node.Get("chooseSuit")?.Flag() ?? false,
            PlayAgain = node.Get("playAgain")?.Flag() ?? false,
            Script = node.Get("script")?.Text(),
        };
    }

    private static TurnRules ReadTurns(Node node)
    {
        node.Allow("stock", "discard", "hand", "draw", "afterDraw", "afterPenalty", "reshuffle");
        return new TurnRules
        {
            Stock = node.Get("stock")?.Text() ?? "stock",
            Discard = node.Get("discard")?.Text() ?? "discard",
            Hand = node.Get("hand")?.Text() ?? "hand",
            DrawCount = node.Get("draw")?.Whole(1, 10) ?? 1,
            AfterDraw = node.Get("afterDraw") is { } after ? (AfterDraw)after.Choice("afterDraw", "mayPlay", "pass", "untilPlayable") : AfterDraw.MayPlay,
            AfterPenalty = node.Get("afterPenalty") is { } penalty ? (AfterDraw)penalty.Choice("afterPenalty", "mayPlay", "pass", "untilPlayable") : null,
            Reshuffle = node.Get("reshuffle")?.Flag() ?? true,
        };
    }

    private static WinRule ReadWin(Node? node, bool players)
    {
        if (node is null)
        {
            return new WinRule(players ? WinKind.EmptyHand : WinKind.AllOnGoals);
        }

        if (node.Element.ValueKind == JsonValueKind.String)
        {
            return new WinRule(node.Choice("win", "goals", "emptyHand") == 0 ? WinKind.AllOnGoals : WinKind.EmptyHand);
        }

        node.Allow("empty");
        return new WinRule(WinKind.Empty) { Piles = node.Require("empty").Items().Select(p => p.Text()).ToList() };
    }

    /// <summary>Checks what the parts say about each other: piles they name exist, effects match some card.</summary>
    private static void Check(CardRuleset ruleset)
    {
        var groups = ruleset.Piles.Select(p => p.Name).ToList();
        if (groups.Distinct(StringComparer.Ordinal).Count() != groups.Count)
        {
            throw new InvalidDataException("ruleset.piles: two piles have the same name.");
        }

        var names = ruleset.Piles.SelectMany(p => p.PerPlayer ? [p.Name] : p.PileNames(1).Append(p.Name)).ToHashSet(StringComparer.Ordinal);
        void Exists(string? name, string where)
        {
            if (name is not null && !names.Contains(name))
            {
                throw new InvalidDataException($"{where}: there is no pile '{name}' (the piles are {string.Join(", ", groups)}).");
            }
        }

        for (var i = 0; i < ruleset.Piles.Count; i++)
        {
            var pile = ruleset.Piles[i];
            var where = $"ruleset.piles[{i}]";
            Exists(pile.Click?.To, $"{where}.click.to");
            Exists(pile.Click?.RedealFrom, $"{where}.click.redealFrom");
            Exists(pile.Complete?.To, $"{where}.complete.to");
            foreach (var from in pile.Build.From)
            {
                Exists(from, $"{where}.build.from");
            }

            if (pile.Build.Empty is { } empty and not "none" && !ruleset.Deck.Faces.Any(f => f.Rank == empty))
            {
                throw new InvalidDataException($"{where}.build.empty: the deck has no rank '{empty}'.");
            }
        }

        foreach (var name in ruleset.Win.Piles)
        {
            Exists(name, "ruleset.win.empty");
        }

        if (!ruleset.IsSolitaire)
        {
            var turns = ruleset.Turns;
            Exists(turns.Stock, "ruleset.turns.stock");
            Exists(turns.Discard, "ruleset.turns.discard");
            if (!ruleset.Piles.Any(p => p.PerPlayer && p.Name == turns.Hand))
            {
                throw new InvalidDataException($"ruleset.turns.hand: a game for several players needs a pile '{turns.Hand}' with \"perPlayer\": true.");
            }
        }

        for (var i = 0; i < ruleset.Effects.Count; i++)
        {
            if (!ruleset.Deck.Faces.Any(ruleset.Effects[i].Cards.Matches))
            {
                throw new InvalidDataException($"ruleset.effects[{i}]: no card in the deck is {Describe(ruleset.Effects[i].Cards)}.");
            }
        }

        CheckScript(ruleset);

        if (ruleset.Win.Kind == WinKind.AllOnGoals && !ruleset.Piles.Any(p => p.Goal))
        {
            throw new InvalidDataException("ruleset.win: to win with every card on a goal pile, mark the foundations \"goal\": true.");
        }
    }

    /// <summary>Compiles the script and runs its top level, and checks the functions the ruleset names are in it.</summary>
    private static void CheckScript(CardRuleset ruleset)
    {
        var named = ruleset.Piles.Select((p, i) => (Name: p.Build.Check, Where: $"ruleset.piles[{i}].build.check"))
            .Concat(ruleset.Effects.Select((e, i) => (Name: e.Script, Where: $"ruleset.effects[{i}].script")))
            .Where(n => n.Name is not null)
            .ToList();
        if (ruleset.Script is not { } source)
        {
            if (named.Count > 0)
            {
                throw new InvalidDataException($"{named[0].Where}: names the function '{named[0].Name}', but the ruleset has no \"script\".");
            }

            return;
        }

        RulesetScript script;
        try
        {
            script = new RulesetScript(source, ruleset.Name, 0);
        }
        catch (Jentl.Script.ScriptException mistake)
        {
            throw new InvalidDataException($"ruleset.script: {mistake.Message}", mistake);
        }

        foreach (var (name, where) in named)
        {
            if (!script.Has(name!))
            {
                throw new InvalidDataException($"{where}: the script has no function '{name}'.");
            }
        }
    }

    private static string Describe(CardSelector cards) =>
        string.Join(" and ", new[] { cards.Rank is null ? null : $"rank '{cards.Rank}'", cards.Suit is null ? null : $"suit '{cards.Suit}'", cards.Tag is null ? null : $"tagged '{cards.Tag}'" }.OfType<string>());

    /// <summary>A JSON value and where it is, for error messages.</summary>
    private sealed class Node(JsonElement element, string path)
    {
        public JsonElement Element { get; } = element;

        public InvalidDataException Error(string message) => new($"{path}: {message}.");

        public void Allow(params string[] fields)
        {
            if (Element.ValueKind != JsonValueKind.Object)
            {
                throw Error($"should be an object with {string.Join(", ", fields)}");
            }

            foreach (var property in Element.EnumerateObject())
            {
                if (!fields.Contains(property.Name, StringComparer.Ordinal))
                {
                    throw new InvalidDataException($"{path}.{property.Name}: isn't a field here (it may have {string.Join(", ", fields)}).");
                }
            }
        }

        public Node? Get(string name) =>
            Element.ValueKind == JsonValueKind.Object && Element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
                ? new Node(value, $"{path}.{name}")
                : null;

        public Node Require(string name) => Get(name) ?? throw Error($"needs \"{name}\"");

        public List<Node> Items() => Element.ValueKind == JsonValueKind.Array
            ? Element.EnumerateArray().Select((item, i) => new Node(item, $"{path}[{i}]")).ToList()
            : throw Error("should be a list");

        public string Text() => Element.ValueKind == JsonValueKind.String ? Element.GetString()! : throw Error("should be text");

        /// <summary>A pile name: letters and digits, not ending in a digit (piles of a group get one).</summary>
        public string Name()
        {
            var name = Text();
            return name.Length > 0 && name.All(char.IsLetterOrDigit) && !char.IsDigit(name[^1])
                ? name
                : throw Error($"'{name}' should be letters and digits, not ending in a digit");
        }

        /// <summary>Script source: one string, or a list of lines (JSON has no text across lines).</summary>
        public string Source() => Element.ValueKind == JsonValueKind.Array
            ? string.Join('\n', Items().Select(line => line.Text()))
            : Text();

        public bool Flag() => Element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Error("should be true or false"),
        };

        public int Whole(int min, int max) =>
            Element.ValueKind == JsonValueKind.Number && Element.TryGetInt32(out var value) && value >= min && value <= max
                ? value
                : throw Error($"should be a whole number from {min} to {max}");

        public int Choice(string what, params string[] options)
        {
            var text = Text();
            var index = Array.FindIndex(options, o => string.Equals(o, text, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : throw Error($"'{text}' isn't a {what} (use {string.Join(", ", options)})");
        }

        public string Colour()
        {
            var text = Text();
            return text.Length == 7 && text[0] == '#' && text[1..].All(Uri.IsHexDigit) ? text : throw Error($"'{text}' should be a colour as #rrggbb");
        }

        public (float X, float Y) Pair()
        {
            var items = Items().ToList();
            return items.Count == 2 && items.All(i => i.Element.ValueKind == JsonValueKind.Number)
                ? (items[0].Element.GetSingle(), items[1].Element.GetSingle())
                : throw Error("should be two numbers, [x, y]");
        }

        /// <summary>Cards per pile of a group: a number for all, a list by index, or "index" / "index+N".</summary>
        public List<int> Counts(int count)
        {
            switch (Element.ValueKind)
            {
                case JsonValueKind.Number:
                    return [Whole(0, 200)];
                case JsonValueKind.Array:
                    return Items().Select(i => i.Whole(0, 200)).ToList();
                default:
                    var text = Text().Replace(" ", "", StringComparison.Ordinal);
                    var plus = text == "index" ? 0
                        : text.StartsWith("index+", StringComparison.Ordinal) && int.TryParse(text[6..], System.Globalization.CultureInfo.InvariantCulture, out var n) ? n
                        : throw Error($"'{text}' should be a number, a list of numbers, or \"index\" / \"index+N\"");
                    return Enumerable.Range(plus, count).ToList();
            }
        }
    }
}
