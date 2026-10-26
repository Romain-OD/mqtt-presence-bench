using System.Globalization;

sealed record Options(
    string Mode,
    string Broker,
    bool Will,
    uint WillDelay,
    uint SessionExpiry,
    string? Then,
    TimeSpan After,
    TimeSpan? Duration)
{
    public static Options Parse(string[] args)
    {
        string? Get(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static TimeSpan Seconds(string value) =>
            TimeSpan.FromSeconds(double.Parse(value, CultureInfo.InvariantCulture));

        return new Options(
            Mode: args.FirstOrDefault() ?? "agent",
            Broker: Get("--broker") ?? "localhost",
            Will: !args.Contains("--no-will"),
            WillDelay: uint.Parse(Get("--will-delay") ?? "0"),
            SessionExpiry: uint.Parse(Get("--session-expiry") ?? "0"),
            Then: Get("--then"),
            After: Seconds(Get("--after") ?? "5"),
            Duration: Get("--for") is { } d ? Seconds(d) : null);
    }
}
