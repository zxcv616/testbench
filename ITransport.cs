/// <summary>A connection that exchanges newline-terminated ASCII protocol messages.</summary>
internal interface ITransport : IDisposable
{
    string Endpoint { get; }

    // Prepare a clean session. Connection/startup time is outside test timing.
    void Connect();

    // Timeout budgets are positive milliseconds. Implementations throw TimeoutException
    // for timeouts and IOException for connection failures. Send adds the newline;
    // ReadLine removes the line ending but preserves other response whitespace.
    void Send(string command, int timeoutMs);
    string ReadLine(int timeoutMs);

    // Close releases the session and is safe to call again. Dispose releases all resources.
    void Close();
}
