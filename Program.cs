using System.Text.Json;

if (args.Length > 3 || args.FirstOrDefault() is "--help" or "-h")
{
    Console.WriteLine("Usage: dotnet run -- [serial-port|tcp://host:port] [tests-file] [logs-directory]");
    return args.Length > 3 ? 2 : 0;
}

string portName = args.ElementAtOrDefault(0) ?? "/dev/cu.usbserial-110";
string testsPath = args.ElementAtOrDefault(1) ?? "tests.json";
string logsDirectory = args.ElementAtOrDefault(2) ?? "logs";
TestDefinition[] tests;
try { tests = TestDefinition.Load(testsPath); }
catch (Exception ex) when (ex is JsonException or InvalidDataException || IsDeviceError(ex))
{
    Console.WriteLine($"ERROR tests file '{testsPath}': {ex.Message}");
    return 2;
}

try
{
    Directory.CreateDirectory(logsDirectory);
    string logPath = Path.GetFullPath(Path.Combine(logsDirectory,
        $"run-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.csv"));
    using var log = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write)) { AutoFlush = true };
    log.WriteLine("timestampUtc,port,test,commands,expected,maxMs,status,executed,durationMs,response,reason");
    Console.WriteLine($"Loaded {tests.Length} tests from {testsPath}\nCSV log: {logPath}");

    using ITransport transport = portName.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase)
        ? new TcpTransport(portName) : new SerialTransport(portName);
    return new TestRunner().Run(tests, transport, log, Console.Out);
}
catch (Exception ex) when (IsDeviceError(ex))
{
    Console.WriteLine($"ERROR run could not complete: {ex.Message}");
    return 2;
}

static bool IsDeviceError(Exception ex) => ex is IOException or UnauthorizedAccessException
    or InvalidOperationException or ArgumentException;
