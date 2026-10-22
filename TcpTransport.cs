using System.Net.Sockets;
using System.Text;

internal sealed class TcpTransport : ITransport
{
    private readonly Uri address;
    private TcpClient? client;
    private NetworkStream? stream;
    public string Endpoint => address.GetLeftPart(UriPartial.Authority);

    public TcpTransport(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
            || parsed.Scheme != "tcp" || string.IsNullOrEmpty(parsed.Host)
            || parsed.Port is < 1 or > 65535 || parsed.UserInfo != ""
            || parsed.AbsolutePath is not ("" or "/") || parsed.Query != "" || parsed.Fragment != "")
            throw new ArgumentException("TCP endpoint must be tcp://host:port");
        address = parsed;
    }

    public void Connect()
    {
        Close();
        client = new TcpClient { NoDelay = true };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            client.ConnectAsync(address.DnsSafeHost, address.Port, deadline.Token).AsTask().GetAwaiter().GetResult();
            stream = client.GetStream();
        }
        catch (OperationCanceledException ex) { Close(); throw new TimeoutException("TCP connection exceeded 5 seconds", ex); }
        catch (SocketException ex) { Close(); throw new IOException($"TCP connection failed: {ex.Message}", ex); }
    }

    public void Send(string command, int timeoutMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        using var deadline = new CancellationTokenSource(timeoutMs);
        try
        {
            var connection = stream ?? throw new IOException("TCP connection is closed");
            connection.WriteAsync(Encoding.ASCII.GetBytes(command + "\n").AsMemory(), deadline.Token)
                .AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException ex) { throw new TimeoutException("TCP send timed out", ex); }
    }

    public string ReadLine(int timeoutMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        // One deadline covers the entire line, including slowly arriving fragments.
        using var deadline = new CancellationTokenSource(timeoutMs);
        var reply = new StringBuilder();
        byte[] next = new byte[1];
        try
        {
            var connection = stream ?? throw new IOException("TCP connection is closed");
            while (true)
            {
                int count = connection.ReadAsync(next.AsMemory(), deadline.Token).AsTask().GetAwaiter().GetResult();
                if (count == 0) throw new IOException("TCP peer disconnected before a complete reply");
                if (next[0] == '\n') return reply.ToString().TrimEnd('\r');
                if (next[0] > 127 || (next[0] < 32 && next[0] != '\r'))
                    throw new IOException("unexpected non-ASCII protocol reply");
                if (reply.Length >= 1024) throw new IOException("unexpected reply: line exceeds 1024 bytes");
                reply.Append((char)next[0]);
            }
        }
        catch (OperationCanceledException ex) { throw new TimeoutException("TCP reply timed out", ex); }
    }

    public void Close()
    {
        stream?.Dispose();
        stream = null;
        client?.Dispose();
        client = null;
    }
    public void Dispose() => Close();
}
