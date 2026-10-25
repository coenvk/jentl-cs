// Modes: echo, slow:<ms>, crash, silent, chatty.
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
