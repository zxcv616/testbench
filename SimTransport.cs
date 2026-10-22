
internal enum SimulationFault { None, Garbage, Timeout, Disconnect, Missing }

internal sealed class SimTransport : ITransport
{
    private readonly DeviceSimulator device;
    private readonly SimulationFault fault;
    private readonly int replyDelayMs;
    private bool connected;
    private string? pending;
    private long replyAt;
    public string Endpoint { get; }
    public int CommandsSent { get; private set; }

    public SimTransport(DeviceSimulator? device = null, SimulationFault fault = SimulationFault.None,
        int replyDelayMs = 0, string endpoint = "sim")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(replyDelayMs);
        this.device = device ?? new DeviceSimulator();
        this.fault = fault;
        this.replyDelayMs = replyDelayMs;
        Endpoint = endpoint;
    }

    public static SimTransport FromEndpoint(string endpoint) => endpoint switch
    {
        "sim" => new(),
        "sim:stuck-open" => new(new DeviceSimulator(ContactFault.StuckOpen), endpoint: endpoint),
        "sim:stuck-closed" => new(new DeviceSimulator(ContactFault.StuckClosed), endpoint: endpoint),
        "sim:garbage" => new(fault: SimulationFault.Garbage, endpoint: endpoint),
        "sim:timeout" => new(fault: SimulationFault.Timeout, endpoint: endpoint),
        "sim:disconnect" => new(fault: SimulationFault.Disconnect, endpoint: endpoint),
        "sim:missing" => new(fault: SimulationFault.Missing, endpoint: endpoint),
        _ => throw new ArgumentException("Unknown simulator scenario. Use sim, sim:stuck-open, sim:stuck-closed, sim:garbage, sim:timeout, sim:disconnect, or sim:missing.")
    };

    public void Connect()
    {
        Close();
        if (fault == SimulationFault.Missing) throw new IOException("simulated device not found");
        connected = true;
        CommandsSent = 0;
    }

    public void Send(string command, int timeoutMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        if (!connected) throw new IOException("simulated connection is closed");
        if (pending is not null) throw new IOException("previous simulated reply has not been consumed");
        CommandsSent++;
        if (fault == SimulationFault.Disconnect) { Close(); throw new IOException("simulated disconnect during send"); }
        string reply = device.Execute(command);
        pending = fault == SimulationFault.Garbage ? "garbage" : reply;
        replyAt = Environment.TickCount64 + replyDelayMs;
    }

    public string ReadLine(int timeoutMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        if (!connected) throw new IOException("simulated connection is closed");
        long wait = Math.Max(0, replyAt - Environment.TickCount64);
        if (pending is null || fault == SimulationFault.Timeout || wait >= timeoutMs)
        {
            Thread.Sleep(timeoutMs);
            throw new TimeoutException("simulated reply timeout");
        }
        if (wait > 0) Thread.Sleep((int)wait);
        string result = pending;
        pending = null;
        return result;
    }

    public void Close() { connected = false; pending = null; device.Disconnect(); }
    public void Dispose() => Close();
}
