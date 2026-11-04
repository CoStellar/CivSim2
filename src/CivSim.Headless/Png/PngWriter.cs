using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CivSim.Headless.Png;

/// <summary>Minimal PNG encoder (8-bit RGB, no filtering). Enough for map previews without extra packages.</summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Writes <paramref name="image"/> as a PNG file, creating the folder if needed.</summary>
    public static void Save(RgbImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        using FileStream fs = File.Create(path);
        Write(image, fs);
    }

    /// <summary>Writes <paramref name="image"/> as PNG to a stream.</summary>
    public static void Write(RgbImage image, Stream output)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(output);
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), image.Height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // colour type: RGB
        WriteChunk(output, "IHDR", ihdr);

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            int stride = image.Width * 3;
            for (int y = 0; y < image.Height; y++)
            {
                z.WriteByte(0);
                z.Write(image.Pixels, y * stride, stride);
            }
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        output.Write(len);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        output.Write(len);
    }

    private static uint Crc(uint crc, byte[] data)
    {
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

/// <summary>Simple RGB bitmap.</summary>
public sealed class RgbImage
{
    /// <summary>Creates a black image.</summary>
    public RgbImage(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 3];
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Row-major RGB bytes.</summary>
    public byte[] Pixels { get; }

    /// <summary>Sets a pixel (ignored outside the image). Colour is 0xRRGGBB.</summary>
    public void Set(int x, int y, uint rgb)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }

        int i = ((y * Width) + x) * 3;
        Pixels[i] = (byte)(rgb >> 16);
        Pixels[i + 1] = (byte)(rgb >> 8);
        Pixels[i + 2] = (byte)rgb;
    }
}
