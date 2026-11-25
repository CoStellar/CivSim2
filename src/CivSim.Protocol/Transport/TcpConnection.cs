using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Threading;
using CivSim.Protocol.Messages;

namespace CivSim.Protocol.Transport;

/// <summary>
/// One end of a network connection over TCP (multiplayer plan, MP1). The same frames as <see cref="InMemoryConnection"/>
/// (<see cref="ProtocolSerializer"/>), with a length in front and Brotli for big frames.
/// </summary>
/// <remarks>
/// <para>Wire format. Each side first sends 8 bytes: <c>CIVS</c> and the protocol version (int32, little-endian); another
/// program or another version is turned away before any message is decoded. Then frames: a 4-byte little-endian header whose
/// top bit says "Brotli" and whose other bits give the length of what follows. A Brotli frame starts with the length of the
/// unpacked frame (int32), which may not be more than the limit of this end, so a small packet cannot unpack into gigabytes.</para>
/// <para>Each connection has a reading and a writing thread. Messages are decoded on the reading thread, so a bad frame closes
/// the connection (with <see cref="CloseReason"/>) and never reaches the game thread; frames are packed on the writing thread,
/// so Brotli does not slow the game. <see cref="Send"/> after the connection closed does nothing: the other side may go away at any
/// moment, and the server must not stop because of it.</para>
/// </remarks>
public sealed class TcpConnection : IConnection, IDisposable
{
    /// <summary>Port a server listens on unless told otherwise (multiplayer plan, chapter 6).</summary>
    public const int DefaultPort = 7777;

    /// <summary>Biggest frame a client may send the server (commands are a few hundred bytes).</summary>
    public const int MaxClientFrame = 256 * 1024;

    /// <summary>Biggest frame the server may send a client, unpacked (the whole map of a huge world at the start).</summary>
    public const int MaxServerFrame = 64 * 1024 * 1024;

    /// <summary>Frames from this size up are packed with Brotli.</summary>
    public const int PackFrom = 512;

    /// <summary>Received messages the game has not taken yet; a client that floods the server beyond this is cut off.</summary>
    public const int MaxUnread = 4096;

    /// <summary>
    /// Frames waiting to be written beyond which the connection counts as congested: the server then sends no new views
    /// to it until it catches up (views are differences from what was sent, so nothing is lost).
    /// </summary>
    public const int CongestedFrames = 32;

    /// <summary>Bytes waiting to be written beyond which the connection counts as congested.</summary>
    public const long CongestedBytes = 8 * 1024 * 1024;

    /// <summary>Time the other side has to say who it is after connecting.</summary>
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    private const uint PackedBit = 0x8000_0000u;
    private const int BrotliQuality = 4;
    private const int BrotliWindow = 22;
    private static ReadOnlySpan<byte> Magic => "CIVS"u8;

    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly int _maxIncoming;
    private readonly ConcurrentQueue<IProtocolMessage> _inbox = new();
    private readonly ConcurrentQueue<byte[]> _outbox = new();
    private readonly SemaphoreSlim _outboxSignal = new(0);
    private readonly Thread _reader;
    private readonly Thread _writer;
    private int _closed;
    private long _bytesSent;
    private long _bytesReceived;
    private long _rawBytesSent;
    private long _framesSent;
    private long _framesReceived;
    private long _queuedBytes;
    private string? _closeReason;

    /// <summary>Wraps a connected socket. <paramref name="maxIncomingFrame"/> is the biggest frame the other side may send.</summary>
    public TcpConnection(Socket socket, int maxIncomingFrame)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxIncomingFrame, 64);
        _socket = socket;
        _socket.NoDelay = true;
        _stream = new NetworkStream(socket, ownsSocket: false);
        _maxIncoming = maxIncomingFrame;
        RemoteAddress = socket.RemoteEndPoint?.ToString() ?? "?";
        RemoteIp = (socket.RemoteEndPoint as System.Net.IPEndPoint)?.Address;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "CivSim net read" };
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "CivSim net write" };
        _writer.Start();
        _reader.Start();
    }

    /// <summary>
    /// Connects to a server (client side). Throws <see cref="SocketException"/> when nobody answers within <paramref name="timeout"/>.
    /// </summary>
    public static TcpConnection Connect(string host, int port, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            socket.ConnectAsync(host, port, cts.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw new SocketException((int)SocketError.TimedOut);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new TcpConnection(socket, MaxServerFrame);
    }

    /// <summary>Address of the other side, for the server's log.</summary>
    public string RemoteAddress { get; }

    /// <summary>IP address of the other side (null when unknown).</summary>
    public System.Net.IPAddress? RemoteIp { get; }

    /// <inheritdoc />
    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    /// <summary>Why the connection closed (null while open, or when this side closed it).</summary>
    public string? CloseReason => Volatile.Read(ref _closeReason);

    /// <inheritdoc />
    public int Backlog => _outbox.Count;

    /// <inheritdoc />
    public bool Congested => _outbox.Count > CongestedFrames || Interlocked.Read(ref _queuedBytes) > CongestedBytes;

    /// <summary>Received messages not taken yet.</summary>
    public int Unread => _inbox.Count;

    /// <summary>Bytes written to the network (after packing).</summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>Bytes the frames sent would take without packing.</summary>
    public long RawBytesSent => Interlocked.Read(ref _rawBytesSent);

    /// <summary>Bytes read from the network.</summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <summary>Frames written to the network.</summary>
    public long FramesSent => Interlocked.Read(ref _framesSent);

    /// <summary>Frames read from the network and waiting in <see cref="TryReceive"/> (or taken).</summary>
    public long FramesReceived => Interlocked.Read(ref _framesReceived);

    /// <inheritdoc />
    public void Send(IProtocolMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsOpen)
        {
            return;
        }

        byte[] frame = ProtocolSerializer.Serialize(message);
        Interlocked.Add(ref _queuedBytes, frame.Length);
        _outbox.Enqueue(frame);
        _outboxSignal.Release();
    }

    /// <inheritdoc />
    public bool TryReceive(out IProtocolMessage? message)
    {
        if (_inbox.TryDequeue(out IProtocolMessage? m))
        {
            message = m;
            return true;
        }

        message = null;
        return false;
    }

    /// <inheritdoc />
    public void Close() => Shut(null);

    /// <inheritdoc />
    public void Dispose()
    {
        Shut(null);
        _outboxSignal.Dispose();
    }

    private void Shut(string? reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _closeReason, reason);
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        _socket.Close();
        try
        {
            _outboxSignal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ReadLoop()
    {
        try
        {
            byte[] hello = new byte[8];
            _socket.ReceiveTimeout = (int)HandshakeTimeout.TotalMilliseconds;
            if (!ReadExactly(hello))
            {
                Shut("The other side closed the connection before saying who it is.");
                return;
            }

            if (!hello.AsSpan(0, 4).SequenceEqual(Magic))
            {
                Shut("The other side is not a CivSim game.");
                return;
            }

            int version = BinaryPrimitives.ReadInt32LittleEndian(hello.AsSpan(4));
            if (version != ProtocolInfo.Version)
            {
                Shut($"The other side speaks protocol {version}; this game speaks {ProtocolInfo.Version}. Both need the same version of the game.");
                return;
            }

            _socket.ReceiveTimeout = 0;
            byte[] header = new byte[4];
            while (IsOpen)
            {
                if (!ReadExactly(header))
                {
                    Shut("The other side closed the connection.");
                    return;
                }

                uint h = BinaryPrimitives.ReadUInt32LittleEndian(header);
                bool packed = (h & PackedBit) != 0;
                int length = (int)(h & ~PackedBit);
                if (length < 4 || length > _maxIncoming)
                {
                    Shut($"A frame of {length} bytes is over the limit of {_maxIncoming}.");
                    return;
                }

                byte[] body = new byte[length];
                if (!ReadExactly(body))
                {
                    Shut("The other side closed the connection in the middle of a message.");
                    return;
                }

                byte[] frame = packed ? Unpack(body) : body;
                if (frame.Length == 0)
                {
                    return; // Unpack closed the connection
                }

                IProtocolMessage message = ProtocolSerializer.Deserialize(frame);
                if (_inbox.Count >= MaxUnread)
                {
                    Shut($"More than {MaxUnread} messages waiting: the other side sends faster than the game reads.");
                    return;
                }

                _inbox.Enqueue(message);
                Interlocked.Increment(ref _framesReceived);
            }
        }
        catch (ProtocolException ex)
        {
            Shut("A message could not be read: " + ex.Message);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Shut(IsOpen ? "The connection was lost: " + ex.Message : null);
        }
    }

    private byte[] Unpack(byte[] body)
    {
        int raw = body.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(body) : -1;
        if (raw < 4 || raw > _maxIncoming)
        {
            Shut($"A packed frame says it unpacks to {raw} bytes, over the limit of {_maxIncoming}.");
            return [];
        }

        byte[] frame = new byte[raw];
        if (!BrotliDecoder.TryDecompress(body.AsSpan(4), frame, out int written) || written != raw)
        {
            Shut("A packed frame could not be unpacked.");
            return [];
        }

        return frame;
    }

    private bool ReadExactly(byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = _stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0)
            {
                return false;
            }

            read += n;
        }

        Interlocked.Add(ref _bytesReceived, buffer.Length);
        return true;
    }

    private void WriteLoop()
    {
        try
        {
            byte[] hello = new byte[8];
            Magic.CopyTo(hello);
            BinaryPrimitives.WriteInt32LittleEndian(hello.AsSpan(4), ProtocolInfo.Version);
            Write(hello);
            byte[] header = new byte[4];
            byte[] packBuffer = [];
            while (IsOpen)
            {
                _outboxSignal.Wait();
                while (_outbox.TryDequeue(out byte[]? frame))
                {
                    Interlocked.Add(ref _queuedBytes, -frame.Length);
                    Interlocked.Add(ref _rawBytesSent, frame.Length + 4);
                    if (frame.Length >= PackFrom)
                    {
                        int bound = 4 + BrotliEncoder.GetMaxCompressedLength(frame.Length);
                        if (packBuffer.Length < bound)
                        {
                            packBuffer = new byte[bound];
                        }

                        if (BrotliEncoder.TryCompress(frame, packBuffer.AsSpan(4), out int packed, BrotliQuality, BrotliWindow) && packed + 4 < frame.Length)
                        {
                            BinaryPrimitives.WriteInt32LittleEndian(packBuffer, frame.Length);
                            BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(packed + 4) | PackedBit);
                            Write(header);
                            _stream.Write(packBuffer, 0, packed + 4);
                            Interlocked.Add(ref _bytesSent, packed + 4);
                            Interlocked.Increment(ref _framesSent);
                            continue;
                        }
                    }

                    BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)frame.Length);
                    Write(header);
                    Write(frame);
                    Interlocked.Increment(ref _framesSent);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Shut(IsOpen ? "The connection was lost: " + ex.Message : null);
        }
    }

    private void Write(byte[] bytes)
    {
        _stream.Write(bytes, 0, bytes.Length);
        Interlocked.Add(ref _bytesSent, bytes.Length);
    }
}
