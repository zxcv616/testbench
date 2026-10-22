using System.Globalization;

internal enum ContactFault { None, StuckOpen, StuckClosed }

/// <summary>Protocol/state model, not an electrical or Wi-Fi radio simulation.</summary>
internal sealed class DeviceSimulator
{
    private readonly Func<long> milliseconds;
    private readonly long started;
    private long relayStarted;
    private bool relayOn;
    public ContactFault ContactFault { get; }

    public DeviceSimulator(ContactFault contactFault = ContactFault.None, Func<long>? clock = null)
    {
        milliseconds = clock ?? (() => Environment.TickCount64);
        started = milliseconds();
        ContactFault = contactFault;
    }

    public string Execute(string command)
    {
        if (relayOn && milliseconds() - relayStarted >= 5000) relayOn = false;
        if (command.Length > 127 || command.Any(c => c < 32 || c > 126))
            return "ERR invalid or oversized command";
        switch (command)
        {
            case "PING": return "OK PONG";
            case "VERSION": return "OK 0.2.1";
            case "UPTIME": return "OK " + unchecked((uint)(milliseconds() - started)).ToString(CultureInfo.InvariantCulture);
            case "IP": return "OK 192.0.2.1"; // Documentation-only address; no network connection.
            case "RSSI": return "OK -40"; // Synthetic, not a measurement.
            case "RELAY ON": relayOn = true; relayStarted = milliseconds(); return "OK";
            case "RELAY OFF": relayOn = false; return "OK";
            case "CONTACT":
                bool closed = ContactFault == ContactFault.StuckClosed || (ContactFault != ContactFault.StuckOpen && relayOn);
                return closed ? "OK CLOSED" : "OK OPEN";
            default: return command.StartsWith("ECHO ", StringComparison.Ordinal) ? "OK " + command[5..] : "ERR unknown command";
        }
    }

    public void Disconnect() => relayOn = false;
}
