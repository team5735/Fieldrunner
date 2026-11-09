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
        /// <param name="topic">Full topic name.</param>
        /// <param name="serverTimeMicros">Server timestamp of the value, microseconds.</param>
        /// <param name="value">Decoded value.</param>
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
        private const int ReconnectDelayMs = 1000; // milliseconds between connection attempts
        private const int TimeSyncPeriodMs = 3000; // milliseconds between server clock offset measurements
        private const int ReceiveBufferBytes = 64 * 1024; // receive chunk size, bytes
        private const double KeepAliveS = 1; // seconds between WebSocket keep-alive pings

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
        private long serverOffsetMicros; // server time minus local time, microseconds
        private int nextUid; // next subuid/pubuid
        private volatile bool isConnected; // true while the socket is open

        /// <summary>
        /// Prepares a client; call <see cref="Start"/> to begin connecting.
        /// </summary>
        /// <param name="host">Server host name or address, e.g. "localhost".</param>
        /// <param name="port">NT4 port; 5810 for robot code and the WPILib sim.</param>
        /// <param name="clientName">Name shown in the server's client list.</param>
        public Nt4Client(string host, int port, string clientName)
        {
            string escapedName = Uri.EscapeDataString(clientName);
            serverUri = new Uri("ws://" + host + ":" + port + "/nt/" + escapedName);
            socket = null;
            serverOffsetMicros = 0;
            nextUid = 1;
            isConnected = false;
        }

        /// <summary>Whether the client is connected to the server.</summary>
        public bool IsConnected()
        {
            return isConnected;
        }

        /// <summary>Current server time: the local clock plus the measured offset.</summary>
        /// <returns>Server time, microseconds.</returns>
        public long GetServerTimeMicros()
        {
            long localMicros = GetLocalMicros();
            long offsetMicros = Interlocked.Read(ref serverOffsetMicros);
            return localMicros + offsetMicros;
        }

        /// <summary>Starts the background connect/receive loop.</summary>
        public void Start()
        {
            CancellationToken token = stop.Token;
            Task.Run(() => RunAsync(token));
        }

        /// <summary>
        /// Subscribes to every topic whose name starts with a prefix. Kept across reconnects.
        /// </summary>
        /// <param name="prefix">Topic name prefix, e.g. "/DriverSim/".</param>
        /// <param name="periodicS">Shortest time between updates the server sends, seconds.</param>
        public void SubscribePrefix(string prefix, double periodicS)
        {
            string quotedPrefix = MiniJson.MakeJsonString(prefix);
            string periodicText = periodicS.ToString(CultureInfo.InvariantCulture);
            string message;
            lock (stateLock)
            {
                int subuid = nextUid;
                nextUid++;
                message = "{\"method\":\"subscribe\",\"params\":{\"topics\":[" + quotedPrefix + "],\"subuid\":"
                    + subuid + ",\"options\":{\"prefix\":true,\"periodic\":" + periodicText + "}}}";
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
            string quotedTopic = MiniJson.MakeJsonString(topic);
            string quotedType = MiniJson.MakeJsonString(type);
            int pubuid;
            string message;
            lock (stateLock)
            {
                pubuid = nextUid;
                nextUid++;
                message = "{\"method\":\"publish\",\"params\":{\"name\":" + quotedTopic + ",\"pubuid\":" + pubuid
                    + ",\"type\":" + quotedType + ",\"properties\":{}}}";
                publishMessages[pubuid] = message;
            }
            SendTextIfConnected("[" + message + "]");
            return pubuid;
        }

        /// <summary>Sends a string value on a published topic; dropped while disconnected.</summary>
        /// <param name="pubuid">Publisher id from <see cref="Publish"/>.</param>
        /// <param name="value">Text to send.</param>
        public void SendString(int pubuid, string value)
        {
            MsgPackWriter writer = StartValueMessage(pubuid, Nt4Types.String);
            writer.WriteString(value);
            byte[] data = writer.GetBytes();
            SendBinaryIfConnected(data);
        }

        /// <summary>Sends a double[] value on a published topic; dropped while disconnected.</summary>
        /// <param name="pubuid">Publisher id from <see cref="Publish"/>.</param>
        /// <param name="values">Numbers to send.</param>
        public void SendDoubleArray(int pubuid, double[] values)
        {
            MsgPackWriter writer = StartValueMessage(pubuid, Nt4Types.DoubleArray);
            writer.WriteArrayHeader(values.Length);
            foreach (double value in values)
            {
                writer.WriteDouble(value);
            }
            byte[] data = writer.GetBytes();
            SendBinaryIfConnected(data);
        }

        /// <summary>Takes the oldest received value update, if any.</summary>
        /// <param name="value">The update, when one was waiting.</param>
        /// <returns>False when no update was waiting.</returns>
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

        // ---- connection loop (background tasks)

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // one connection attempt and its lifetime
                ClientWebSocket ws = new ClientWebSocket();
                ws.Options.AddSubProtocol(SubprotocolV41);
                ws.Options.AddSubProtocol(SubprotocolV40);
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(KeepAliveS);
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
                    Task timeSync = RunTimeSyncAsync(ws, token);
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

                // wait before trying again
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
            if (messages.Count == 0)
            {
                return;
            }
            string joined = string.Join(",", messages);
            byte[] frame = Encoding.UTF8.GetBytes("[" + joined + "]");
            await SendAsync(ws, frame, WebSocketMessageType.Text, token).ConfigureAwait(false);
        }

        private async Task RunTimeSyncAsync(ClientWebSocket ws, CancellationToken token)
        {
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                // [-1, 0, int, localTime]: the server echoes our time with its own as the timestamp
                long localMicros = GetLocalMicros();
                MsgPackWriter writer = new MsgPackWriter();
                writer.WriteArrayHeader(4);
                writer.WriteInt(-1);
                writer.WriteInt(0);
                writer.WriteInt(Nt4Types.Int);
                writer.WriteInt(localMicros);
                byte[] data = writer.GetBytes();
                try
                {
                    await SendAsync(ws, data, WebSocketMessageType.Binary, token).ConfigureAwait(false);
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
            ArraySegment<byte> chunkSegment = new ArraySegment<byte>(chunk);
            byte[] message = new byte[ReceiveBufferBytes];
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                // gather one whole WebSocket message
                int length = 0;
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(chunkSegment, token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    int needed = length + result.Count;
                    if (needed > message.Length)
                    {
                        int newSize = Math.Max(message.Length * 2, needed);
                        Array.Resize(ref message, newSize);
                    }
                    Array.Copy(chunk, 0, message, length, result.Count);
                    length = needed;
                } while (!result.EndOfMessage);

                // dispatch it
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string text = Encoding.UTF8.GetString(message, 0, length);
                    HandleText(text);
                }
                else
                {
                    HandleBinary(message, length);
                }
            }
        }

        // ---- incoming messages (receive task)

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
                if (parameters == null || !parameters.ContainsKey("id"))
                {
                    continue;
                }

                // topic ids come and go as publishers appear and disappear
                long id = (long)(double)parameters["id"];
                if (method == "announce" && parameters.ContainsKey("name"))
                {
                    string name = parameters["name"] as string;
                    lock (stateLock)
                    {
                        topicNames[id] = name;
                    }
                }
                else if (method == "unannounce")
                {
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
            while (reader.HasMore())
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
                    long nowMicros = GetLocalMicros();
                    long halfRoundTripMicros = (nowMicros - sentMicros) / 2;
                    long offsetMicros = timestampMicros + halfRoundTripMicros - nowMicros;
                    Interlocked.Exchange(ref serverOffsetMicros, offsetMicros);
                    continue;
                }

                // value update for a known topic
                string topic;
                lock (stateLock)
                {
                    topicNames.TryGetValue(topicId, out topic);
                }
                if (topic != null)
                {
                    Nt4Value update = new Nt4Value(topic, timestampMicros, message[3]);
                    received.Enqueue(update);
                }
            }
        }

        // ---- outgoing messages

        private MsgPackWriter StartValueMessage(int pubuid, int typeId)
        {
            long serverTimeMicros = GetServerTimeMicros();
            MsgPackWriter writer = new MsgPackWriter();
            writer.WriteArrayHeader(4);
            writer.WriteInt(pubuid);
            writer.WriteInt(serverTimeMicros);
            writer.WriteInt(typeId);
            return writer;
        }

        private void SendTextIfConnected(string text)
        {
            ClientWebSocket ws = socket;
            if (ws != null && isConnected)
            {
                byte[] data = Encoding.UTF8.GetBytes(text);
                _ = SendAsync(ws, data, WebSocketMessageType.Text, stop.Token);
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
                    ArraySegment<byte> segment = new ArraySegment<byte>(data);
                    await ws.SendAsync(segment, type, true, token).ConfigureAwait(false);
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

        private long GetLocalMicros()
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
