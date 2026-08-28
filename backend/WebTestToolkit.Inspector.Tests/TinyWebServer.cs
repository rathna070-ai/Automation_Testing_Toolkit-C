using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WebTestToolkit.Inspector.Tests;

// A ~60-line HTTP server so the browser test runs against a real http:// origin.
//
// Why not HttpListener: it needs a URL ACL reservation on Windows for anything but an
// elevated process. Why not a data:/file:/ URL: both give the page an opaque origin, where
// sessionStorage throws — and the whole point of the test is proving that captures queued
// in sessionStorage survive a form submit.
internal sealed class TinyWebServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly IReadOnlyDictionary<string, string> _pages;
    private readonly CancellationTokenSource _cts = new();

    public string BaseUrl { get; }

    public TinyWebServer(IReadOnlyDictionary<string, string> pages)
    {
        _pages = pages;
        _listener = new TcpListener(IPAddress.Loopback, 0); // port 0 = let the OS pick a free one
        _listener.Start();

        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = $"http://127.0.0.1:{port}";

        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (Exception)
            {
                return; // Listener stopped; the test is over.
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);
                var path = ParsePath(request);

                var body = _pages.TryGetValue(path, out var page)
                    ? page
                    : "<!doctype html><title>Not found</title><h1>404</h1>";

                var bytes = Encoding.UTF8.GetBytes(body);
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    $"Content-Length: {bytes.Length}\r\n" +
                    "Cache-Control: no-store\r\n" +
                    "Connection: close\r\n\r\n");

                await stream.WriteAsync(header);
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }
            catch (Exception)
            {
                // A browser that hangs up mid-request is not a test failure.
            }
        }
    }

    private static async Task<string> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[4096];
        var text = new StringBuilder();

        // Headers only — none of these pages have a request body.
        while (!text.ToString().Contains("\r\n\r\n"))
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
                break;
            text.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        return text.ToString();
    }

    private static string ParsePath(string request)
    {
        var firstLine = request.Split("\r\n")[0];
        var parts = firstLine.Split(' ');
        if (parts.Length < 2)
            return "/";

        var target = parts[1];
        var query = target.IndexOf('?');
        return query >= 0 ? target[..query] : target;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}
