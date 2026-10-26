// Modes: echo, slow:<ms>, crash, deaf, silent, chatty, flood, count (answers "<n>:<line>", n counting the lines this
// process read, so a test can tell a process that kept running from a fresh one).
var mode = args.Length > 0 ? args[0] : "echo";
var delay = 0;
if (mode.StartsWith("slow:", StringComparison.Ordinal))
{
    delay = int.Parse(mode["slow:".Length..], System.Globalization.CultureInfo.InvariantCulture);
    mode = "slow";
}

switch (mode)
{
    case "crash":
        Console.In.ReadLine();
        Console.Error.WriteLine("boom");
        return 3;
    case "deaf":
        Thread.Sleep(Timeout.Infinite);
        return 0;
    case "silent":
        while (Console.In.ReadLine() is not null)
        {
        }

        return 0;
    case "flood":
        // Floods stdout after the first line (many lines, then one far too long), says so on stderr, then echoes.
        Console.In.ReadLine();
        var lineOfFlood = new string('x', 1024);
        for (var i = 0; i < 20_000; i++)
        {
            Console.Out.WriteLine(lineOfFlood);
        }

        Console.Out.WriteLine(new string('y', 3 << 20));
        Console.Out.Flush();
        Console.Error.WriteLine("flooded");
        while (Console.In.ReadLine() is { } echoed)
        {
            Console.Out.WriteLine(echoed);
        }

        return 0;
    case "count":
        var count = 0;
        while (Console.In.ReadLine() is { } counted)
        {
            Console.Out.WriteLine($"{++count}:{counted}");
        }

        return 0;
    case "echo":
    case "slow":
    case "chatty":
        while (Console.In.ReadLine() is { } line)
        {
            if (delay > 0)
            {
                Thread.Sleep(delay);
            }

            if (mode == "chatty")
            {
                Console.Out.WriteLine("hello world");
            }

            Console.Out.WriteLine(line);
        }

        return 0;
    default:
        Console.Error.WriteLine($"unknown mode {mode}");
        return 2;
}
