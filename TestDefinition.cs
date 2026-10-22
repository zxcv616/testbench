using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

internal sealed class TestDefinition
{
    public string Name { get; set; } = "";
    public string? Command { get; set; }
    public string[]? Steps { get; set; }
    public string? Expect { get; set; }
    public string? ExpectPrefix { get; set; }
    public string? ExpectFormat { get; set; }
    public int MaxMs { get; set; }
    [JsonIgnore] public string[] Commands => Steps ?? [Command!];
    [JsonIgnore] public string Expected => Expect ?? (ExpectPrefix is not null
        ? $"prefix: {ExpectPrefix}" : $"format: {ExpectFormat}");

    public bool Accepts(string reply) => Expect is not null ? reply == Expect
        : ExpectPrefix is not null ? reply.StartsWith(ExpectPrefix, StringComparison.Ordinal)
        : ExpectFormat == "version" ? Regex.IsMatch(reply, @"\AOK (0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z")
        : reply.StartsWith("OK ", StringComparison.Ordinal)
          && uint.TryParse(reply.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out _);

    public static TestDefinition[] Load(string path)
    {
        var tests = JsonSerializer.Deserialize<TestDefinition[]>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        });
        if (tests is null || tests.Length == 0) throw new InvalidDataException("test list must not be empty");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var test in tests)
        {
            if (test is null || string.IsNullOrWhiteSpace(test.Name) || !names.Add(test.Name))
                throw new InvalidDataException("each test needs a unique, nonempty name");
            if (test.MaxMs <= 0) throw new InvalidDataException($"{test.Name}: maxMs must be positive");
            if ((test.Command is null) == (test.Steps is null) || test.Commands.Length == 0)
                throw new InvalidDataException($"{test.Name}: specify command OR nonempty steps");
            if ((test.Expect is not null ? 1 : 0) + (test.ExpectPrefix is not null ? 1 : 0)
                + (test.ExpectFormat is not null ? 1 : 0) != 1)
                throw new InvalidDataException($"{test.Name}: specify exactly one expectation");
            if (test.ExpectPrefix == "" || (test.ExpectFormat is not null && test.ExpectFormat is not ("version" or "uptime")))
                throw new InvalidDataException($"{test.Name}: invalid expectation");
            foreach (string command in test.Commands)
            {
                if (string.IsNullOrWhiteSpace(command) || command.Any(c => c < 32 || c > 126) || command.Length > 127)
                    throw new InvalidDataException($"{test.Name}: commands must be 1–127 printable ASCII characters");
                if (command.StartsWith("WAIT ", StringComparison.Ordinal) && !TryWait(command, out _))
                    throw new InvalidDataException($"{test.Name}: WAIT requires nonnegative integer milliseconds");
            }
            if (TryWait(test.Commands[^1], out _))
                throw new InvalidDataException($"{test.Name}: final step must be a device command");
        }
        return tests;
    }

    public static bool TryWait(string command, out int milliseconds)
    {
        milliseconds = 0;
        return command.StartsWith("WAIT ", StringComparison.Ordinal)
            && int.TryParse(command.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out milliseconds);
    }
}
