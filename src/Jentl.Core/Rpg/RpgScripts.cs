using Jentl.Rpg;
using Jentl.Rpg.Quests;
using Jentl.Rpg.Scripting;
using Jentl.Script;

namespace Jentl.Core.Rpg;

/// <summary>
/// Runs an RPG's content scripts with Jentl.Script: set it as <see cref="RpgState.Scripts"/>, and conditions and
/// effects written as <c>{"script": ...}</c> can use the state. Scripts get <c>flag(name)</c>, <c>setFlag(name)</c>,
/// <c>clearFlag(name)</c>, <c>var(name)</c>, <c>addVar(name, amount)</c>, <c>has(item, count)</c>,
/// <c>give(item, count)</c>, <c>take(item, count)</c>, <c>quest(id)</c> (<c>"notStarted"</c>, <c>"active"</c>,
/// <c>"ready"</c> or <c>"completed"</c>), <c>startQuest(id)</c>, <c>completeQuest(id)</c>, <c>heal(amount)</c> and
/// <c>say(text)</c>, besides the script library. Each piece of source is compiled once.
/// </summary>
public sealed class RpgScripts : IRpgScripts
{
    private readonly RpgState _state;
    private readonly Dictionary<string, Jentl.Script.Script> _statements = new(StringComparer.Ordinal);

    public RpgScripts(RpgState state, ScriptEngine? engine = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        Engine = engine ?? new ScriptEngine();
        Engine.Define("flag", call => _state.Flags.Contains(call.Text(0)));
        Engine.Define("setFlag", call => { _state.Flags.Add(call.Text(0)); });
        Engine.Define("clearFlag", call => { _state.Flags.Remove(call.Text(0)); });
        Engine.Define("var", call => (double)_state.Variable(call.Text(0)));
        Engine.Define("addVar", call => { _state.AddVariable(call.Text(0), call.Whole(1, 1)); });
        Engine.Define("has", call => _state.Inventory.Count(call.Text(0)) >= call.Whole(1, 1));
        Engine.Define("give", call => (double)_state.Give(call.Text(0), call.Whole(1, 1)));
        Engine.Define("take", call => _state.Take(call.Text(0), call.Whole(1, 1)));
        Engine.Define("startQuest", call => { _state.StartQuest(call.Text(0)); });
        Engine.Define("completeQuest", call => { _state.CompleteQuest(call.Text(0)); });
        Engine.Define("heal", call => { _state.Heal(call.Whole(0, 0)); });
        Engine.Define("say", call => { _state.Say(call[0].ToString()); });
        Engine.Define("quest", call =>
        {
            var quest = _state.Quests[call.Text(0)];
            return quest.Status switch
            {
                QuestStatus.NotStarted => "notStarted",
                QuestStatus.Active when quest.IsReady => "ready",
                QuestStatus.Active => "active",
                _ => "completed",
            };
        });
    }

    /// <summary>The engine scripts run in: define more functions on it for a game's own content.</summary>
    public ScriptEngine Engine { get; }

    public bool Test(string source) => Engine.Evaluate(source).IsTruthy;

    public void Run(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_statements.TryGetValue(source, out var script))
        {
            _statements[source] = script = Engine.Load(source, "content");
        }

        script.Run();
    }
}
