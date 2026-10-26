using System.Net;
using System.Net.Sockets;

/// <summary>
/// A TCP relay between the sensor and the broker. <see cref="Cut"/> is what a power cut looks
/// like from the broker's side: the connection stays open, but nothing crosses it again.
/// No DISCONNECT, no FIN, no RST, so the broker only finds out from the keep-alive.
/// </summary>
sealed class Cable : IAsyncDisposable
{
    readonly TcpListener _listener;
    readonly string _brokerHost;
    readonly int _brokerPort;
    readonly List<TcpClient> _wires = [];
    volatile bool _cut;

    Cable(int listenPort, string brokerHost, int brokerPort)
    {
        _listener = new TcpListener(IPAddress.Loopback, listenPort);
        _brokerHost = brokerHost;
        _brokerPort = brokerPort;
    }

    public bool IsCut => _cut;

    public static Cable PlugIn(int listenPort, string brokerHost, int brokerPort)
    {
        var cable = new Cable(listenPort, brokerHost, brokerPort);
        cable._listener.Start();
        _ = cable.AcceptAsync();
        return cable;
    }

    public void Cut() => _cut = true;

    async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient sensorSide;
            try { sensorSide = await _listener.AcceptTcpClientAsync(); }
            catch (Exception e) when (e is SocketException or ObjectDisposedException) { return; }

            var brokerSide = new TcpClient();
            await brokerSide.ConnectAsync(_brokerHost, _brokerPort);
            _wires.Add(sensorSide);
            _wires.Add(brokerSide);
            _ = PumpAsync(sensorSide, brokerSide);
            _ = PumpAsync(brokerSide, sensorSide);
        }
    }

    async Task PumpAsync(TcpClient from, TcpClient to)
    {
        var buffer = new byte[8192];
        try
        {
            int read;
            while ((read = await from.GetStream().ReadAsync(buffer)) > 0 && !_cut)
                await to.GetStream().WriteAsync(buffer.AsMemory(0, read));

            if (!_cut)
                to.Client.Shutdown(SocketShutdown.Send);   // a normal close still goes through
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { }
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        if (!_cut)                                         // after a cut, leave the wire hanging
            foreach (var wire in _wires) wire.Dispose();
        return ValueTask.CompletedTask;
    }
}
