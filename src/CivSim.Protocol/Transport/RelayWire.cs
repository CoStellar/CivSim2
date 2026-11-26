using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CivSim.Protocol.Transport;

/// <summary>
/// The few bytes a game server, a player and the relay say to each other before the relay starts passing bytes between them
/// (multiplayer plan, chapter 4, MP6). The relay never reads the game: after this greeting it only copies bytes both ways, so it
/// does not change with the game's protocol. This file uses nothing but .NET, so the relay program links it without the game.
/// </summary>
/// <remarks>
/// <para>Greeting: <c>CVRL</c>, a version byte, a kind byte and short texts (2-byte length, UTF-8). Kinds:</para>
/// <list type="bullet">
/// <item><see cref="Open"/> (a game server): password to open games, previous code and secret (to get the same code back after a
/// lost connection), game version. Answer: status, room code, room secret. The connection then stays open as the room's control
/// line: the relay sends <see cref="Incoming"/> with a ticket for each player who wants in, and <see cref="Ping"/>; the server answers
/// pings with <see cref="Pong"/>.</item>
/// <item><see cref="Join"/> (a player): room code. Answer once the game server took the player: status and a text; then the bytes of
/// the game (a <see cref="TcpConnection"/>).</item>
/// <item><see cref="Accept"/> (the game server, a new connection for each player): code, secret and ticket. Answer: status; then the
/// bytes of the game.</item>
/// </list>
/// <para>A domain name (<c>relay.example.com</c>) is reached over TLS with a checked certificate (Let's Encrypt on the
/// relay); an IP address, <c>localhost</c> or a name without a dot (a container next door, <c>relay</c>) without TLS (multiplayer plan, chapter 9). The relay tells the two apart by the first byte.</para>
/// </remarks>
public static class RelayWire
{
    /// <summary>Port a relay listens on unless told otherwise.</summary>
    public const int DefaultPort = 7700;

    /// <summary>Version of the greeting; a relay turns away other versions with a clear reason.</summary>
    public const byte Version = 1;

    /// <summary>A game server opens a room.</summary>
    public const byte Open = 1;

    /// <summary>A player joins a room.</summary>
    public const byte Join = 2;

    /// <summary>The game server takes a player who asked to join.</summary>
    public const byte Accept = 3;

    /// <summary>Control line, relay to server: a player wants in (followed by the ticket).</summary>
    public const byte Incoming = 1;

    /// <summary>Control line, relay to server: are you there.</summary>
    public const byte Ping = 2;

    /// <summary>Control line, server to relay: still here.</summary>
    public const byte Pong = 3;

    /// <summary>First byte of a TLS connection (a handshake record).</summary>
    public const byte TlsFirstByte = 0x16;

    /// <summary>Longest text in a greeting.</summary>
    public const int MaxText = 256;

    /// <summary>Time either side has for the greeting.</summary>
    public static readonly TimeSpan GreetingTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Letters of room codes: no look-alikes (0/O, 1/I/L, 2/Z, 5/S, 8/B).</summary>
    public const string CodeLetters = "ACDEFGHJKMNPQRTUVWXY34679";

    /// <summary>Letters in a room code (shown as two groups of three, e.g. <c>K7M-4QD</c>).</summary>
    public const int CodeLength = 6;

    private static ReadOnlySpan<byte> Magic => "CVRL"u8;

    // Tests only: TLS to an IP address with a certificate made on the spot.
    internal static RemoteCertificateValidationCallback? TestCertificateCheck { get; set; }

    /// <summary>A room code as typed (any case, with or without the dash and spaces) in its plain form, or null when it cannot be one.</summary>
    public static string? NormalizeCode(string? typed)
    {
        if (typed is null)
        {
            return null;
        }

        var b = new StringBuilder(CodeLength);
        foreach (char c in typed)
        {
            if (c is '-' or ' ')
            {
                continue;
            }

            char u = char.ToUpperInvariant(c);
            if (CodeLetters.IndexOf(u, StringComparison.Ordinal) < 0 || b.Length == CodeLength)
            {
                return null;
            }

            b.Append(u);
        }

        return b.Length == CodeLength ? b.ToString() : null;
    }

    /// <summary>A plain room code as people read it: <c>K7MQ4D</c> → <c>K7M-Q4D</c>.</summary>
    public static string ShowCode(string code) => code.Length == CodeLength ? code[..3] + "-" + code[3..] : code;

    /// <summary>
    /// True when connections to <paramref name="host"/> use TLS: a domain name (with a dot, e.g. <c>civsim.example.com</c>). Not for an
    /// IP address, <c>localhost</c> or a name without a dot (another container in the same Docker network, e.g. <c>relay</c>).
    /// </summary>
    public static bool UsesTls(string host) =>
        !IPAddress.TryParse(host.Trim('[', ']'), out _) && host.Trim().TrimEnd('.').Contains('.', StringComparison.Ordinal);

    /// <summary>
    /// Connects to a relay (TLS for an address with a name) and sends a greeting. Throws <see cref="RelayException"/> when the relay
    /// cannot be reached or its certificate is wrong.
    /// </summary>
    public static (Socket Socket, Stream Stream) Connect(string host, int port, TimeSpan timeout, byte kind, params string[] texts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        Stream? stream = null;
        try
        {
            using (var cts = new CancellationTokenSource(timeout))
            {
                socket.ConnectAsync(host, port, cts.Token).AsTask().GetAwaiter().GetResult();
            }

            stream = new NetworkStream(socket, ownsSocket: false);
            socket.ReceiveTimeout = (int)GreetingTimeout.TotalMilliseconds;
            socket.SendTimeout = (int)GreetingTimeout.TotalMilliseconds;
            if (UsesTls(host) || TestCertificateCheck is not null)
            {
                var tls = new SslStream(stream, leaveInnerStreamOpen: false, TestCertificateCheck);
                stream = tls;
                tls.AuthenticateAsClient(host);
            }

            WriteGreeting(stream, kind, texts);
            return (socket, stream);
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or IOException or System.Security.Authentication.AuthenticationException)
        {
            stream?.Dispose();
            socket.Dispose();
            string why = e switch
            {
                OperationCanceledException => "no answer",
                System.Security.Authentication.AuthenticationException => "its certificate is not valid for this address (" + e.Message + ")",
                _ => e.Message,
            };
            throw new RelayException($"Could not reach the relay {host}:{port}: {why}", e);
        }
    }

    /// <summary>Writes a greeting.</summary>
    public static void WriteGreeting(Stream stream, byte kind, params string[] texts)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var bytes = new MemoryStream();
        bytes.Write(Magic);
        bytes.WriteByte(Version);
        bytes.WriteByte(kind);
        bytes.WriteByte((byte)texts.Length);
        foreach (string t in texts)
        {
            WriteText(bytes, t);
        }

        stream.Write(bytes.GetBuffer(), 0, (int)bytes.Length);
        stream.Flush();
    }

    /// <summary>
    /// Reads a greeting: its kind and texts. Throws <see cref="RelayException"/> when it is not one (another program, another version,
    /// a text too long).
    /// </summary>
    public static (byte Kind, string[] Texts) ReadGreeting(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> head = stackalloc byte[7];
        ReadExactly(stream, head);
        if (!head[..4].SequenceEqual(Magic))
        {
            throw new RelayException("This is not a CivSim relay greeting.");
        }

        if (head[4] != Version)
        {
            throw new RelayException($"The relay speaks version {Version}; the other side speaks {head[4]}. Update the game or the relay.");
        }

        int count = head[6];
        if (count > 8)
        {
            throw new RelayException("Too many texts in the greeting.");
        }

        string[] texts = new string[count];
        for (int i = 0; i < count; i++)
        {
            texts[i] = ReadText(stream);
        }

        return (head[5], texts);
    }

    /// <summary>Writes an answer: "yes" with texts, or "no" with the reason.</summary>
    public static void WriteAnswer(Stream stream, bool ok, params string[] texts)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var bytes = new MemoryStream();
        bytes.WriteByte(ok ? (byte)0 : (byte)1);
        bytes.WriteByte((byte)texts.Length);
        foreach (string t in texts)
        {
            WriteText(bytes, t);
        }

        stream.Write(bytes.GetBuffer(), 0, (int)bytes.Length);
        stream.Flush();
    }

    /// <summary>Reads an answer; a "no" throws <see cref="RelayException"/> with the relay's reason.</summary>
    public static string[] ReadAnswer(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> head = stackalloc byte[2];
        try
        {
            ReadExactly(stream, head);
        }
        catch (IOException e)
        {
            throw new RelayException("The relay closed the connection without an answer.", e);
        }

        if (head[1] > 8)
        {
            throw new RelayException("The relay's answer could not be read.");
        }

        string[] texts = new string[head[1]];
        for (int i = 0; i < texts.Length; i++)
        {
            texts[i] = ReadText(stream);
        }

        if (head[0] != 0)
        {
            throw new RelayException(texts.Length > 0 ? texts[0] : "The relay said no.");
        }

        return texts;
    }

    /// <summary>Writes one message of the control line: a kind and a number (a ticket).</summary>
    public static void WriteControl(Stream stream, byte kind, uint value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> b = stackalloc byte[5];
        b[0] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(b[1..], value);
        stream.Write(b);
        stream.Flush();
    }

    /// <summary>Reads one message of the control line.</summary>
    public static (byte Kind, uint Value) ReadControl(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> b = stackalloc byte[5];
        ReadExactly(stream, b);
        return (b[0], BinaryPrimitives.ReadUInt32LittleEndian(b[1..]));
    }

    private static void WriteText(Stream to, string text)
    {
        byte[] t = Encoding.UTF8.GetBytes(text ?? "");
        if (t.Length > MaxText)
        {
            throw new ArgumentException($"A relay text has at most {MaxText} bytes.", nameof(text));
        }

        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(length, (ushort)t.Length);
        to.Write(length);
        to.Write(t);
    }

    private static string ReadText(Stream from)
    {
        Span<byte> length = stackalloc byte[2];
        ReadExactly(from, length);
        int n = BinaryPrimitives.ReadUInt16LittleEndian(length);
        if (n > MaxText)
        {
            throw new RelayException($"A text of {n} bytes is over the limit of {MaxText}.");
        }

        byte[] t = new byte[n];
        ReadExactly(from, t);
        return Encoding.UTF8.GetString(t);
    }

    private static void ReadExactly(Stream from, Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = from.Read(buffer[read..]);
            if (n <= 0)
            {
                throw new IOException("The connection closed in the middle of the greeting.");
            }

            read += n;
        }
    }
}

/// <summary>The relay said no, or could not be reached; the message says why in words for the player.</summary>
public sealed class RelayException : Exception
{
    /// <summary>Creates the exception.</summary>
    public RelayException()
    {
    }

    /// <summary>Creates the exception with a reason.</summary>
    public RelayException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a reason and the error behind it.</summary>
    public RelayException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
