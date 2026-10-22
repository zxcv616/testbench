internal static class TransportFactory
{
    public static ITransport Create(string endpoint) => endpoint == "sim" || endpoint.StartsWith("sim:", StringComparison.Ordinal)
        ? SimTransport.FromEndpoint(endpoint) : endpoint.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase)
        ? new TcpTransport(endpoint) : new SerialTransport(endpoint);
}
