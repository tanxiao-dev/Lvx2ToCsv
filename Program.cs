using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Lvx2ToCsv;

internal static class Program
{
    // This converter currently targets LVX2 v2 recordings from Livox Viewer 2
    // containing Mid-360 Cartesian 32-bit point data (data type 1).
    private const int PublicHeaderSize = 24;
    private const int PrivateHeaderSize = 5;
    private const int DeviceInfoSizeV2 = 63;
    private const int FrameHeaderSize = 24;
    private const int PacketHeaderSize = 27;
    private const int Cartesian32PointSize = 14;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 1 && args[0].Equals("--version", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Lvx2ToCsv v1.0.0");
            Console.WriteLine("LVX2 to CSV Converter for Livox Mid-360");
            Console.WriteLine("Author: Tan Xiao");
            Console.WriteLine("Copyright (c) 2026 Tan Xiao");
            return 0;
        }

        if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        Console.WriteLine("====================================");
        Console.WriteLine(" Lvx2ToCsv v1.0.0");
        Console.WriteLine(" Author: Tan Xiao");
        Console.WriteLine("====================================");
        Console.WriteLine();

        try
        {
            string input = args[0].Trim('"');
            string? output = null;
            int? minReflectivity = null;
            bool skipZero = false;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o" or "--output":
                        if (++i >= args.Length) throw new ArgumentException("Missing value after --output.");
                        output = args[i].Trim('"');
                        break;
                    case "--min-reflectivity":
                        if (++i >= args.Length || !int.TryParse(args[i], out int r) || r < 0 || r > 255)
                            throw new ArgumentException("--min-reflectivity must be an integer from 0 to 255.");
                        minReflectivity = r;
                        break;
                    case "--skip-zero":
                        skipZero = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {args[i]}");
                }
            }

            if (!File.Exists(input))
                throw new FileNotFoundException("Input file not found.", input);
            if (!string.Equals(Path.GetExtension(input), ".lvx2", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Input file must have the .lvx2 extension.");

            output ??= Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(input))!,
                Path.GetFileNameWithoutExtension(input) +
                (minReflectivity.HasValue ? $"_ref_ge_{minReflectivity.Value}" : "") +
                ".csv");

            var result = ConvertFile(input, output, minReflectivity, skipZero);

            Console.WriteLine();
            Console.WriteLine("Conversion complete.");
            Console.WriteLine($"Frames:       {result.Frames:N0}");
            Console.WriteLine($"Packets:      {result.Packets:N0}");
            Console.WriteLine($"Points read:  {result.PointsRead:N0}");
            Console.WriteLine($"Points saved: {result.PointsSaved:N0}");
            Console.WriteLine($"Output:       {Path.GetFullPath(output)}");
            Console.WriteLine();
            Console.WriteLine("------------------------------------");
            Console.WriteLine(" Converted by Lvx2ToCsv v1.0.0");
            Console.WriteLine(" Author: Tan Xiao");
            Console.WriteLine("------------------------------------");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    private static ConvertResult ConvertFile(string input, string output, int? minReflectivity, bool skipZero)
    {
        using FileStream fs = new(input, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        using BinaryReader br = new(fs);

        if (fs.Length < PublicHeaderSize + PrivateHeaderSize)
            throw new InvalidDataException("File is too small to be a valid LVX2 file.");

        byte[] publicHeader = br.ReadBytes(PublicHeaderSize);
        string signature = Encoding.ASCII.GetString(publicHeader, 0, 16).TrimEnd('\0');
        if (!signature.StartsWith("livox_tech", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid Livox signature.");

        // Version bytes live at offsets 16..19 in the public header.
        byte[] version = publicHeader[16..20];
        if (version[0] != 2)
            throw new NotSupportedException($"This tool currently supports LVX2 v2.x recordings. File version starts with {version[0]}.");

        uint frameDurationMs = br.ReadUInt32();
        byte deviceCount = br.ReadByte();
        if (deviceCount == 0)
            throw new InvalidDataException("LVX2 reports zero devices.");

        long deviceInfoBytes = (long)DeviceInfoSizeV2 * deviceCount;
        if (fs.Position + deviceInfoBytes > fs.Length)
            throw new InvalidDataException("Device information block is truncated.");
        fs.Position += deviceInfoBytes;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using StreamWriter sw = new(output, false, new UTF8Encoding(true), 1 << 20);
        sw.WriteLine("FrameIndex,PacketTimestampNs,UdpCounter,PointIndexInPacket,LidarIP,X_m,Y_m,Z_m,Reflectivity,Tag");

        long frames = 0, packets = 0, pointsRead = 0, pointsSaved = 0;
        byte[] frameHeader = new byte[FrameHeaderSize];
        byte[] packetHeader = new byte[PacketHeaderSize];
        byte[] point = new byte[Cartesian32PointSize];

        while (fs.Position + FrameHeaderSize <= fs.Length)
        {
            long frameStart = fs.Position;
            ReadExact(fs, frameHeader);
            ulong currentOffset = BinaryPrimitives.ReadUInt64LittleEndian(frameHeader.AsSpan(0, 8));
            ulong nextOffset = BinaryPrimitives.ReadUInt64LittleEndian(frameHeader.AsSpan(8, 8));
            ulong frameIndex = BinaryPrimitives.ReadUInt64LittleEndian(frameHeader.AsSpan(16, 8));

            if (currentOffset != (ulong)frameStart || nextOffset <= currentOffset || nextOffset > (ulong)fs.Length)
                throw new InvalidDataException($"Invalid frame header at byte {frameStart}.");

            frames++;
            while ((ulong)fs.Position < nextOffset)
            {
                if ((ulong)(fs.Position + PacketHeaderSize) > nextOffset)
                    throw new InvalidDataException($"Truncated packet header in frame {frameIndex}.");

                ReadExact(fs, packetHeader);
                uint lidarId = BinaryPrimitives.ReadUInt32LittleEndian(packetHeader.AsSpan(1, 4));
                ulong packetTimestampNs = BinaryPrimitives.ReadUInt64LittleEndian(packetHeader.AsSpan(7, 8));
                ushort udpCounter = BinaryPrimitives.ReadUInt16LittleEndian(packetHeader.AsSpan(15, 2));
                byte dataType = packetHeader[17];
                uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(packetHeader.AsSpan(18, 4));
                packets++;

                if (dataType != 1)
                    throw new NotSupportedException($"Encountered data type {dataType}. This build supports Mid-360 Cartesian 32-bit data type 1 only.");
                if (payloadLength % Cartesian32PointSize != 0)
                    throw new InvalidDataException($"Payload length {payloadLength} is not divisible by {Cartesian32PointSize}.");
                if ((ulong)fs.Position + payloadLength > nextOffset)
                    throw new InvalidDataException($"Packet payload exceeds frame boundary in frame {frameIndex}.");

                int pointCount = checked((int)(payloadLength / Cartesian32PointSize));
                string lidarIp = LidarIdToIp(lidarId);

                for (int i = 0; i < pointCount; i++)
                {
                    ReadExact(fs, point);
                    int xMm = BinaryPrimitives.ReadInt32LittleEndian(point.AsSpan(0, 4));
                    int yMm = BinaryPrimitives.ReadInt32LittleEndian(point.AsSpan(4, 4));
                    int zMm = BinaryPrimitives.ReadInt32LittleEndian(point.AsSpan(8, 4));
                    byte reflectivity = point[12];
                    byte tag = point[13];
                    pointsRead++;

                    if (skipZero && xMm == 0 && yMm == 0 && zMm == 0) continue;
                    if (minReflectivity.HasValue && reflectivity < minReflectivity.Value) continue;

                    sw.Write(frameIndex.ToString(CultureInfo.InvariantCulture)); sw.Write(',');
                    sw.Write(packetTimestampNs.ToString(CultureInfo.InvariantCulture)); sw.Write(',');
                    sw.Write(udpCounter.ToString(CultureInfo.InvariantCulture)); sw.Write(',');
                    sw.Write(i.ToString(CultureInfo.InvariantCulture)); sw.Write(',');
                    sw.Write(lidarIp); sw.Write(',');
                    sw.Write((xMm / 1000.0).ToString("0.000", CultureInfo.InvariantCulture)); sw.Write(',');
                    sw.Write((yMm / 1000.0).ToString("0.000", CultureInfo.InvariantCulture)); sw.Write(',');
                    sw.Write((zMm / 1000.0).ToString("0.000", CultureInfo.InvariantCulture)); sw.Write(',');
                    sw.Write(reflectivity.ToString(CultureInfo.InvariantCulture)); sw.Write(',');
                    sw.WriteLine(tag.ToString(CultureInfo.InvariantCulture));
                    pointsSaved++;
                }
            }

            fs.Position = (long)nextOffset;
        }

        return new ConvertResult(frames, packets, pointsRead, pointsSaved, frameDurationMs, deviceCount);
    }

    private static void ReadExact(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer, total, buffer.Length - total);
            if (n == 0) throw new EndOfStreamException("Unexpected end of file.");
            total += n;
        }
    }

    private static string LidarIdToIp(uint lidarId)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, lidarId);
        return $"{b[0]}.{b[1]}.{b[2]}.{b[3]}";
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Lvx2ToCsv v1.0.0");
        Console.WriteLine("LVX2 to CSV Converter for Livox Mid-360");
        Console.WriteLine();
        Console.WriteLine("Author:");
        Console.WriteLine("  Tan Xiao");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Lvx2ToCsv.exe input.lvx2");
        Console.WriteLine("  Lvx2ToCsv.exe input.lvx2 --min-reflectivity 200");
        Console.WriteLine("  Lvx2ToCsv.exe input.lvx2 --skip-zero");
        Console.WriteLine("  Lvx2ToCsv.exe input.lvx2 -o output.csv");
        Console.WriteLine("  Lvx2ToCsv.exe --version");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <file>          Specify output CSV file.");
        Console.WriteLine("  --min-reflectivity <0-255>   Export only points at or above the threshold.");
        Console.WriteLine("  --skip-zero                  Skip points whose X, Y and Z are all zero.");
        Console.WriteLine("  -h, --help                   Show this help.");
        Console.WriteLine("  --version                    Show version and author information.");
        Console.WriteLine();
        Console.WriteLine("Tip:");
        Console.WriteLine("  You can drag an .lvx2 file directly onto Lvx2ToCsv.exe.");
        Console.WriteLine();
        Console.WriteLine("Copyright (c) 2026 Tan Xiao");
    }

    private sealed record ConvertResult(long Frames, long Packets, long PointsRead, long PointsSaved, uint FrameDurationMs, byte DeviceCount);
}
