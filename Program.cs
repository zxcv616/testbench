using System.IO.Ports;

string portName = "/dev/cu.usbserial-110";

SerialPort port = new SerialPort(portName, 115200);
port.NewLine = "\n";
port.ReadTimeout = 1000; // ms

try
{
    port.Open();
    Thread.Sleep(500); // give the board a moment after opening
    port.DiscardInBuffer();

    port.WriteLine("PING");
    string reply = port.ReadLine().Trim();

    if (reply == "OK PONG")
        Console.WriteLine($"PASS  PING -> {reply}");
    else
        Console.WriteLine($"FAIL  PING -> unexpected reply: {reply}");
}
catch (TimeoutException)
{
    Console.WriteLine("FAIL  PING -> no reply (timeout)");
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR {ex.Message}");
}
finally
{
    if (port.IsOpen) port.Close();
}