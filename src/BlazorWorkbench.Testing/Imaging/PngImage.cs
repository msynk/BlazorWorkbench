using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace BlazorWorkbench.Testing.Imaging;

/// <summary>
/// Minimal, dependency-free PNG codec for screenshot comparison. Decodes non-interlaced 8-bit grayscale, RGB, palette,
/// grayscale+alpha and RGBA images (what browsers produce) and encodes RGBA.
/// </summary>
public sealed class PngImage
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public PngImage(int width, int height, byte[]? rgba = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Pixels = rgba ?? new byte[width * height * 4];
        if (Pixels.Length != width * height * 4)
            throw new ArgumentException("Pixel buffer size does not match the dimensions.", nameof(rgba));
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>RGBA pixels, row major, 4 bytes per pixel.</summary>
    public byte[] Pixels { get; }

    public static PngImage Load(string path) => Decode(File.ReadAllBytes(path));

    public static PngImage Decode(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG file.");

        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null, transparency = null;
        using var idat = new MemoryStream();
        var offset = 8;
        while (offset + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
            var type = Encoding.ASCII.GetString(data, offset + 4, 4);
            if (length < 0 || offset + 12 + length > data.Length)
                throw new InvalidDataException("Truncated PNG chunk.");
            var chunk = data.AsSpan(offset + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(chunk);
                    height = BinaryPrimitives.ReadInt32BigEndian(chunk[4..]);
                    bitDepth = chunk[8];
                    colorType = chunk[9];
                    interlace = chunk[12];
                    break;
                case "PLTE":
                    palette = chunk.ToArray();
                    break;
                case "tRNS":
                    transparency = chunk.ToArray();
                    break;
                case "IDAT":
                    idat.Write(chunk);
                    break;
                case "IEND":
                    offset = data.Length;
                    continue;
            }

            offset += 12 + length;
        }

        if (width <= 0 || height <= 0)
            throw new InvalidDataException("PNG has no IHDR chunk.");
        if (bitDepth != 8 || interlace != 0)
            throw new NotSupportedException($"Only non-interlaced 8-bit PNG images are supported (bit depth {bitDepth}, interlace {interlace}).");

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new NotSupportedException($"PNG color type {colorType} is not supported."),
        };

        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        z.ReadExactly(raw);

        var image = new PngImage(width, height);
        var previous = new byte[stride];
        var current = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            Array.Copy(raw, y * (stride + 1) + 1, current, 0, stride);
            Unfilter(filter, current, previous, channels);

            var o = y * width * 4;
            for (var x = 0; x < width; x++, o += 4)
            {
                switch (colorType)
                {
                    case 0:
                        var g = current[x];
                        image.Pixels[o] = image.Pixels[o + 1] = image.Pixels[o + 2] = g;
                        image.Pixels[o + 3] = 255;
                        break;
                    case 2:
                        image.Pixels[o] = current[x * 3];
                        image.Pixels[o + 1] = current[x * 3 + 1];
                        image.Pixels[o + 2] = current[x * 3 + 2];
                        image.Pixels[o + 3] = 255;
                        break;
                    case 3:
                        var index = current[x];
                        if (palette is null || index * 3 + 2 >= palette.Length)
                            throw new InvalidDataException("Invalid palette index.");
                        image.Pixels[o] = palette[index * 3];
                        image.Pixels[o + 1] = palette[index * 3 + 1];
                        image.Pixels[o + 2] = palette[index * 3 + 2];
                        image.Pixels[o + 3] = transparency is not null && index < transparency.Length ? transparency[index] : (byte)255;
                        break;
                    case 4:
                        image.Pixels[o] = image.Pixels[o + 1] = image.Pixels[o + 2] = current[x * 2];
                        image.Pixels[o + 3] = current[x * 2 + 1];
                        break;
                    default:
                        Array.Copy(current, x * 4, image.Pixels, o, 4);
                        break;
                }
            }

            (previous, current) = (current, previous);
        }

        return image;
    }

    private static void Unfilter(byte filter, byte[] row, byte[] previous, int bpp)
    {
        switch (filter)
        {
            case 0:
                return;
            case 1:
                for (var i = bpp; i < row.Length; i++) row[i] += row[i - bpp];
                return;
            case 2:
                for (var i = 0; i < row.Length; i++) row[i] += previous[i];
                return;
            case 3:
                for (var i = 0; i < row.Length; i++)
                    row[i] += (byte)(((i >= bpp ? row[i - bpp] : 0) + previous[i]) / 2);
                return;
            case 4:
                for (var i = 0; i < row.Length; i++)
                {
                    int a = i >= bpp ? row[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
                    int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    row[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                }

                return;
            default:
                throw new InvalidDataException($"Unknown PNG filter {filter}.");
        }
    }

    public byte[] Encode()
    {
        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], Height);
        header[8] = 8;   // bit depth
        header[9] = 6;   // RGBA
        header[10] = 0;  // compression
        header[11] = 0;  // filter
        header[12] = 0;  // no interlace
        WriteChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var stride = Width * 4;
            var line = new byte[stride + 1];
            for (var y = 0; y < Height; y++)
            {
                // Sub filter: cheap and compresses screenshots well.
                line[0] = 1;
                var o = y * stride;
                for (var i = 0; i < stride; i++)
                    line[i + 1] = (byte)(Pixels[o + i] - (i >= 4 ? Pixels[o + i - 4] : 0));
                z.Write(line);
            }
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, Encode());
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        stream.Write(buffer);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc32.Update(Crc32.Update(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        stream.Write(buffer);
    }

    private static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }

            return table;
        }
    }
}
