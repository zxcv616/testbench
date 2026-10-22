using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

internal sealed record SoakStatistics(int Requested, int Attempted, int Passed, int Failed, int NotRun,
    double? FailureRatePercent, double? MinMs, double? MeanMs, double? P95Ms, double? MaxMs)
{
    public static SoakStatistics From(IReadOnlyList<TestObservation> observations)
    {
        var times = observations.Where(o => o.Executed && o.Passed).Select(o => o.DurationMs).Order().ToArray();
        int attempted = observations.Count(o => o.Executed);
        int failed = attempted - times.Length;
        return new(observations.Count, attempted, times.Length, failed, observations.Count - attempted,
            attempted == 0 ? null : 100.0 * failed / attempted,
            times.Length == 0 ? null : times[0], times.Length == 0 ? null : times.Average(),
            times.Length == 0 ? null : times[(int)Math.Ceiling(times.Length * 0.95) - 1],
            times.Length == 0 ? null : times[^1]);
    }
}

internal static class SoakCommand
{
    public static int Run(string[] args)
    {
        if (args.Length is < 1 or > 4 || !TryNumber(args.ElementAtOrDefault(1), 1000, 100000, out int count)
            || !TryNumber(args.ElementAtOrDefault(2), 500, 60000, out int maxMs))
        {
            Console.WriteLine("Usage: --soak <endpoint> [count=1000, 1..100000] [maxMs=500, 1..60000] [logs-directory=logs]");
            return 2;
        }
        string endpoint = args[0];
        try
        {
            string directory = args.ElementAtOrDefault(3) ?? "logs";
            Directory.CreateDirectory(directory);
            string stem = Path.GetFullPath(Path.Combine(directory, $"soak-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}"));
            using var csv = new StreamWriter(new FileStream(stem + ".csv", FileMode.CreateNew, FileAccess.Write)) { AutoFlush = true };
            csv.WriteLine("timestampUtc,port,test,commands,expected,maxMs,status,executed,durationMs,response,reason");
            using var transport = TransportFactory.Create(endpoint);
            bool simulated = transport is SimTransport;
            Console.WriteLine($"{(simulated ? "SIMULATED" : "DEVICE")} soak: {count} sequential PINGs, {maxMs} ms limit, {endpoint}");
            Console.WriteLine($"CSV log: {stem}.csv");
            var tests = Enumerable.Range(1, count).Select(i => new TestDefinition
            { Name = $"ping-{i:D6}", Command = "PING", Expect = "OK PONG", MaxMs = maxMs }).ToArray();
            var observations = new List<TestObservation>(count);
            var started = DateTimeOffset.UtcNow;
            var total = Stopwatch.StartNew();
            int exit = new TestRunner().Run(tests, transport, csv, TextWriter.Null, observation =>
            {
                observations.Add(observation);
                if (observations.Count % 100 == 0 || observations.Count == count)
                    Console.WriteLine($"Processed {observations.Count}/{count}");
            });
            total.Stop();
            var statistics = SoakStatistics.From(observations);
            var summary = new
            {
                StartedUtc = started, Endpoint = endpoint, Simulated = simulated, MaxMs = maxMs,
                WallSeconds = total.Elapsed.TotalSeconds, Completed = statistics.NotRun == 0,
                Statistics = statistics,
                Method = "One connection; sequential PINGs; no retries or pacing. Latencies include send/read and host processing, exclude connection setup and CSV writes, and use successful replies only. P95 uses nearest rank. Failure rate uses attempted tests only; not-run tests are reported separately."
            };
            File.WriteAllText(stem + ".json", JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{statistics.Passed}/{count} passed; {statistics.Failed} failed; {statistics.NotRun} not run");
            Console.WriteLine($"Failure rate (attempted): {Format(statistics.FailureRatePercent)}%");
            Console.WriteLine($"Successful reply latency (ms): min {Format(statistics.MinMs)}, mean {Format(statistics.MeanMs)}, p95 {Format(statistics.P95Ms)}, max {Format(statistics.MaxMs)}");
            Console.WriteLine($"Summary: {stem}.json");
            if (statistics.NotRun > 0) Console.WriteLine("INCOMPLETE: session stopped; inspect the CSV failure reason before rerunning.");
            return exit;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Console.WriteLine($"ERROR soak could not complete: {ex.Message}");
            return 2;
        }
    }

    private static bool TryNumber(string? text, int fallback, int maximum, out int value)
    {
        value = fallback;
        return (text is null || int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)) && value >= 1 && value <= maximum;
    }
    private static string Format(double? value) => value?.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a";
}
