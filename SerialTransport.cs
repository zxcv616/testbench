using System.IO.Ports;
using System.Text;

internal sealed class SerialTransport : ITransport
{
    private readonly SerialPort port;
    public string Endpoint => port.PortName;

    public SerialTransport(string portName)
    {
        port = new SerialPort(portName, 115200)
        {
            NewLine = "\n",
            Encoding = Encoding.ASCII,
            ReadTimeout = 200,
            WriteTimeout = 200
        };
    }

    public void Connect()
    {
        try
        {
            port.Open();
            Thread.Sleep(500); // Allow board startup before any timed commands.
            port.DiscardInBuffer();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            throw new IOException($"could not connect to {Endpoint}: {ex.Message}", ex);
        }
    }

    public void Send(string command, int timeoutMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        try
        {
            port.WriteTimeout = timeoutMs;
            port.WriteLine(command);
        }
        catch (InvalidOperationException ex)
        {
            throw new IOException("serial connection is closed", ex);
        }
    }

    public string ReadLine(int timeoutMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        try
        {
            port.ReadTimeout = timeoutMs;
            return port.ReadLine().TrimEnd('\r');
        }
        catch (InvalidOperationException ex)
        {
            throw new IOException("serial connection is closed", ex);
        }
    }

    public void Close() => port.Close();
    public void Dispose() => port.Dispose();
}
