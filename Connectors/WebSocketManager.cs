using System.ComponentModel;
using System.Net.WebSockets;
using System.Text;

namespace ProSystem;

internal class WebSocketManager : IDisposable, INotifyPropertyChanged
{
    private readonly string URL;
    private readonly AddInformation AddInfo;
    private readonly Action<string> HandleData;

    private ClientWebSocket? webSocket;
    private CancellationTokenSource? tokenSource;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Connected { get => webSocket?.State == WebSocketState.Open; }

    public WebSocketManager(string url, Action<string> handleData, AddInformation addInfo)
    {
        ArgumentException.ThrowIfNullOrEmpty(url, nameof(url));
        URL = url;
        AddInfo = addInfo;
        HandleData = handleData;
    }

    public async Task<bool> ConnectAsync(string relativeURL)
    {
        if (!Connected)
        {
            tokenSource = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            webSocket = new();
            webSocket.Options.KeepAliveInterval = TimeSpan.FromHours(24);
            await webSocket.ConnectAsync(new(URL + relativeURL), tokenSource.Token);
            if (!Connected) await Task.Delay(250);

            _ = Task.Run(ReceiveAsync, tokenSource.Token);
            PropertyChanged?.Invoke(this, new(nameof(Connected)));
        }
        return Connected;
    }

    public async Task DisconnectAsync()
    {
        tokenSource?.Cancel();
        tokenSource?.Dispose();
        tokenSource = null;
        if (webSocket == null) return;

        if (Connected)
        {
            await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            if (Connected) await Task.Delay(250);
        }
        webSocket.Dispose();
        webSocket = null;
        PropertyChanged?.Invoke(this, new(nameof(Connected)));
    }

    public async Task SendAsync(string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(message, nameof(message));
        ArgumentNullException.ThrowIfNull(webSocket, nameof(webSocket));

        var data = Encoding.ASCII.GetBytes(message);
        await webSocket.SendAsync(new(data), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task ReceiveAsync()
    {
        try
        {
            while (webSocket != null && tokenSource != null && !tokenSource.IsCancellationRequested &&
                (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseSent))
            {
                var buffer = new ArraySegment<byte>(new byte[8192]);
                var result = await webSocket.ReceiveAsync(buffer, tokenSource.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var data = Encoding.UTF8.GetString([.. buffer], buffer.Offset, buffer.Count);
                HandleData(data);
            }
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            AddInfo("WebSocketManager: " + ex.Message, true, !ex.Message.StartsWith("The remote party closed"));
            AddInfo("StackTrace: " + ex.StackTrace, false);
            if (ex.InnerException != null)
            {
                AddInfo("Inner: " + ex.InnerException.Message, false);
                AddInfo("Inner stackTrace: " + ex.InnerException.StackTrace, false);
            }
        }
        finally { await DisconnectAsync(); }
    }

    public void Dispose() => DisconnectAsync().Wait();
}
