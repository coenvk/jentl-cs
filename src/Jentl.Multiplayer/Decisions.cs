namespace Jentl.Multiplayer;

/// <summary>Why a decision was not the agent's own answer.</summary>
public enum Fault
{
    /// <summary>Nothing went wrong; the move is the agent's answer.</summary>
    None,

    /// <summary>There was no answer within the time limit.</summary>
    Late,

    /// <summary>The agent answered with nothing, or with a move that is not allowed.</summary>
    Illegal,

    /// <summary>The agent is no longer there: asking it failed, or it was already known to be gone.</summary>
    Gone,
}

/// <summary>A move to play and whether it is the agent's own answer or a replacement for a <see cref="Fault"/>.</summary>
public readonly record struct Decided<TMove>(TMove Move, Fault Fault);

/// <summary>Asks an agent, such as a bot in another process, for a move without ever waiting longer than a limit.</summary>
public static class Decisions
{
    /// <summary>
    /// Asks for a move and returns the answer when it is legal and in time. Otherwise returns what <paramref name="fallback"/>
    /// gives, with the reason: <see cref="Fault.Late"/> when the limit passed (the token given to <paramref name="ask"/> is
    /// then cancelled), <see cref="Fault.Illegal"/> for a null or not-allowed answer, <see cref="Fault.Gone"/> when asking
    /// throws or <paramref name="isGone"/> says the agent is gone (then nobody is asked).
    /// </summary>
    public static async Task<Decided<TMove>> DecideAsync<TMove>(
        Func<CancellationToken, Task<TMove?>> ask,
        TimeSpan limit,
        Func<TMove, bool> isLegal,
        Func<TMove> fallback,
        Func<bool>? isGone = null)
        where TMove : class
    {
        if (isGone?.Invoke() == true)
        {
            return new Decided<TMove>(fallback(), Fault.Gone);
        }

        using var asking = new CancellationTokenSource();
        using var timer = new CancellationTokenSource();
        Task<TMove?> answer;
        try
        {
            answer = ask(asking.Token);
        }
        catch (Exception)
        {
            return new Decided<TMove>(fallback(), Fault.Gone);
        }

        var finished = await Task.WhenAny(answer, Task.Delay(limit, timer.Token));
        if (finished != answer)
        {
            await asking.CancelAsync();

            // An ask that ignores the cancellation may still fail later; look at that so it is not left unobserved.
            _ = answer.ContinueWith(
                late => late.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return new Decided<TMove>(fallback(), Fault.Late);
        }

        await timer.CancelAsync();
        TMove? move;
        try
        {
            move = await answer;
        }
        catch (Exception)
        {
            return new Decided<TMove>(fallback(), Fault.Gone);
        }

        return move is not null && isLegal(move)
            ? new Decided<TMove>(move, Fault.None)
            : new Decided<TMove>(fallback(), Fault.Illegal);
    }
}
