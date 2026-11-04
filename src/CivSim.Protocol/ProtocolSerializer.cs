using System;
using System.Buffers.Binary;
using CivSim.Protocol.Messages;
using MessagePack;

namespace CivSim.Protocol;

/// <summary>
/// Turns messages into bytes and back. Every frame starts with the 4-byte protocol version (little-endian),
/// followed by the MessagePack body.
/// </summary>
public static class ProtocolSerializer
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    /// <summary>Serializes a message into a frame.</summary>
    public static byte[] Serialize(IProtocolMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        byte[] body = MessagePackSerializer.Serialize(message, Options);
        byte[] frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, ProtocolInfo.Version);
        body.CopyTo(frame, 4);
        return frame;
    }

    /// <summary>Reads the protocol version of a frame without decoding the body.</summary>
    public static int ReadVersion(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4)
        {
            throw new ProtocolException("Frame too short.");
        }

        return BinaryPrimitives.ReadInt32LittleEndian(frame);
    }

    /// <summary>Deserializes a frame. Throws <see cref="ProtocolException"/> on a version mismatch or bad data.</summary>
    public static IProtocolMessage Deserialize(ReadOnlyMemory<byte> frame)
    {
        int version = ReadVersion(frame.Span);
        if (version != ProtocolInfo.Version)
        {
            throw new ProtocolException($"Protocol version {version} is not supported (expected {ProtocolInfo.Version}).");
        }

        try
        {
            return MessagePackSerializer.Deserialize<IProtocolMessage>(frame[4..], Options)
                ?? throw new ProtocolException("Empty message.");
        }
        catch (MessagePackSerializationException ex)
        {
            throw new ProtocolException("Malformed message.", ex);
        }
    }
}

/// <summary>A frame could not be decoded.</summary>
public sealed class ProtocolException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner cause.</summary>
    public ProtocolException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
