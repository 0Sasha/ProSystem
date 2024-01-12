﻿using System.ComponentModel;
using System.Net.WebSockets;
using System.Text;

namespace ProSystem;

internal class WebSocketManager : IDisposable, INotifyPropertyChanged
{
    private readonly string URL;
    private readonly AddInformation AddInfo;
    private readonly Action<string> DataHandler;

    private ClientWebSocket webSocket;
    private CancellationTokenSource tokenSource;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Connected { get => webSocket != null && webSocket.State == WebSocketState.Open; }

    public WebSocketManager(string url, Action<string> dataHandler, AddInformation addInfo)
    {
        if (url == null || url == string.Empty) throw new ArgumentNullException(nameof(url));
        URL = url;
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
        DataHandler = dataHandler ?? throw new ArgumentNullException(nameof(dataHandler));
    }

    public async Task<bool> ConnectAsync(string relativeURL)
    {
        if (!Connected)
        {
            tokenSource = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            webSocket = new();
            await webSocket.ConnectAsync(new(URL + relativeURL), tokenSource.Token);
            if (webSocket.State != WebSocketState.Open) await Task.Delay(250);

            _ = Task.Run(ReceiveAsync, tokenSource.Token);
            PropertyChanged.Invoke(this, new(nameof(Connected)));
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
        if (message == null || message == string.Empty) throw new ArgumentNullException(nameof(message));
        var data = Encoding.ASCII.GetBytes(message);
        await webSocket.SendAsync(new(data), WebSocketMessageType.Text,
            true, token == null ? CancellationToken.None : token.Value);
    }

    private async Task ReceiveAsync()
    {
        var data = string.Empty;
        WebSocketReceiveResult result;
        while (true)
        {
            try
            {
                while (webSocket != null && tokenSource != null && !tokenSource.Token.IsCancellationRequested &&
                    (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseSent))
                {
                    var buffer = new ArraySegment<byte>(new byte[8192]);
                    result = await webSocket.ReceiveAsync(buffer, tokenSource.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;

                    data = Encoding.UTF8.GetString([.. buffer], buffer.Offset, buffer.Count);
                    DataHandler(data);
                }
                await DisconnectAsync();
                PropertyChanged.Invoke(this, new(nameof(Connected)));
                return;
            }
            catch (TaskCanceledException)
            {
                await DisconnectAsync();
                PropertyChanged.Invoke(this, new(nameof(Connected)));
                return;
            }
            catch (Exception ex)
            {
                AddInfo("WebSocketManager: " + ex.Message, notify: true);
                AddInfo("StackTrace: " + ex.StackTrace);
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
