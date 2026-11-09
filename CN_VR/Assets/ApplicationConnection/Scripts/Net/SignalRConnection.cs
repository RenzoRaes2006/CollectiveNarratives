using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CollectiveNarratives.Net
{
    public sealed class SignalRException : Exception
    {
        public SignalRException(string message) : base(message) { }
    }

    // Minimale SignalR-client: JSON-protocol over WebSockets, zonder extra packages (werkt dus ook met IL2CPP op de Quest).
    // Protocol: handshake {"protocol":"json","version":1}, daarna JSON-berichten die eindigen op teken 0x1E.
    //   type 1 = aanroep/event, type 3 = antwoord op een aanroep, type 6 = ping, type 7 = verbinding dicht.
    // Let op: Received en Closed komen binnen op een achtergrond-thread. In Unity eerst naar de main thread brengen
    // (SessionClient doet dat). Geen Unity-code hier: ook getest vanuit webapp/tests.
    public sealed class SignalRConnection : IDisposable
    {
        const byte RecordSeparator = 0x1E;
        // Vaak pingen: crasht de game, dan merkt de server dat binnen ~15 s (en toont het grote scherm een storing).
        static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);
        static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(30); // server pingt elke 5 s

        readonly ClientWebSocket _socket = new ClientWebSocket();
        readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new ConcurrentDictionary<string, TaskCompletionSource<string>>();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly TaskCompletionSource<bool> _handshake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int _invocationId;
        int _closed;
        bool _handshakeDone;
        long _lastReceivedTicks;

        // Event van de server: (naam, eerste argument als ruwe JSON, of "" zonder argument).
        public event Action<string, string> Received;

        // Verbinding weg: foutmelding, of null bij een nette sluiting.
        public event Action<string> Closed;

        public TimeSpan InvokeTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public bool IsConnected => Volatile.Read(ref _closed) == 0 && _socket.State == WebSocketState.Open;

        // hubUrl: bv. https://collective-narratives.app/hubs/session. accessToken: de game key (of null).
        public async Task ConnectAsync(string hubUrl, string accessToken, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await _socket.ConnectAsync(ToWebSocketUri(hubUrl, accessToken), timeout.Token).ConfigureAwait(false);
            }
            Interlocked.Exchange(ref _lastReceivedTicks, DateTime.UtcNow.Ticks);
            _ = Task.Run(ReceiveLoop);
            await SendRecordAsync("{\"protocol\":\"json\",\"version\":1}").ConfigureAwait(false);

            var done = await Task.WhenAny(_handshake.Task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            if (done != _handshake.Task)
            {
                Close("No response to the handshake");
                throw new TimeoutException("SignalR: no response to the handshake");
            }
            await _handshake.Task.ConfigureAwait(false); // gooit de fout van de server door
            _ = Task.Run(PingLoop);
        }

        // Roept een hub-methode aan en geeft het antwoord als ruwe JSON terug ("null" als er geen is).
        // rawArgs zijn al JSON: gebruik MiniJson.Quote voor tekst, MiniJson.Number voor getallen, JsonUtility.ToJson voor objecten.
        public async Task<string> InvokeAsync(string method, params string[] rawArgs)
        {
            if (!IsConnected) throw new SignalRException("Not connected");
            string id = Interlocked.Increment(ref _invocationId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            string json = "{\"type\":1,\"invocationId\":" + MiniJson.Quote(id) + ",\"target\":" + MiniJson.Quote(method)
                + ",\"arguments\":[" + string.Join(",", rawArgs ?? new string[0]) + "]}";
            try
            {
                await SendRecordAsync(json).ConfigureAwait(false);
            }
            catch
            {
                _pending.TryRemove(id, out _);
                throw;
            }

            var done = await Task.WhenAny(tcs.Task, Task.Delay(InvokeTimeout)).ConfigureAwait(false);
            if (done != tcs.Task)
            {
                _pending.TryRemove(id, out _);
                throw new TimeoutException("SignalR: no response to " + method);
            }
            return await tcs.Task.ConfigureAwait(false);
        }

        public async Task CloseAsync()
        {
            if (_socket.State == WebSocketState.Open)
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                        await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token).ConfigureAwait(false);
                }
                catch { /* maakt niet uit, we sluiten toch */ }
            }
            Close(null);
        }

        public void Dispose()
        {
            Close(null);
            _socket.Dispose();
        }

        // ---------- Intern ----------

        static Uri ToWebSocketUri(string hubUrl, string accessToken)
        {
            var b = new UriBuilder(hubUrl);
            if (b.Scheme == "https") b.Scheme = "wss";
            else if (b.Scheme == "http") b.Scheme = "ws";
            if (!string.IsNullOrEmpty(accessToken))
            {
                string query = b.Query.TrimStart('?');
                b.Query = (query.Length > 0 ? query + "&" : "") + "access_token=" + Uri.EscapeDataString(accessToken);
            }
            return b.Uri;
        }

        async Task SendRecordAsync(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json + (char)RecordSeparator);
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            var pending = new MemoryStream();
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Close("The server closed the connection");
                        return;
                    }
                    Interlocked.Exchange(ref _lastReceivedTicks, DateTime.UtcNow.Ticks);
                    pending.Write(buffer, 0, result.Count);
                    HandleRecords(pending);
                }
            }
            catch (Exception e)
            {
                Close(_cts.IsCancellationRequested ? null : e.Message);
            }
        }

        // Knipt de volledige berichten (tot 0x1E) uit de buffer; een half bericht blijft staan.
        void HandleRecords(MemoryStream pending)
        {
            byte[] buf = pending.GetBuffer();
            int length = (int)pending.Length;
            int start = 0;
            for (int i = 0; i < length; i++)
            {
                if (buf[i] != RecordSeparator) continue;
                string json = Encoding.UTF8.GetString(buf, start, i - start);
                start = i + 1;
                try
                {
                    HandleRecord(json);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    // Eén kapot bericht (of een fout in een handler) mag de verbinding niet stoppen.
                    System.Diagnostics.Debug.WriteLine("SignalR: skipped message: " + e.Message);
                }
            }
            int rest = length - start;
            if (rest > 0 && start > 0) Buffer.BlockCopy(buf, start, buf, 0, rest);
            pending.SetLength(rest);
            pending.Position = rest;
        }

        void HandleRecord(string json)
        {
            var msg = MiniJson.ParseObject(json);
            string raw;

            if (!_handshakeDone)
            {
                _handshakeDone = true;
                if (msg.TryGetValue("error", out raw)) _handshake.TrySetException(new SignalRException(MiniJson.ReadString(raw)));
                else _handshake.TrySetResult(true);
                return;
            }

            int type = msg.TryGetValue("type", out raw) ? MiniJson.ReadInt(raw) : 0;
            switch (type)
            {
                case 1: // event van de server
                    string target = MiniJson.ReadString(msg["target"]);
                    string arg = "";
                    if (msg.TryGetValue("arguments", out raw))
                    {
                        var args = MiniJson.ParseArray(raw);
                        if (args.Count > 0) arg = args[0];
                    }
                    var received = Received;
                    if (received != null) received(target, arg);
                    break;

                case 3: // antwoord op InvokeAsync
                    string id = MiniJson.ReadString(msg["invocationId"]);
                    TaskCompletionSource<string> tcs;
                    if (!_pending.TryRemove(id, out tcs)) break;
                    if (msg.TryGetValue("error", out raw)) tcs.TrySetException(new SignalRException(MiniJson.ReadString(raw)));
                    else tcs.TrySetResult(msg.TryGetValue("result", out raw) ? raw : "null");
                    break;

                case 7: // server sluit
                    Close(msg.TryGetValue("error", out raw) ? MiniJson.ReadString(raw) : "The server closed the connection");
                    break;

                // 6 = ping: niets te doen, _lastReceivedTicks is al bijgewerkt.
            }
        }

        async Task PingLoop()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await Task.Delay(PingInterval, _cts.Token).ConfigureAwait(false);
                    if (DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastReceivedTicks), DateTimeKind.Utc) > ServerTimeout)
                    {
                        Close("The server stopped responding");
                        return;
                    }
                    await SendRecordAsync("{\"type\":6}").ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Close(_cts.IsCancellationRequested ? null : e.Message);
            }
        }

        void Close(string error)
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            _cts.Cancel();
            _handshake.TrySetException(new SignalRException(error ?? "Connection closed"));
            foreach (var id in _pending.Keys)
            {
                TaskCompletionSource<string> tcs;
                if (_pending.TryRemove(id, out tcs)) tcs.TrySetException(new SignalRException("Connection lost"));
            }
            try { _socket.Abort(); } catch { }
            var closed = Closed;
            if (closed != null) closed(error);
        }
    }
}
