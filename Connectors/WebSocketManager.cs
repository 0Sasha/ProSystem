using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProSystem;

internal class WebSocketManager : IDisposable
{
    private readonly Uri URL;
    private readonly AddInformation AddInfo;
    private readonly Action<string> DataHandler;

    private ClientWebSocket webSocket;
    private CancellationTokenSource tokenSource;

    public bool Connected { get => webSocket != null && webSocket.State == WebSocketState.Open; }

    public WebSocketManager(string url, Action<string> dataHandler, AddInformation addInfo)
    {
        URL = new(url);
        AddInfo = addInfo;
        DataHandler = dataHandler;
    }

    public async Task<bool> ConnectAsync()
    {
        if (!Connected)
        {
            tokenSource = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            webSocket = new();
            await webSocket.ConnectAsync(URL, tokenSource.Token);
            if (webSocket.State == WebSocketState.Open)
                _ = Task.Run(ReceiveAsync, tokenSource.Token);
        }
        return Connected;
    }

    public async Task DisconnectAsync()
    {
        tokenSource?.Cancel();
        tokenSource?.Dispose();
        tokenSource = null;
        if (webSocket == null) return;

        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            if (webSocket.State == WebSocketState.Open) await Task.Delay(250);
        }
        webSocket.Dispose();
        webSocket = null;
    }

    public async Task SendAsync(string message, CancellationToken? token = null)
    {
        var data = Encoding.ASCII.GetBytes(message);
        await webSocket.SendAsync(new(data), WebSocketMessageType.Text,
            true, token == null ? CancellationToken.None : token.Value);
    }

    private async Task ReceiveAsync()
    {
        string data = string.Empty;
        WebSocketReceiveResult result;
        while (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseSent)
        {
            try
            {
                while (tokenSource != null && !tokenSource.Token.IsCancellationRequested)
                {
                    var buffer = new ArraySegment<byte>(new byte[8192]);
                    result = await webSocket.ReceiveAsync(buffer, tokenSource.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;

                    data = Encoding.UTF8.GetString(buffer.ToArray(), buffer.Offset, buffer.Count);
                    DataHandler(data);
                }
                return;
            }
            catch (TaskCanceledException)
            {
                await DisconnectAsync();
                return;
            }
            catch (Exception ex)
            {
                AddInfo("WebSocketManager: " + ex.Message, notify: true);
                AddInfo("StackTrace: " + ex.StackTrace);
                AddInfo("Data: " + data, false);
                if (ex.InnerException != null)
                {
                    AddInfo("Inner: " + ex.InnerException.Message, false);
                    AddInfo("Inner stackTrace: " + ex.InnerException.StackTrace, false);
                }
            }
        }
    }

    public void Dispose() => DisconnectAsync().Wait();
}
