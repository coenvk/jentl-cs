using System.Text;

namespace Fluffy;

public sealed partial class StateMachine<TState, TTrigger>
{
    /// <summary>
    /// Finds likely mistakes in the configuration: states that can never be reached, and initial transitions to
    /// states that are not substates. States reached only through <c>PermitDynamic</c> cannot be checked and count as
    /// reachable when any dynamic transition exists.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        foreach (var config in _states.Values)
        {
            if (config.HasInitial && !Lineage(config.Initial!).Skip(1).Any(s => Same.Equals(s, config.State)))
            {
                problems.Add($"The initial transition of {config.State} goes to {config.Initial}, which is not one of its substates.");
            }
        }

        var hasDynamic = _states.Values.SelectMany(c => c.Handlers.Values.SelectMany(h => h)).Any(h => h.IsDynamic);
        if (!hasDynamic)
        {
            var reachable = Reachable();
            problems.AddRange(_states.Keys.Where(s => !reachable.Contains(s)).Select(s => $"{s} can never be reached."));
        }

        return problems;
    }

    /// <summary>The configuration as a Mermaid state diagram (renders in GitHub markdown inside a <c>mermaid</c> code block).</summary>
    public string ToMermaid()
    {
        var text = new StringBuilder("stateDiagram-v2\n");
        var ids = Ids();
        void Write(bool top, TState parent, string indent)
        {
            foreach (var config in _states.Values.Where(c => top ? !c.HasSuperstate : c.HasSuperstate && Same.Equals(c.Superstate, parent)))
            {
                var id = ids[config.State];
                var children = _states.Values.Any(c => c.HasSuperstate && Same.Equals(c.Superstate, config.State));
                if (children)
                {
                    text.Append(indent).Append("state ").Append(id).AppendLine(" {");
                    if (config.HasInitial)
                    {
                        text.Append(indent).Append("    [*] --> ").AppendLine(ids[config.Initial!]);
                    }

                    Write(false, config.State, indent + "    ");
                    text.Append(indent).AppendLine("}");
                }
                else if (id != config.State.ToString())
                {
                    text.Append(indent).Append(id).Append(" : ").AppendLine(config.State.ToString());
                }
                else if (!top)
                {
                    // Substates must be listed inside their superstate's block, or Mermaid draws them outside it.
                    text.Append(indent).AppendLine(id);
                }
            }
        }

        text.Append("    [*] --> ").AppendLine(ids[Initial]);
        Write(true, Initial, "    ");
        foreach (var (from, to, label) in Edges())
        {
            text.Append("    ").Append(ids[from]).Append(" --> ").Append(ids[to]).Append(" : ").AppendLine(label);
        }

        return text.ToString();
    }

    /// <summary>The configuration in Graphviz DOT, with superstates as clusters.</summary>
    public string ToDot()
    {
        var text = new StringBuilder("digraph {\n    compound=true;\n    node [shape=box, style=rounded];\n");
        var ids = Ids();
        void Write(bool top, TState parent, string indent)
        {
            foreach (var config in _states.Values.Where(c => top ? !c.HasSuperstate : c.HasSuperstate && Same.Equals(c.Superstate, parent)))
            {
                if (_states.Values.Any(c => c.HasSuperstate && Same.Equals(c.Superstate, config.State)))
                {
                    text.Append(indent).Append("subgraph cluster_").Append(ids[config.State]).AppendLine(" {");
                    text.Append(indent).Append("    label=\"").Append(Escape(config.State.ToString() ?? "")).AppendLine("\";");
                    text.Append(indent).Append("    ").Append(ids[config.State]).AppendLine(" [shape=point, style=invis];");
                    Write(false, config.State, indent + "    ");
                    text.Append(indent).AppendLine("}");
                }
                else
                {
                    text.Append(indent).Append(ids[config.State]).Append(" [label=\"").Append(Escape(config.State.ToString() ?? "")).AppendLine("\"];");
                }
            }
        }

        text.AppendLine("    start [shape=point];");
        Write(true, Initial, "    ");
        text.Append("    start -> ").Append(ids[Initial]).AppendLine(";");
        foreach (var (from, to, label) in Edges())
        {
            text.Append("    ").Append(ids[from]).Append(" -> ").Append(ids[to]).Append(" [label=\"").Append(Escape(label)).AppendLine("\"];");
        }

        return text.Append("}\n").ToString();
    }

    /// <summary>Every fixed transition (triggers and timers) with a label such as "Poke [hungry]" or "after 5s".</summary>
    private IEnumerable<(TState From, TState To, string Label)> Edges()
    {
        foreach (var config in _states.Values)
        {
            foreach (var (trigger, handlers) in config.Handlers)
            {
                foreach (var handler in handlers.Where(h => h.Kind == StateConfiguration<TState, TTrigger>.HandlerKind.Move && !h.IsDynamic))
                {
                    yield return (config.State, handler.Target!, Label(trigger.ToString() ?? "", handler.Description, handler.Guard is not null));
                }
            }

            foreach (var timer in config.Timers)
            {
                yield return (config.State, timer.Destination, Label($"after {timer.Delay.TotalSeconds:0.##}s", timer.Description, timer.Guard is not null));
            }
        }
    }

    private static string Label(string what, string? description, bool guarded) =>
        guarded ? $"{what} [{description ?? "guard"}]" : what;

    private HashSet<TState> Reachable()
    {
        var reached = new HashSet<TState>(Same);
        var frontier = new Queue<TState>();
        void Reach(TState state)
        {
            // Being in a state means being in its superstates too.
            foreach (var s in Lineage(state))
            {
                if (reached.Add(s))
                {
                    frontier.Enqueue(s);
                }
            }
        }

        Reach(Initial);
        while (frontier.TryDequeue(out var state))
        {
            if (Config(state) is not { } config)
            {
                continue;
            }

            if (config.HasInitial)
            {
                Reach(config.Initial!);
            }

            foreach (var target in config.Handlers.Values.SelectMany(h => h).Where(h => h.Target is not null).Select(h => h.Target!))
            {
                Reach(target);
            }

            foreach (var timer in config.Timers)
            {
                Reach(timer.Destination);
            }

            // Transitions defined on a superstate apply in its substates, but those are reached only by entering them.
        }

        return reached;
    }

    /// <summary>Identifiers safe for diagrams: the state's name if it is plain, otherwise s1, s2...</summary>
    private Dictionary<TState, string> Ids()
    {
        var ids = new Dictionary<TState, string>(Same);
        var n = 0;
        foreach (var state in _states.Keys.Append(Initial))
        {
            if (!ids.ContainsKey(state))
            {
                var name = state.ToString() ?? "";
                ids[state] = name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && char.IsAsciiLetter(name[0]) && !ids.ContainsValue(name)
                    ? name
                    : $"s{++n}";
            }
        }

        return ids;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
