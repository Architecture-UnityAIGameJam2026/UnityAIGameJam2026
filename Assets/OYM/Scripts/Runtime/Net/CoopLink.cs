using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace OYM.Net
{
    /// The wire between the two titles / games: one TCP connection on the LAN (the host listens on Port, the guest
    /// connects), framed messages [u16 length][payload], payload[0] = the message type (CoopMsg). A reader thread queues
    /// whole frames for the main thread, a writer thread sends. Nagle off: small state packets go out at once.
    /// A second guest is told the room is full and dropped. Everything here is thread-safe; Poll / Send are main thread.
    public sealed class CoopLink : IDisposable
    {
        public const int Port = 7781;
        const int MaxFrame = 60000;

        public enum State { Idle, Listening, Connecting, Connected, Closed }

        TcpListener _listener;
        TcpClient _client;
        NetworkStream _stream;
        Thread _accept, _reader, _writer, _connect;
        readonly ConcurrentQueue<byte[]> _in = new ConcurrentQueue<byte[]>();
        readonly BlockingCollection<byte[]> _out = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), 4096);
        volatile State _state = State.Idle;
        volatile string _error;
        volatile bool _disposed;
        readonly object _gate = new object();
        long _lastRecv = DateTime.UtcNow.Ticks;

        public State Current => _state;
        public bool Connected => _state == State.Connected;
        /// why it closed / failed (null while fine)
        public string Error => _error;
        public string RemoteAddress { get; private set; }
        /// seconds since the last frame arrived (the writer thread pings every second, also while a scene loads)
        public float Silence => (float)TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastRecv)).TotalSeconds;

        // ------------------------------------------------------------------------------------------------ host
        /// Listen on all interfaces. False (and Error set) when the port is taken (another title on this PC hosts).
        public bool Listen()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Server.ExclusiveAddressUse = true;
                _listener.Start(2);
            }
            catch (Exception e)
            {
                _error = e.Message;
                _listener = null;
                _state = State.Closed;
                return false;
            }
            _state = State.Listening;
            _accept = new Thread(AcceptLoop) { IsBackground = true, Name = "OYM co-op accept" };
            _accept.Start();
            return true;
        }

        void AcceptLoop()
        {
            while (!_disposed)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch { break; }                                       // Stop() / Dispose()
                lock (_gate)
                {
                    if (_client != null || _disposed)
                    {
                        // one guest per room: say so and drop the extra connection
                        try { var s = c.GetStream(); var f = Frame(CoopMsg.RejectMsg("THE ROOM ALREADY HAS TWO PILOTS")); s.Write(f, 0, f.Length); }
                        catch { }
                        try { c.Close(); } catch { }
                        continue;
                    }
                    Attach(c);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ guest
        /// Connect to a host (non-blocking; Current goes Connecting -> Connected or Closed with Error).
        public void Connect(string ip, int timeoutMs = 4000)
        {
            _state = State.Connecting;
            _connect = new Thread(() =>
            {
                var c = new TcpClient();
                try
                {
                    var task = c.ConnectAsync(ip, Port);
                    if (!task.Wait(timeoutMs) || !c.Connected) throw new TimeoutException("no answer from " + ip);
                    lock (_gate)
                    {
                        if (_disposed) { c.Close(); return; }
                        Attach(c);
                    }
                }
                catch (Exception e)
                {
                    try { c.Close(); } catch { }
                    _error = e is AggregateException ae && ae.InnerException != null ? ae.InnerException.Message : e.Message;
                    if (!_disposed) _state = State.Closed;
                }
            }) { IsBackground = true, Name = "OYM co-op connect" };
            _connect.Start();
        }

        // ------------------------------------------------------------------------------------------------ the connection
        void Attach(TcpClient c)
        {
            _client = c;
            c.NoDelay = true;
            c.ReceiveBufferSize = 1 << 16;
            c.SendBufferSize = 1 << 16;
            _stream = c.GetStream();
            try { RemoteAddress = ((IPEndPoint)c.Client.RemoteEndPoint).Address.ToString(); } catch { RemoteAddress = "?"; }
            Interlocked.Exchange(ref _lastRecv, DateTime.UtcNow.Ticks);
            _state = State.Connected;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "OYM co-op read" };
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "OYM co-op write" };
            _reader.Start();
            _writer.Start();
        }

        void ReadLoop()
        {
            var head = new byte[2];
            try
            {
                while (!_disposed)
                {
                    if (!ReadExact(head, 2)) break;
                    int n = head[0] | (head[1] << 8);
                    if (n <= 0 || n > MaxFrame) throw new IOException("bad frame " + n);
                    var buf = new byte[n];
                    if (!ReadExact(buf, n)) break;
                    Interlocked.Exchange(ref _lastRecv, DateTime.UtcNow.Ticks);
                    if (n == 1 && buf[0] == CoopMsg.Ping) continue;           // keep-alive only
                    _in.Enqueue(buf);
                }
            }
            catch (Exception e) { if (!_disposed) _error = e.Message; }
            if (!_disposed) { if (_error == null) _error = "the other side closed the connection"; _state = State.Closed; }
        }

        bool ReadExact(byte[] b, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = _stream.Read(b, got, n - got);
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        void WriteLoop()
        {
            var ping = Frame(new[] { CoopMsg.Ping });
            try
            {
                while (!_disposed)
                {
                    // a ping after a quiet second: the other side's silence check keeps working while our main
                    // thread is busy loading a scene
                    if (!_out.TryTake(out var f, 1000)) f = ping;
                    if (_out.IsAddingCompleted && f == ping) break;
                    _stream.Write(f, 0, f.Length);
                }
            }
            catch (Exception e) { if (!_disposed) { _error = e.Message; _state = State.Closed; } }
        }

        static byte[] Frame(byte[] payload)
        {
            var f = new byte[payload.Length + 2];
            f[0] = (byte)(payload.Length & 0xff); f[1] = (byte)(payload.Length >> 8);
            Buffer.BlockCopy(payload, 0, f, 2, payload.Length);
            return f;
        }

        // ------------------------------------------------------------------------------------------------ main thread API
        public void Send(byte[] payload)
        {
            if (_state != State.Connected || payload == null || payload.Length == 0) return;
            if (payload.Length > MaxFrame) { Debug.LogWarning($"[OYM-COOP] message {payload[0]} too big ({payload.Length} B), dropped"); return; }
            try { _out.TryAdd(Frame(payload)); } catch (InvalidOperationException) { }
        }

        public bool Poll(out byte[] frame) => _in.TryDequeue(out frame);

        /// Send `last` (e.g. Bye) if connected, let the writer flush for a moment, then close.
        public void Shutdown(byte[] last)
        {
            if (_disposed) return;
            if (last != null && _state == State.Connected) { try { _out.TryAdd(Frame(last)); } catch (InvalidOperationException) { } }
            try { _out.CompleteAdding(); } catch { }
            if (_writer != null && _writer.IsAlive) _writer.Join(300);
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _state = State.Closed;
            try { _out.CompleteAdding(); } catch { }
            try { _listener?.Stop(); } catch { }
            lock (_gate)
            {
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
            }
        }
    }
}
