using Microsoft.VisualBasic.FileIO;

namespace TestBench.Tests;

public class SimulationTests
{
    [Theory]
    [InlineData("PING", "OK PONG")]
    [InlineData("VERSION", "OK 0.2.1")]
    [InlineData("ECHO hello", "OK hello")]
    [InlineData("ECHO ", "OK ")]
    [InlineData("ECHO a,b\"c", "OK a,b\"c")]
    [InlineData("FOO", "ERR unknown command")]
    [InlineData("CONTACT", "OK OPEN")]
    [InlineData("RSSI", "OK -40")]
    [InlineData("IP", "OK 192.0.2.1")]
    public void ProtocolReplies(string command, string expected) => Assert.Equal(expected, new DeviceSimulator().Execute(command));

    [Fact]
    public void UptimeWrapsAndRelayWatchdogExpires()
    {
        long now = 100;
        var device = new DeviceSimulator(clock: () => now);
        Assert.Equal("OK 0", device.Execute("UPTIME"));
        device.Execute("RELAY ON");
        now += 4999;
        Assert.Equal("OK CLOSED", device.Execute("CONTACT"));
        now++;
        Assert.Equal("OK OPEN", device.Execute("CONTACT"));
        now = 100 + (long)uint.MaxValue + 2;
        Assert.Equal("OK 1", device.Execute("UPTIME"));
    }

    [Fact]
    public void DisconnectDropsRelayAndClearsLateReply()
    {
        var device = new DeviceSimulator();
        using var transport = new SimTransport(device);
        transport.Connect();
        transport.Send("RELAY ON", 100);
        transport.Close();
        transport.Close();
        Assert.Equal("OK OPEN", device.Execute("CONTACT"));
        Assert.Throws<IOException>(() => transport.ReadLine(10));
        transport.Connect();
        transport.Send("PING", 100);
        Assert.Equal("OK PONG", transport.ReadLine(100));
    }

    [Theory]
    [InlineData("tests.json", 4)]
    [InlineData("tests-wifi.json", 6)]
    [InlineData("tests-relay.json", 8)]
    public void RealJsonSuitesRunUnchanged(string file, int count)
    {
        using var transport = new SimTransport();
        var (exit, rows, output) = Run(TestDefinition.Load(Path.Combine(AppContext.BaseDirectory, file)), transport);
        Assert.Equal(0, exit);
        Assert.Equal(count, rows.Length);
        Assert.All(rows, row => Assert.Equal("PASS", row[6]));
        Assert.Contains($"{count}/{count} passed", output);
    }

    [Theory]
    [InlineData("StuckOpen", "relay-closes", 7)]
    [InlineData("StuckClosed", "relay-starts-open", 6)]
    public void ContactFaultsFailDespiteSuccessfulAcknowledgments(string fault, string failed, int passed)
    {
        using var transport = new SimTransport(new DeviceSimulator(Enum.Parse<ContactFault>(fault)));
        var (exit, rows, output) = Run(TestDefinition.Load(Path.Combine(AppContext.BaseDirectory, "tests-relay.json")), transport);
        Assert.Equal(1, exit);
        Assert.Equal("FAIL", rows.Single(r => r[2] == failed)[6]);
        Assert.Equal("true", rows.Last()[7]); // Cleanup OFF test still executes.
        Assert.Contains($"{passed}/8 passed", output);
    }

    [Theory]
    [InlineData("Timeout", 1)]
    [InlineData("Disconnect", 1)]
    [InlineData("Missing", 0)]
    public void BrokenSessionDoesNotSendFurtherCommands(string fault, int sent)
    {
        using var transport = new SimTransport(fault: Enum.Parse<SimulationFault>(fault));
        var (exit, rows, _) = Run([Ping("first", 100), Ping("second", 100)], transport);
        Assert.Equal(1, exit);
        Assert.Equal(sent, transport.CommandsSent);
        Assert.Equal("false", rows[1][7]);
        Assert.Equal("", rows[1][8]);
        Assert.Contains("not run", rows[1][10]);
    }

    [Fact]
    public void GarbageRepliesFailWithoutCrashing()
    {
        using var transport = new SimTransport(fault: SimulationFault.Garbage);
        var (exit, rows, _) = Run([Ping("ping")], transport);
        Assert.Equal(1, exit);
        Assert.Equal("garbage", rows[0][9]);
        Assert.Contains("expected OK PONG", rows[0][10]);
    }

    [Fact]
    public void SlowRepliesExceedBudget()
    {
        using var transport = new SimTransport(replyDelayMs: 200);
        var (exit, rows, _) = Run([Ping("slow", 50)], transport);
        Assert.Equal(1, exit);
        Assert.Contains("exceeded maxMs", rows[0][10]);
    }

    [Fact]
    public void IntermediateErrorsStopSequenceButNextTestRuns()
    {
        using var transport = new SimTransport();
        var sequence = new TestDefinition { Name = "sequence", Steps = ["FOO", "RELAY ON"], Expect = "OK", MaxMs = 1000 };
        var (exit, rows, _) = Run([sequence, Ping("next")], transport);
        Assert.Equal(1, exit);
        Assert.Equal(2, transport.CommandsSent);
        Assert.Contains("step 'FOO'", rows[0][10]);
        Assert.Equal("PASS", rows[1][6]);
    }

    [Fact]
    public void WaitSharesWholeSequenceBudget()
    {
        using var transport = new SimTransport();
        var test = new TestDefinition { Name = "wait", Steps = ["WAIT 100", "PING"], Expect = "OK PONG", MaxMs = 50 };
        var (exit, _, _) = Run([test], transport);
        Assert.Equal(1, exit);
        Assert.Equal(0, transport.CommandsSent);
    }

    [Fact]
    public void CsvRoundTripsQuotesCommasAndNewlines()
    {
        using var transport = new SimTransport();
        var test = new TestDefinition { Name = "name,\"quoted\"\nnext", Command = "ECHO a,b\"c", Expect = "OK a,b\"c", MaxMs = 1000 };
        var (exit, rows, _) = Run([test], transport);
        Assert.Equal(0, exit);
        Assert.Equal(test.Name, rows[0][2]);
        Assert.Equal(test.Expect, rows[0][9]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{\"name\":\"a\",\"command\":\"PING\",\"expect\":\"OK\",\"maxMs\":0}]")]
    public void InvalidConfigurationRejected(string json)
    {
        string path = Path.GetTempFileName();
        try { File.WriteAllText(path, json); Assert.Throws<InvalidDataException>(() => TestDefinition.Load(path)); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("sim", 8)]
    [InlineData("sim:stuck-open", 7)]
    [InlineData("sim:stuck-closed", 6)]
    [InlineData("sim:garbage", 0)]
    [InlineData("sim:timeout", 0)]
    [InlineData("sim:disconnect", 0)]
    [InlineData("sim:missing", 0)]
    public void ScenarioSelectionProducesExpectedResults(string endpoint, int expectedPassed)
    {
        using var transport = SimTransport.FromEndpoint(endpoint);
        var (exit, rows, output) = Run(TestDefinition.Load(Path.Combine(AppContext.BaseDirectory, "tests-relay.json")), transport);
        Assert.Equal(expectedPassed == 8 ? 0 : 1, exit);
        Assert.Equal(expectedPassed, rows.Count(row => row[6] == "PASS"));
        Assert.All(rows, row => Assert.Equal(endpoint, row[1]));
        Assert.Contains($"{expectedPassed}/8 passed", output);
    }

    [Fact]
    public void UnknownScenarioRejected() => Assert.Throws<ArgumentException>(() => SimTransport.FromEndpoint("sim:typo"));

    [Fact]
    public void SimulatorRejectsInvalidFrames()
    {
        var device = new DeviceSimulator();
        Assert.StartsWith("ERR", device.Execute(new string('x', 128)));
        Assert.StartsWith("ERR", device.Execute("PING\nRELAY ON"));
    }

    private static TestDefinition Ping(string name, int maxMs = 1000) => new() { Name = name, Command = "PING", Expect = "OK PONG", MaxMs = maxMs };

    private static (int Exit, string[][] Rows, string Output) Run(TestDefinition[] tests, ITransport transport)
    {
        using var csv = new StringWriter();
        using var output = new StringWriter();
        int exit = new TestRunner().Run(tests, transport, csv, output);
        using var parser = new TextFieldParser(new StringReader(csv.ToString())) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData) rows.Add(parser.ReadFields()!);
        return (exit, rows.ToArray(), output.ToString());
    }
}
