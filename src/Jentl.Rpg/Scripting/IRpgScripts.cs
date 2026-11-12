namespace Jentl.Rpg.Scripting;

/// <summary>
/// Runs the scripts written in content: conditions (<c>{"script": "flag(\"met\") and var(\"gold\") >= 5"}</c>) and
/// effects (<c>{"script": "give(\"potion\", 2)"}</c>). The RPG kit doesn't run scripts itself; Jentl.Core's
/// <c>RpgScripts</c> does it with Jentl.Script, or a game can bring its own.
/// </summary>
public interface IRpgScripts
{
    /// <summary>Whether the expression <paramref name="source"/> holds.</summary>
    bool Test(string source);

    /// <summary>Runs the statements <paramref name="source"/>.</summary>
    void Run(string source);
}
