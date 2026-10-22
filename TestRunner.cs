using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

internal sealed class TestRunner
{
    // The caller owns and disposes the transport and writers, including on failures.
    public int Run(TestDefinition[] tests, ITransport transport, TextWriter log, TextWriter output, Action<TestObservation>? observe = null)
    {
        int passed = 0;
        string? unavailable = null;
        try
        {
            transport.Connect();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            unavailable = $"device unavailable at {transport.Endpoint}: {ex.Message}";
        }

        foreach (var test in tests)
        {
            var timestamp = DateTimeOffset.UtcNow;
            bool executed = unavailable is null;
            string response = "";
            string reason = unavailable is null ? "" : $"{unavailable} (not run)";
            var timer = new Stopwatch();
            if (executed)
            {
                timer.Start();
                try
                {
                    for (int step = 0; step < test.Commands.Length; step++)
                    {
                        string command = test.Commands[step];
                        if (TestDefinition.TryWait(command, out int waitMs))
                        {
                            if (waitMs >= RemainingMs()) throw new TimeoutException();
                            Thread.Sleep(waitMs);
                            continue;
                        }
                        transport.Send(command, RemainingMs());
                        response = transport.ReadLine(RemainingMs());
                        if (timer.Elapsed.TotalMilliseconds > test.MaxMs) throw new TimeoutException();
                        if (step < test.Commands.Length - 1 && response != "OK" && !response.StartsWith("OK ", StringComparison.Ordinal))
                        {
                            reason = $"step '{command}': expected OK acknowledgment, got {response}";
                            break;
                        }
                    }
                    if (reason == "" && !test.Accepts(response))
                        reason = $"expected {test.Expected}, got {response}";
                }
                catch (TimeoutException)
                {
                    reason = $"exceeded maxMs ({test.MaxMs} ms); command/reply timeout";
                    // A late reply must never be mistaken for the next command's reply.
                    unavailable = "session stopped after timeout; reconnect before retrying";
                }
                catch (IOException ex)
                {
                    reason = $"device disconnected or I/O failure: {ex.Message}";
                    unavailable = "session stopped after device failure";
                }
                finally { timer.Stop(); }
            }
            bool success = reason.Length == 0;
            if (success) passed++;
            string status = success ? "PASS" : "FAIL";
            string duration = executed ? timer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) : "";
            log.WriteLine(string.Join(",", new[] { timestamp.ToString("O"), transport.Endpoint, test.Name,
                JsonSerializer.Serialize(test.Commands), test.Expected, test.MaxMs.ToString(CultureInfo.InvariantCulture),
                status, executed ? "true" : "false", duration, response, reason }.Select(Csv)));
            output.WriteLine($"{status}  {test.Name}{(executed ? $" ({duration} ms)" : "")}: {(success ? response : reason)}");

            observe?.Invoke(new TestObservation(success, executed, timer.Elapsed.TotalMilliseconds));

            int RemainingMs()
            {
                int remaining = test.MaxMs - (int)Math.Ceiling(timer.Elapsed.TotalMilliseconds);
                return remaining > 0 ? remaining : throw new TimeoutException();
            }
        }
        output.WriteLine($"\n{passed}/{tests.Length} passed");
        return passed == tests.Length ? 0 : 1;
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}

internal sealed record TestObservation(bool Passed, bool Executed, double DurationMs);
