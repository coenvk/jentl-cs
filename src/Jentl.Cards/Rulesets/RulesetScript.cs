using Jentl.Script;

namespace Jentl.Cards.Rulesets;

/// <summary>
/// A ruleset's script and what it may read and do. Reading works any time: <c>pile(name)</c>, <c>piles(group)</c>,
/// <c>hand(seat)</c>, <c>seat()</c>, <c>players()</c>, <c>direction()</c>, <c>suit()</c> and <c>counter(name)</c>.
/// Acting works only while a move is being played, and goes through the turn so undo takes it back:
/// <c>skip(n)</c>, <c>reverse()</c>, <c>again()</c>, <c>chooseSuit()</c>, <c>draw(seat, n)</c>,
/// <c>move(from, to, n)</c>, <c>flip(pile, up)</c>, <c>count(name, amount)</c> and <c>say(text)</c>. Cards are maps
/// (<c>rank</c>, <c>suit</c>, <c>label</c>, <c>order</c>, <c>points</c>, <c>colour</c>, <c>tags</c>, <c>code</c>,
/// <c>faceUp</c>), piles objects (<c>name</c>, <c>group</c>, <c>index</c>, <c>count</c>, <c>empty</c>, <c>top</c>,
/// <c>cards</c>).
/// </summary>
internal sealed class RulesetScript
{
    private const string CounterPrefix = "script:";

    private readonly Jentl.Script.Script _script;
    private RulesetGame? _game;
    private CardTurnBuilder? _turn;

    public RulesetScript(string source, string name, int seed)
    {
        Engine = new ScriptEngine(seed) { InstructionLimit = 1_000_000 };
        Define();
        _script = Engine.Load(source, name);
        _script.Run();
    }

    public ScriptEngine Engine { get; }

    // What the script asked for while a card was played, for the game to act on.
    public int Skip { get; private set; }

    public int Reverses { get; private set; }

    public bool Again { get; private set; }

    public bool Choose { get; private set; }

    public bool Has(string function) => _script[function].Type == ScriptType.Function;

    public void Attach(RulesetGame game) => _game = game;

    public void Begin(CardTurnBuilder turn)
    {
        _turn = turn;
        (Skip, Reverses, Again, Choose) = (0, 0, false, false);
    }

    public void End() => _turn = null;

    public ScriptValue Call(string function, params ScriptValue[] arguments) => _script.Call(function, arguments);

    /// <summary>A hook's answer, or null when the script has no such function or it returns nil.</summary>
    public ScriptValue? Hook(string function, params ScriptValue[] arguments) =>
        Has(function) && Call(function, arguments) is { IsNil: false } answer ? (ScriptValue?)answer : null;

    public static ScriptValue CardValue(Card card, bool? faceUp = null)
    {
        var face = card.Face;
        var map = new ScriptMap
        {
            ["rank"] = face.Rank,
            ["suit"] = face.Suit is null ? ScriptValue.Nil : face.Suit,
            ["label"] = face.Label,
            ["order"] = face.Order,
            ["points"] = face.Points,
            ["colour"] = face.Colour is null ? ScriptValue.Nil : face.Colour,
            ["tags"] = new ScriptList(face.Tags.Select(t => ScriptValue.FromText(t))),
            ["code"] = face.Code,
        };
        if (faceUp is { } up)
        {
            map["faceUp"] = up;
        }

        return map;
    }

    public ScriptValue PileValue(Pile pile) => ScriptValue.FromHost(new PileObject(Game, pile));

    private RulesetGame Game => _game ?? throw new ScriptException("the table isn't dealt yet; read it inside the rules' functions");

    private CardTurnBuilder Turn => _turn ?? throw new ScriptException("this only works while a move is played (in played, moved or an effect)");

    private Pile PileOf(ScriptCall call, int index) => call[index].Type switch
    {
        ScriptType.Text => Game.Table[call.Text(index)],
        ScriptType.Host when call[index].AsHost is PileObject pile => pile.Pile,
        _ => throw call.Error($"argument {index + 1} should be a pile or a pile's name, got {call[index]}"),
    };

    private void Define()
    {
        Engine.Define("seat", _ => Game.CurrentSeat);
        Engine.Define("players", _ => Game.Players);
        Engine.Define("direction", _ => Game.Direction);
        Engine.Define("suit", _ => Game.ChosenSuit is { } suit ? suit : ScriptValue.Nil);
        Engine.Define("counter", call => Game.Table.Counter(CounterPrefix + call.Text(0)));
        Engine.Define("pile", call => PileValue(PileOf(call, 0)));
        Engine.Define("hand", call => PileValue(Game.Hand(call.Whole(0))));
        Engine.Define("piles", call => new ScriptList(Game.Piles(call.Text(0)).Select(PileValue)));

        Engine.Define("skip", call =>
        {
            _ = Turn;
            Skip += call.Whole(0, 1);
        });
        Engine.Define("reverse", call =>
        {
            _ = Turn;
            Reverses++;
        });
        Engine.Define("again", call =>
        {
            _ = Turn;
            Again = true;
        });
        Engine.Define("chooseSuit", call =>
        {
            _ = Turn;
            Choose = true;
        });
        Engine.Define("draw", call => Game.DrawFor(Turn, call.Whole(0), call.Whole(1, 1)));
        Engine.Define("count", call => Turn.Count(CounterPrefix + call.Text(0), call.Whole(1, 1)));
        Engine.Define("say", call => Turn.Tag("say:" + call[0]));
        Engine.Define("move", call =>
        {
            var (from, to, count) = (PileOf(call, 0), PileOf(call, 1), call.Whole(2, 1));
            if (count < 1 || count > from.Count || from == to)
            {
                throw call.Error($"can't move {count} card(s) from {from.Name} ({from.Count}) to {to.Name}");
            }

            Turn.Move(from, from.Count - count, to);
        });
        Engine.Define("flip", call =>
        {
            var pile = PileOf(call, 0);
            if (pile.IsEmpty)
            {
                throw call.Error($"{pile.Name} is empty");
            }

            Turn.Flip(pile, pile.Count - 1, call[1].IsNil || call.IsTrue(1));
        });
    }

    /// <summary>A pile as scripts see it.</summary>
    private sealed class PileObject(RulesetGame game, Pile pile) : IScriptObject
    {
        public Pile Pile { get; } = pile;

        public ScriptValue GetMember(string name) => name switch
        {
            "name" => Pile.Name,
            "group" => game.RuleOf(Pile).Name,
            "index" => game.PlaceOf(Pile).Index,
            "count" => Pile.Count,
            "empty" => Pile.IsEmpty,
            "top" => Pile.Top is { } top ? CardValue(top.Card, top.FaceUp) : ScriptValue.Nil,
            "cards" => new ScriptList(Pile.Cards.Select(c => CardValue(c.Card, c.FaceUp))),
            _ => throw new ScriptException($"a pile has no field '{name}' (it has name, group, index, count, empty, top and cards)"),
        };

        public void SetMember(string name, ScriptValue value) => throw new ScriptException("a pile changes only through move, flip and draw");

        public override string ToString() => Pile.Name;
    }
}
