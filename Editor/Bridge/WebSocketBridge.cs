// WebSocket client (Unity side). Connects to Node sidecar WS server on localhost.
// All inbound messages are routed through MainThreadDispatcher before invoking callbacks.
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BurgerMonster.ClaudeAgent
{
    public class WebSocketBridge : IDisposable
    {
        ClientWebSocket _ws;
        CancellationTokenSource _cts;
        readonly int _port;

        public event Action<IncomingMessage> OnMessage;
        public event Action<string> OnError;
        public event Action OnDisconnected;

        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public WebSocketBridge(int port) => _port = port;

        public async Task ConnectAsync()
        {
            _cts = new CancellationTokenSource();
            _ws  = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri($"ws://localhost:{_port}"), _cts.Token);
            _ = ReceiveLoopAsync();
        }

        public async Task SendAsync<T>(T payload)
        {
            if (!IsConnected) return;
            var json  = JsonUtility.ToJson(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                                true, _cts.Token);
        }

        async Task ReceiveLoopAsync()
        {
            var buf = new byte[8192];
            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token);
                        sb.Append(Encoding.UTF8.GetString(buf, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var json = sb.ToString();
                    var msg  = JsonUtility.FromJson<IncomingMessage>(json);
                    MainThreadDispatcher.Enqueue(() => OnMessage?.Invoke(msg));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MainThreadDispatcher.Enqueue(() => OnError?.Invoke(ex.Message));
            }
            finally
            {
                MainThreadDispatcher.Enqueue(() => OnDisconnected?.Invoke());
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _ws?.Dispose();
        }
    }
}
