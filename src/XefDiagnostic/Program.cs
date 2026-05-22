using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace XefDiagnostic;

class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            Console.WriteLine("XEF 文件诊断工具 v1.0");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine("用法: XefDiagnostic <文件路径> [文件路径2] [文件路径3] ...");
            Console.WriteLine();
            Console.WriteLine("示例:");
            Console.WriteLine(@"  XefDiagnostic ""C:\path\to\file.xef""");
            Console.WriteLine(@"  XefDiagnostic file1.xef file2.xef file3.xef");
            Console.WriteLine();
            Console.WriteLine("功能:");
            Console.WriteLine("  - 验证XEF文件完整性");
            Console.WriteLine("  - 统计各类型帧的数量");
            Console.WriteLine("  - 定位损坏区域位置");
            Console.WriteLine("  - 输出详细的诊断报告");
            return;
        }

        Console.WriteLine("XEF 文件诊断工具 v1.0");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        int successCount = 0;
        int failCount = 0;

        foreach (var filePath in args)
        {
            if (!File.Exists(filePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ 错误: 文件不存在 - {filePath}");
                Console.ResetColor();
                failCount++;
                continue;
            }

            try
            {
                DiagnoseFile(filePath);
                successCount++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ 诊断失败: {ex.Message}");
                Console.ResetColor();
                failCount++;
            }
            Console.WriteLine();
        }

        if (args.Length > 1)
        {
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"诊断完成: {successCount} 成功, {failCount} 失败");
        }
    }

    static void DiagnoseFile(string filePath)
    {
        Console.WriteLine(new string('=', 80));
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"诊断文件: {Path.GetFileName(filePath)}");
        Console.ResetColor();
        Console.WriteLine(new string('=', 80));

        var fileInfo = new FileInfo(filePath);
        Console.WriteLine($"文件大小: {fileInfo.Length / (1024.0 * 1024.0 * 1024.0):F2} GB ({fileInfo.Length:N0} bytes)");
        Console.WriteLine();

        using var reader = new DiagnosticXefReader(filePath);
        reader.Diagnose();
    }
}

/// <summary>
/// 带诊断功能的XEF读取器
/// </summary>
class DiagnosticXefReader : IDisposable
{
    // Stream type GUIDs
    private static readonly Guid GUID_UNCOMPRESSED_COLOR = new("2ba0d67d-be11-4534-9444-3fb21ae0f08b");
    private static readonly Guid GUID_COMPRESSED_COLOR = new("0a3914dc-3b16-11e1-aac3-001e4fd58c0f");
    private static readonly Guid GUID_DEPTH = new("0a3914d6-3b16-11e1-aac3-001e4fd58c0f");
    private static readonly Guid GUID_IR = new("0a3914d7-3b16-11e1-aac3-001e4fd58c0f");
    private static readonly Guid GUID_RAW_IR = new("0a3914e2-3b16-11e1-aac3-001e4fd58c0f");
    private static readonly Guid GUID_LONG_EXPOSURE_IR = new("7e06d98e-d271-4a1f-9bfd-6648a700db75");
    private static readonly Guid GUID_BODY = new("a0c45179-5168-4875-a75c-f8f1760f637c");
    private static readonly Guid GUID_BODY_INDEX = new("df82ffac-b533-4438-954a-686a1e20f4aa");
    private static readonly Guid GUID_AUDIO = new("787c7abd-9f6e-4a85-8d67-6365ff80cc69");
    private static readonly Guid GUID_PROPERTIES = new("8083a32f-d7b4-449b-99b9-44c6fcd97570");

    // Data constants
    private const short FLAG_COMPRESSED = 0x0001;
    private const short EVENT_UNKRECORD_INDEX = -1;
    private const int EVENT_DEFAULT_TAG_SIZE = 24;
    private const int STREAM_INDEX_OFFSET = 0;
    private const int STREAM_FLAGS_OFFSET = 2;
    private const int STREAM_TYPID_OFFSET = 4;
    private const int STREAM_TYPID_SIZE = 16;
    private const int STREAM_NAME_SIZE = 256;
    private const int STREAM_TAGSIZE_OFFSET = 258;
    private const int STREAM_SEMID_OFFSET = 82;
    private const int STREAM_SEMID_SIZE = 16;
    private const int ARC_STREAM_EXTRA_UNK_SIZE = 8;

    private readonly BinaryReader _reader;
    private readonly Dictionary<short, XefStreamInfo> _streams = new();
    private int _totalReportedStreams;
    private long _eventStartAddress;
    private bool _endOfStream;
    private readonly string _filePath;

    // 诊断统计
    private int _colorFrames;
    private int _depthFrames;
    private int _irFrames;
    private int _unknownFrames;
    private int _skippedFrames;
    private int _errorCount;
    private long _lastValidPosition;
    private int _scanRecoveryAttempts;
    private int _scanRecoverySuccesses;
    private DateTime _startTime;

    public DiagnosticXefReader(string path)
    {
        _filePath = path;
        _reader = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        _startTime = DateTime.Now;
    }

    public void Dispose()
    {
        _reader?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Diagnose()
    {
        Console.WriteLine("【阶段1】读取文件头...");
        if (!ReadHeader())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ✗ 文件头无效，不是有效的XEF文件");
            Console.ResetColor();
            return;
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  ✓ 文件头有效");
        Console.ResetColor();
        Console.WriteLine($"  报告的流数量: {_totalReportedStreams}");
        Console.WriteLine($"  事件数据起始位置: 0x{_eventStartAddress:X}");
        Console.WriteLine();

        Console.WriteLine("【阶段2】扫描流定义...");
        ScanStreams();
        Console.WriteLine($"  发现 {_streams.Count} 个流定义:");
        foreach (var (index, stream) in _streams.OrderBy(x => x.Key))
        {
            var type = ClassifyStream(stream.DataTypeId);
            var color = type switch
            {
                StreamType.Color => ConsoleColor.Yellow,
                StreamType.Depth => ConsoleColor.Blue,
                StreamType.Ir => ConsoleColor.Magenta,
                _ => ConsoleColor.Gray
            };
            Console.ForegroundColor = color;
            Console.WriteLine($"    [{index}] {stream.DataTypeName} ({type}) {(stream.IsCompressed ? "[压缩]" : "")}");
            Console.ResetColor();
        }
        Console.WriteLine();

        Console.WriteLine("【阶段3】逐帧读取诊断...");
        Console.WriteLine("  (每100帧输出一次进度)");
        Console.WriteLine();

        _reader.BaseStream.Position = _eventStartAddress;
        _endOfStream = false;
        _streams.Clear();

        int frameCount = 0;
        FrameInfo? frame;

        while ((frame = ReadNextEvent()) != null)
        {
            frameCount++;
            _lastValidPosition = _reader.BaseStream.Position;

            switch (frame.StreamType)
            {
                case StreamType.Color:
                    _colorFrames++;
                    break;
                case StreamType.Depth:
                    _depthFrames++;
                    break;
                case StreamType.Ir:
                    _irFrames++;
                    break;
                default:
                    _unknownFrames++;
                    break;
            }

            if (frameCount % 100 == 0)
            {
                var progress = (_reader.BaseStream.Position - _eventStartAddress) /
                              (double)(_reader.BaseStream.Length - _eventStartAddress) * 100;
                var elapsed = DateTime.Now - _startTime;
                Console.WriteLine($"  已处理 {frameCount} 帧 | 进度: {progress:F1}% | " +
                                $"彩色: {_colorFrames} 深度: {_depthFrames} 红外: {_irFrames} | " +
                                $"位置: 0x{_reader.BaseStream.Position:X} | " +
                                $"用时: {elapsed:mm\\:ss}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("【阶段4】诊断结果汇总");
        Console.WriteLine(new string('-', 80));

        var totalElapsed = DateTime.Now - _startTime;
        var readBytes = _lastValidPosition - _eventStartAddress;
        var totalBytes = _reader.BaseStream.Length - _eventStartAddress;
        var readPercentage = (double)readBytes / totalBytes * 100;

        Console.WriteLine($"总帧数统计:");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  彩色帧: {_colorFrames}");
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"  深度帧: {_depthFrames}");
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"  红外帧:  {_irFrames}");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"  未知帧: {_unknownFrames}");
        Console.ResetColor();
        Console.WriteLine($"  跳过的帧: {_skippedFrames}");
        Console.WriteLine($"  总计: {_colorFrames + _depthFrames + _irFrames + _unknownFrames} 帧");
        Console.WriteLine();

        Console.WriteLine($"数据读取情况:");
        Console.WriteLine($"  最后有效位置: 0x{_lastValidPosition:X}");
        Console.WriteLine($"  已读取数据: {readBytes / (1024.0 * 1024.0):F1} MB");
        Console.WriteLine($"  总数据大小: {totalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB");
        Console.WriteLine($"  读取进度: {readPercentage:F2}%");
        Console.WriteLine();

        Console.WriteLine($"错误恢复统计:");
        Console.WriteLine($"  错误发生次数: {_errorCount}");
        Console.WriteLine($"  扫描恢复尝试: {_scanRecoveryAttempts}");
        Console.WriteLine($"  扫描恢复成功: {_scanRecoverySuccesses}");
        Console.WriteLine();

        Console.WriteLine($"性能统计:");
        Console.WriteLine($"  总用时: {totalElapsed:hh\\:mm\\:ss\\.ff}");
        if (totalElapsed.TotalSeconds > 0)
            Console.WriteLine($"  读取速度: {readBytes / totalElapsed.TotalSeconds / (1024 * 1024):F1} MB/s");
        Console.WriteLine();

        // 诊断结论
        Console.WriteLine("【诊断结论】");
        Console.WriteLine(new string('-', 80));

        if (readPercentage < 1.0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("⚠ 警告: 文件读取在非常早期就终止了！");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("可能的原因:");
            Console.WriteLine("  1. 文件存在大面积损坏（最可能）");
            Console.WriteLine("  2. 文件格式不兼容");
            Console.WriteLine("  3. 文件截断或不完整");
            Console.WriteLine();
            Console.WriteLine("建议:");
            Console.WriteLine("  - 使用十六进制编辑器检查文件");
            Console.WriteLine("  - 验证文件是否完整下载/传输");
            Console.WriteLine("  - 尝试从备份恢复文件");
        }
        else if (readPercentage < 90.0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠ 警告: 文件读取提前终止");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine($"只读取了 {readPercentage:F1}% 的数据");
            Console.WriteLine("文件中存在损坏区域，导致读取提前终止。");
            Console.WriteLine();
            Console.WriteLine("建议:");
            Console.WriteLine($"  - 检查最后有效位置 0x{_lastValidPosition:X} 之后的数据");
            Console.WriteLine("  - 考虑使用其他工具尝试恢复文件");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ 文件读取完成，数据基本完整");
            Console.ResetColor();

            if (_errorCount > 0)
            {
                Console.WriteLine($"  注意: 存在 {_errorCount} 个错误，但已成功恢复");
            }
        }
    }

    private bool ReadHeader()
    {
        try
        {
            byte[] signature = _reader.ReadBytes(8);
            string sig = Encoding.ASCII.GetString(signature);
            Console.WriteLine($"  文件签名: {sig}");

            if (!sig.StartsWith("EVENTS1"))
                return false;

            _reader.ReadUInt32();
            int streamCountReported = _reader.ReadInt32();
            if (streamCountReported == 0)
                streamCountReported = int.MaxValue;
            _totalReportedStreams = streamCountReported - 1;
            _reader.ReadInt64();

            _eventStartAddress = _reader.BaseStream.Position;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  读取文件头时出错: {ex.Message}");
            return false;
        }
    }

    private void ScanStreams()
    {
        _reader.BaseStream.Position = _eventStartAddress;
        _endOfStream = false;
        _streams.Clear();

        while (!_endOfStream)
        {
            PeekEventKey(out short streamIndex, out short streamFlags);
            if (streamIndex == EVENT_UNKRECORD_INDEX) { SkipUnknownEvent(); continue; }
            if (streamIndex == _totalReportedStreams + 1) { _endOfStream = true; break; }
            if (!_streams.ContainsKey(streamIndex))
            {
                var stream = ReadStreamDescription();
                _streams[streamIndex] = stream;
            }
            else break;
        }
    }

    private bool IsValidStreamIndex(short index)
    {
        return (index > 0 && index <= _totalReportedStreams)
            || index == EVENT_UNKRECORD_INDEX
            || index == _totalReportedStreams + 1;
    }

    private void PeekEventKey(out short streamIndex, out short streamFlags)
    {
        streamIndex = _reader.ReadInt16();
        streamFlags = _reader.ReadInt16();
        _reader.BaseStream.Position -= 2 * sizeof(short);
    }

    private FrameInfo? ReadNextEvent()
    {
        while (!_endOfStream)
        {
            try
            {
                PeekEventKey(out short streamIndex, out short streamFlags);
                if (streamIndex == EVENT_UNKRECORD_INDEX) { SkipUnknownEvent(); continue; }
                if (streamIndex == _totalReportedStreams + 1) { _endOfStream = true; return null; }
                if (!_streams.ContainsKey(streamIndex))
                {
                    long posBefore = _reader.BaseStream.Position;
                    try
                    {
                        _streams[streamIndex] = ReadStreamDescription();
                    }
                    catch (InvalidDataException ex)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"  ⚠ 流定义读取失败 @ 0x{posBefore:X}: {ex.Message}");
                        Console.ResetColor();
                        _reader.BaseStream.Position = posBefore;
                        _errorCount++;
                        if (!ScanForNextValidEvent())
                        {
                            _endOfStream = true;
                            return null;
                        }
                    }
                    continue;
                }

                long dataPosBefore = _reader.BaseStream.Position;
                try
                {
                    var result = ReadDataEvent();
                    if (result != null) return result;
                }
                catch (InvalidDataException ex)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"  ⚠ 数据事件读取失败 @ 0x{dataPosBefore:X}: {ex.Message}");
                    Console.ResetColor();
                    _reader.BaseStream.Position = dataPosBefore;
                    _errorCount++;
                    if (!ScanForNextValidEvent())
                    {
                        _endOfStream = true;
                        return null;
                    }
                    continue;
                }

                // ReadDataEvent returned null
                if (!_endOfStream)
                {
                    _skippedFrames++;
                    _reader.BaseStream.Position = dataPosBefore;
                    if (!ScanForNextValidEvent())
                    {
                        _endOfStream = true;
                        return null;
                    }
                }
            }
            catch (EndOfStreamException)
            {
                _endOfStream = true;
                return null;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ 未预期的错误 @ 0x{_reader.BaseStream.Position:X}: {ex.Message}");
                Console.ResetColor();
                _errorCount++;
                _endOfStream = true;
                return null;
            }
        }
        return null;
    }

    private bool ScanForNextValidEvent()
    {
        _scanRecoveryAttempts++;
        long startPos = _reader.BaseStream.Position;
        long maxScan = Math.Min(startPos + 0x100000, _reader.BaseStream.Length - 48);

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"  → 扫描恢复: 从 0x{startPos:X} 开始，最大扫描到 0x{maxScan:X} (1MB范围)");
        Console.ResetColor();

        for (long pos = startPos + 4; pos < maxScan; pos++)
        {
            _reader.BaseStream.Position = pos;
            short testIndex = _reader.ReadInt16();
            short testFlags = _reader.ReadInt16();
            int testSize = _reader.ReadInt32();

            if (!IsValidStreamIndex(testIndex) || testFlags < 0 || testSize <= 0 || testSize > 50 * 1024 * 1024)
                continue;

            // Try different tag sizes
            int[] tagSizes = { 4, 24, 28 };
            foreach (int tagSize in tagSizes)
            {
                long nextPos = pos + 24 + tagSize + testSize;
                if (nextPos + 8 > _reader.BaseStream.Length) continue;

                _reader.BaseStream.Position = nextPos;
                short nextIndex = _reader.ReadInt16();
                short nextFlags = _reader.ReadInt16();
                int nextSize = _reader.ReadInt32();

                if (IsValidStreamIndex(nextIndex) && nextFlags >= 0 && nextSize > 0 && nextSize < 50 * 1024 * 1024)
                {
                    // Found valid position
                    _reader.BaseStream.Position = pos;
                    _scanRecoverySuccesses++;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✓ 扫描恢复成功: 找到有效位置 0x{pos:X} (偏移 {(pos - startPos):N0} bytes)");
                    Console.ResetColor();
                    return true;
                }
            }
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ✗ 扫描恢复失败: 在1MB范围内未找到有效事件");
        Console.ResetColor();
        return false;
    }

    private void SkipUnknownEvent()
    {
        _reader.ReadInt16(); _reader.ReadInt16();
        _reader.ReadInt32(); _reader.ReadInt64();
        _reader.ReadInt32(); _reader.ReadInt32();
        _reader.ReadBytes(0x6000);
        PeekEventKey(out short peekIndex, out _);
        if (!IsValidStreamIndex(peekIndex))
        {
            _reader.ReadBytes(0x1000);
            PeekEventKey(out peekIndex, out _);
            if (!IsValidStreamIndex(peekIndex))
                _reader.ReadBytes(0x5000);
        }
    }

    private XefStreamInfo ReadStreamDescription()
    {
        var nameEvent = ReadRawEvent();
        short streamIndex = BitConverter.ToInt16(nameEvent.TagData, STREAM_INDEX_OFFSET);
        short streamFlags = BitConverter.ToInt16(nameEvent.TagData, STREAM_FLAGS_OFFSET);
        bool compressed = (streamFlags & FLAG_COMPRESSED) == FLAG_COMPRESSED;

        byte[] guidBuf = new byte[STREAM_TYPID_SIZE];
        if (nameEvent.TagData.Length >= STREAM_TYPID_OFFSET + STREAM_TYPID_SIZE)
            Array.Copy(nameEvent.TagData, STREAM_TYPID_OFFSET, guidBuf, 0, STREAM_TYPID_SIZE);
        Guid dataTypeId = new Guid(guidBuf);

        int nameLen = Math.Min(STREAM_NAME_SIZE, nameEvent.EventData.Length);
        string dataTypeName = Encoding.Unicode.GetString(nameEvent.EventData, 0, nameLen).TrimEnd('\0');
        short tagSize = nameEvent.EventData.Length >= STREAM_TAGSIZE_OFFSET + sizeof(short)
            ? BitConverter.ToInt16(nameEvent.EventData, STREAM_TAGSIZE_OFFSET)
            : (short)EVENT_DEFAULT_TAG_SIZE;

        if (compressed) _reader.ReadBytes(ARC_STREAM_EXTRA_UNK_SIZE);

        ReadRawEvent(); // GUID event

        byte[] semanticBuf = new byte[STREAM_SEMID_SIZE];
        if (nameEvent.EventData.Length >= STREAM_SEMID_OFFSET + STREAM_SEMID_SIZE)
            Array.Copy(nameEvent.EventData, STREAM_SEMID_OFFSET, semanticBuf, 0, STREAM_SEMID_SIZE);
        Guid semanticId = new Guid(semanticBuf);

        return new XefStreamInfo(streamIndex, streamFlags, tagSize, dataTypeName, dataTypeId, semanticId);
    }

    private RawEvent ReadRawEvent()
    {
        short streamIndex = _reader.ReadInt16();
        short streamFlags = _reader.ReadInt16();
        int dataSize = _reader.ReadInt32();
        TimeSpan relativeTime = TimeSpan.FromTicks(_reader.ReadInt64());
        _reader.ReadUInt32();
        int fullDataSize = _reader.ReadInt32();

        if (dataSize < 0 || dataSize > 100 * 1024 * 1024)
            throw new InvalidDataException($"Invalid data size: {dataSize}");

        XefStreamInfo? evtStream = _streams.ContainsKey(streamIndex) ? _streams[streamIndex] : null;
        byte[] tagData = (evtStream != null && evtStream.TagSize > 0)
            ? _reader.ReadBytes(evtStream.TagSize)
            : _reader.ReadBytes(EVENT_DEFAULT_TAG_SIZE);
        byte[] eventData = _reader.ReadBytes(dataSize);

        if (_reader.BaseStream.Position >= _reader.BaseStream.Length) _endOfStream = true;
        return new RawEvent(evtStream, tagData, eventData, relativeTime, fullDataSize, dataSize);
    }

    private FrameInfo? ReadDataEvent()
    {
        try
        {
            short streamIndex = _reader.ReadInt16();
            short streamFlags = _reader.ReadInt16();
            XefStreamInfo? evtStream = _streams.ContainsKey(streamIndex) ? _streams[streamIndex] : null;
            int dataSize = _reader.ReadInt32();
            TimeSpan relativeTime = TimeSpan.FromTicks(_reader.ReadInt64());
            _reader.ReadUInt32();
            int fullDataSize = _reader.ReadInt32();

            if (dataSize < 0 || dataSize > 100 * 1024 * 1024)
                throw new InvalidDataException($"Invalid data size: {dataSize}");

            int frameIndex = 0;
            byte[] tagData = (evtStream != null && evtStream.TagSize > 0)
                ? _reader.ReadBytes(evtStream.TagSize)
                : _reader.ReadBytes(EVENT_DEFAULT_TAG_SIZE);
            if (evtStream != null && evtStream.TagSize == 4)
                frameIndex = BitConverter.ToInt32(tagData, 0);

            byte[] eventData = _reader.ReadBytes(dataSize);
            if (_reader.BaseStream.Position >= _reader.BaseStream.Length) _endOfStream = true;

            if (evtStream != null && evtStream.IsCompressed)
            {
                byte[] decompressed = DecompressZlib(eventData, fullDataSize);
                if (decompressed.Length < fullDataSize)
                    return null; // incomplete decompression
                eventData = decompressed;
            }

            StreamType streamType = evtStream != null ? ClassifyStream(evtStream.DataTypeId) : StreamType.Unknown;
            return new FrameInfo
            {
                FrameIndex = frameIndex,
                RelativeTime = relativeTime,
                StreamType = streamType,
                StreamName = evtStream?.DataTypeName ?? "Unknown",
                Data = eventData,
                DataSize = eventData.Length
            };
        }
        catch (IOException)
        {
            _endOfStream = true;
            return null;
        }
        catch (ArgumentException)
        {
            _endOfStream = true;
            return null;
        }
    }

    private static byte[] DecompressZlib(byte[] data, int uncompressedSize)
    {
        if (data.Length < 2 || data[0] != 0x78 || data[1] != 0x01)
            return data;
        byte[] result = new byte[uncompressedSize];
        using var ms = new MemoryStream(data, 2, data.Length - 2);
        using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
        int totalRead = 0;
        while (totalRead < uncompressedSize)
        {
            int read = deflate.Read(result, totalRead, uncompressedSize - totalRead);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead == uncompressedSize ? result : result.AsSpan(0, totalRead).ToArray();
    }

    public static StreamType ClassifyStream(Guid dataTypeId)
    {
        if (dataTypeId == GUID_UNCOMPRESSED_COLOR || dataTypeId == GUID_COMPRESSED_COLOR) return StreamType.Color;
        if (dataTypeId == GUID_DEPTH) return StreamType.Depth;
        if (dataTypeId == GUID_IR || dataTypeId == GUID_RAW_IR || dataTypeId == GUID_LONG_EXPOSURE_IR) return StreamType.Ir;
        if (dataTypeId == GUID_BODY) return StreamType.Body;
        if (dataTypeId == GUID_BODY_INDEX) return StreamType.BodyIndex;
        if (dataTypeId == GUID_AUDIO) return StreamType.Audio;
        return StreamType.Unknown;
    }

    private class RawEvent
    {
        public XefStreamInfo? Stream;
        public byte[] TagData;
        public byte[] EventData;
        public TimeSpan RelativeTime;
        public int FullDataSize;
        public int DataSize;
        public RawEvent(XefStreamInfo? s, byte[] t, byte[] e, TimeSpan r, int f, int d)
        { Stream = s; TagData = t; EventData = e; RelativeTime = r; FullDataSize = f; DataSize = d; }
    }
}

enum StreamType
{
    Color,
    Depth,
    Ir,
    Audio,
    Body,
    BodyIndex,
    Unknown
}

class FrameInfo
{
    public int FrameIndex { get; set; }
    public TimeSpan RelativeTime { get; set; }
    public StreamType StreamType { get; set; }
    public string StreamName { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int DataSize { get; set; }
}

class XefStreamInfo
{
    public short StreamIndex { get; }
    public short StreamFlags { get; }
    public int TagSize { get; }
    public string DataTypeName { get; }
    public Guid DataTypeId { get; }
    public Guid SemanticId { get; }
    public bool IsCompressed => (StreamFlags & 0x0001) == 0x0001;

    public XefStreamInfo(short index, short flags, int tagSize, string name, Guid typeId, Guid semanticId)
    {
        StreamIndex = index; StreamFlags = flags; TagSize = tagSize;
        DataTypeName = name; DataTypeId = typeId; SemanticId = semanticId;
    }
}
