using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DriverSim.Net
{
    /// <summary>
    /// One value update received from the NetworkTables server.
    /// </summary>
    public readonly struct Nt4Value
    {
        /// <summary>Creates an update.</summary>
        public Nt4Value(string topic, long serverTimeMicros, object value)
        {
            Topic = topic;
            ServerTimeMicros = serverTimeMicros;
            Value = value;
        }

        /// <summary>Full topic name, e.g. "/DriverSim/robot/state".</summary>
        public string Topic { get; }

        /// <summary>Server timestamp of the value, microseconds.</summary>
        public long ServerTimeMicros { get; }

        /// <summary>Decoded value: long, double, bool, string, byte[] or object[] (arrays).</summary>
        public object Value { get; }
    }

    /// <summary>
    /// Small NetworkTables 4.1 client (WebSocket + JSON + MessagePack) with no dependencies beyond .NET. It connects
    /// in the background, reconnects on its own, re-sends subscriptions and publications after a reconnect, and keeps
    /// its clock in sync with the server so published values carry server timestamps.
    ///
    /// Threading: networking runs on background tasks. Received values are queued; drain them from the main thread
    /// with <see cref="TryDequeue"/>. Publishing methods are safe to call from any thread.
    /// </summary>
    public sealed class Nt4Client : IDisposable
    {
        private const string SubprotocolV41 = "v4.1.networktables.first.wpi.edu"; // NT 4.1
        private const string SubprotocolV40 = "networktables.first.wpi.edu"; // NT 4.0 fallback
        private const int ReconnectDelayMs = 1000; // wait between connection attempts
        private const int TimeSyncPeriodMs = 3000; // how often to re-measure the server clock offset
        private const int ReceiveBufferBytes = 64 * 1024; // receive chunk size

        private readonly Uri serverUri; // ws://host:port/nt/name
        private readonly CancellationTokenSource stop = new CancellationTokenSource(); // ends all background work
        private readonly ConcurrentQueue<Nt4Value> received = new ConcurrentQueue<Nt4Value>(); // updates for the app
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1); // ClientWebSocket allows one sender
        private readonly Stopwatch clock = Stopwatch.StartNew(); // local monotonic clock
        private readonly object stateLock = new object(); // guards the collections below
        private readonly List<string> subscribeMessages = new List<string>(); // JSON to re-send on connect
        private readonly Dictionary<int, string> publishMessages = new Dictionary<int, string>(); // pubuid -> JSON
        private readonly Dictionary<long, string> topicNames = new Dictionary<long, string>(); // server id -> name
        private ClientWebSocket socket; // current connection, null between attempts
        private long serverOffsetMicros; // server time minus local time
        private int nextUid = 1; // next subuid/pubuid
        private volatile bool isConnected; // true while the socket is open

        /// <summary>
        /// Prepares a client; call <see cref="Start"/> to begin connecting.
        /// </summary>
        /// <param name="host">Server host name or address, e.g. "localhost".</param>
        /// <param name="port">NT4 port; 5810 for robot code and the WPILib sim.</param>
        /// <param name="clientName">Name shown in the server's client list.</param>
        public Nt4Client(string host, int port, string clientName)
        {
            serverUri = new Uri("ws://" + host + ":" + port + "/nt/" + Uri.EscapeDataString(clientName));
        }

        /// <summary>True while connected to the server.</summary>
        public bool IsConnected
        {
            get { return isConnected; }
        }

        /// <summary>Current server time, microseconds (local clock plus the measured offset).</summary>
        public long ServerTimeMicros
        {
            get { return LocalMicros() + Interlocked.Read(ref serverOffsetMicros); }
        }

        /// <summary>Starts the background connect/receive loop.</summary>
        public void Start()
        {
            Task.Run(() => RunAsync(stop.Token));
        }

        /// <summary>
        /// Subscribes to every topic whose name starts with a prefix. Kept across reconnects.
        /// </summary>
        /// <param name="prefix">Topic name prefix, e.g. "/DriverSim/".</param>
        /// <param name="periodicS">How often the server may send updates, seconds.</param>
        public void SubscribePrefix(string prefix, double periodicS)
        {
            string message;
            lock (stateLock)
            {
                int subuid = nextUid;
                nextUid++;
                message = "{\"method\":\"subscribe\",\"params\":{\"topics\":[" + MiniJson.Quote(prefix)
                    + "],\"subuid\":" + subuid + ",\"options\":{\"prefix\":true,\"periodic\":"
                    + periodicS.ToString(CultureInfo.InvariantCulture) + "}}}";
                subscribeMessages.Add(message);
            }
            SendTextIfConnected("[" + message + "]");
        }

        /// <summary>
        /// Announces that this client publishes a topic. Kept across reconnects.
        /// </summary>
        /// <param name="topic">Full topic name.</param>
        /// <param name="type">NT4 type string, e.g. "string" or "double[]".</param>
        /// <returns>Publisher id to pass to the Send methods.</returns>
        public int Publish(string topic, string type)
        {
            int pubuid;
            string message;
            lock (stateLock)
            {
                pubuid = nextUid;
                nextUid++;
                message = "{\"method\":\"publish\",\"params\":{\"name\":" + MiniJson.Quote(topic) + ",\"pubuid\":"
                    + pubuid + ",\"type\":" + MiniJson.Quote(type) + ",\"properties\":{}}}";
                publishMessages[pubuid] = message;
            }
            SendTextIfConnected("[" + message + "]");
            return pubuid;
        }

        /// <summary>Sends a string value on a published topic; dropped while disconnected.</summary>
        public void SendString(int pubuid, string value)
        {
            MsgPackWriter writer = StartValueMessage(pubuid, Nt4Types.String);
            writer.WriteString(value);
            SendBinaryIfConnected(writer.ToArray());
        }

        /// <summary>Sends a double[] value on a published topic; dropped while disconnected.</summary>
        public void SendDoubleArray(int pubuid, double[] values)
        {
            MsgPackWriter writer = StartValueMessage(pubuid, Nt4Types.DoubleArray);
            writer.WriteArrayHeader(values.Length);
            foreach (double value in values)
            {
                writer.WriteDouble(value);
            }
            SendBinaryIfConnected(writer.ToArray());
        }

        /// <summary>Takes the oldest received value update, if any.</summary>
        public bool TryDequeue(out Nt4Value value)
        {
            return received.TryDequeue(out value);
        }

        /// <summary>Stops all background work and closes the connection.</summary>
        public void Dispose()
        {
            stop.Cancel();
            ClientWebSocket current = socket;
            if (current != null)
            {
                current.Abort();
                current.Dispose();
            }
        }

        // ---- connection loop

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                ClientWebSocket ws = new ClientWebSocket();
                ws.Options.AddSubProtocol(SubprotocolV41);
                ws.Options.AddSubProtocol(SubprotocolV40);
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(1);
                try
                {
                    await ws.ConnectAsync(serverUri, token).ConfigureAwait(false);
                    socket = ws;
                    lock (stateLock)
                    {
                        topicNames.Clear();
                    }
                    isConnected = true;
                    await SendAnnouncementsAsync(ws, token).ConfigureAwait(false);
                    Task timeSync = TimeSyncLoopAsync(ws, token);
                    await ReceiveLoopAsync(ws, token).ConfigureAwait(false);
                    await timeSync.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // connection refused or dropped: fall through to reconnect
                }
                isConnected = false;
                socket = null;
                ws.Dispose();
                try
                {
                    await Task.Delay(ReconnectDelayMs, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        private async Task SendAnnouncementsAsync(ClientWebSocket ws, CancellationToken token)
        {
            List<string> messages = new List<string>();
            lock (stateLock)
            {
                messages.AddRange(publishMessages.Values);
                messages.AddRange(subscribeMessages);
            }
            if (messages.Count > 0)
            {
                string frame = "[" + string.Join(",", messages) + "]";
                await SendAsync(ws, Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, token)
                    .ConfigureAwait(false);
            }
        }

        private async Task TimeSyncLoopAsync(ClientWebSocket ws, CancellationToken token)
        {
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                // [-1, 0, int, localTime]: the server echoes our time with its own as the timestamp
                MsgPackWriter writer = new MsgPackWriter();
                writer.WriteArrayHeader(4);
                writer.WriteInt(-1);
                writer.WriteInt(0);
                writer.WriteInt(Nt4Types.Int);
                writer.WriteInt(LocalMicros());
                try
                {
                    await SendAsync(ws, writer.ToArray(), WebSocketMessageType.Binary, token).ConfigureAwait(false);
                    await Task.Delay(TimeSyncPeriodMs, token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken token)
        {
            byte[] chunk = new byte[ReceiveBufferBytes];
            byte[] message = new byte[ReceiveBufferBytes];
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                // gather one whole WebSocket message
                int length = 0;
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(chunk), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    if (length + result.Count > message.Length)
                    {
                        Array.Resize(ref message, Math.Max(message.Length * 2, length + result.Count));
                    }
                    Array.Copy(chunk, 0, message, length, result.Count);
                    length += result.Count;
                } while (!result.EndOfMessage);

                // dispatch it
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    HandleText(Encoding.UTF8.GetString(message, 0, length));
                }
                else
                {
                    HandleBinary(message, length);
                }
            }
        }

        // ---- incoming messages

        private void HandleText(string text)
        {
            List<object> messages = MiniJson.Parse(text) as List<object>;
            if (messages == null)
            {
                return;
            }
            foreach (object item in messages)
            {
                Dictionary<string, object> message = item as Dictionary<string, object>;
                if (message == null || !message.ContainsKey("method") || !message.ContainsKey("params"))
                {
                    continue;
                }
                string method = message["method"] as string;
                Dictionary<string, object> parameters = message["params"] as Dictionary<string, object>;
                if (parameters == null)
                {
                    continue;
                }
                if (method == "announce" && parameters.ContainsKey("id") && parameters.ContainsKey("name"))
                {
                    long id = (long)(double)parameters["id"];
                    lock (stateLock)
                    {
                        topicNames[id] = parameters["name"] as string;
                    }
                }
                else if (method == "unannounce" && parameters.ContainsKey("id"))
                {
                    long id = (long)(double)parameters["id"];
                    lock (stateLock)
                    {
                        topicNames.Remove(id);
                    }
                }
            }
        }

        private void HandleBinary(byte[] data, int length)
        {
            MsgPackReader reader = new MsgPackReader(data, length);
            while (reader.HasMore)
            {
                object[] message = reader.Read() as object[];
                if (message == null || message.Length != 4)
                {
                    return;
                }
                long topicId = (long)message[0];
                long timestampMicros = (long)message[1];

                // time sync reply: timestamp is server time, value is our send time
                if (topicId == -1)
                {
                    long sentMicros = (long)message[3];
                    long nowMicros = LocalMicros();
                    long halfRoundTrip = (nowMicros - sentMicros) / 2;
                    long offset = timestampMicros + halfRoundTrip - nowMicros;
                    Interlocked.Exchange(ref serverOffsetMicros, offset);
                    continue;
                }

                // value update
                string topic;
                lock (stateLock)
                {
                    topicNames.TryGetValue(topicId, out topic);
                }
                if (topic != null)
                {
                    received.Enqueue(new Nt4Value(topic, timestampMicros, message[3]));
                }
            }
        }

        // ---- outgoing messages

        private MsgPackWriter StartValueMessage(int pubuid, int typeId)
        {
            MsgPackWriter writer = new MsgPackWriter();
            writer.WriteArrayHeader(4);
            writer.WriteInt(pubuid);
            writer.WriteInt(ServerTimeMicros);
            writer.WriteInt(typeId);
            return writer;
        }

        private void SendTextIfConnected(string text)
        {
            ClientWebSocket ws = socket;
            if (ws != null && isConnected)
            {
                _ = SendAsync(ws, Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, stop.Token);
            }
        }

        private void SendBinaryIfConnected(byte[] data)
        {
            ClientWebSocket ws = socket;
            if (ws != null && isConnected)
            {
                _ = SendAsync(ws, data, WebSocketMessageType.Binary, stop.Token);
            }
        }

        private async Task SendAsync(ClientWebSocket ws, byte[] data, WebSocketMessageType type, CancellationToken token)
        {
            await sendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.SendAsync(new ArraySegment<byte>(data), type, true, token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // the receive loop notices the broken connection and reconnects
            }
            finally
            {
                sendLock.Release();
            }
        }

        private long LocalMicros()
        {
            return clock.ElapsedTicks * 1000000L / Stopwatch.Frequency;
        }
    }

    /// <summary>NT4 binary type ids.</summary>
    public static class Nt4Types
    {
        public const int Boolean = 0;
        public const int Double = 1;
        public const int Int = 2;
        public const int Float = 3;
        public const int String = 4;
        public const int Raw = 5;
        public const int BooleanArray = 16;
        public const int DoubleArray = 17;
        public const int IntArray = 18;
        public const int FloatArray = 19;
        public const int StringArray = 20;
    }
}
