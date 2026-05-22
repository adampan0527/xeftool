using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using XefTool.Models;

namespace XefTool.Services;

/// <summary>
/// XEF binary format reader, ported from KinectXEFTools (MIT License).
/// Supports both uncompressed and compressed (zlib/archived) XEF files.
/// Streams events sequentially for memory efficiency with large files.
/// </summary>
public class XefReader : IDisposable
{
    // --- Stream type GUIDs (from KinectXEFTools/XEFUtil.cs) ---
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

    // --- Data constants ---
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

    // --- Frame dimensions ---
    public const int COLOR_WIDTH = 1920;
    public const int COLOR_HEIGHT = 1080;
    public const int DEPTH_WIDTH = 512;
    public const int DEPTH_HEIGHT = 424;
    public const int IR_WIDTH = 512;
    public const int IR_HEIGHT = 424;

    private readonly BinaryReader _reader;
    private readonly Dictionary<short, XefStreamInfo> _streams = new();
    private int _totalReportedStreams;
    private readonly long _eventStartAddress;
    private bool _endOfStream;

    // Diagnostic tracking
    private int _errorCount;
    private int _scanRecoveryAttempts;
    private int _scanRecoverySuccesses;
    private long _lastValidPosition;

    public string FilePath { get; }
    public bool EndOfStream => _endOfStream;
    public int StreamCount => Math.Max(_streams.Count, _totalReportedStreams);
    public long EventStartAddress => _eventStartAddress;
    public long CurrentPosition => _reader.BaseStream.Position;
    public long FileLength => _reader.BaseStream.Length;
    public int DiagnosticErrorCount => _errorCount;
    public int DiagnosticScanAttempts => _scanRecoveryAttempts;
    public int DiagnosticScanSuccesses => _scanRecoverySuccesses;
    public long DiagnosticLastValidPosition => _lastValidPosition;

    public XefReader(string path)
    {
        FilePath = path;
        _reader = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        ReadHeader();
        _eventStartAddress = _reader.BaseStream.Position;
    }

    public void Dispose()
    {
        _reader?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ReadHeader()
    {
        byte[] signature = _reader.ReadBytes(8);
        string sig = Encoding.ASCII.GetString(signature);
        if (!sig.StartsWith("EVENTS1"))
            throw new InvalidDataException($"Not a valid XEF file. Signature: {sig}");

        _reader.ReadUInt32();
        int streamCountReported = _reader.ReadInt32();
        if (streamCountReported == 0)
            streamCountReported = int.MaxValue;
        _totalReportedStreams = streamCountReported - 1;
        _reader.ReadInt64();
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

    public FileMetadata GetMetadata()
    {
        long savedPos = _reader.BaseStream.Position;
        _reader.BaseStream.Position = _eventStartAddress;
        _endOfStream = false;
        _streams.Clear();

        var metadata = new FileMetadata
        {
            FilePath = FilePath,
            FileName = Path.GetFileName(FilePath),
            FileSize = new FileInfo(FilePath).Length
        };

        while (!_endOfStream)
        {
            PeekEventKey(out short streamIndex, out short streamFlags);
            if (streamIndex == EVENT_UNKRECORD_INDEX) { SkipUnknownEvent(); continue; }
            if (streamIndex == _totalReportedStreams + 1) { _endOfStream = true; break; }
            if (!_streams.ContainsKey(streamIndex))
            {
                var stream = ReadStreamDescription();
                _streams[streamIndex] = stream;
                metadata.Streams.Add(new StreamInfo
                {
                    Index = streamIndex,
                    Name = stream.DataTypeName,
                    DataTypeId = stream.DataTypeId,
                    IsCompressed = stream.IsCompressed,
                    Type = ClassifyStream(stream.DataTypeId)
                });
            }
            else break;
        }

        metadata.StreamCount = _streams.Count;
        _reader.BaseStream.Position = _eventStartAddress;
        _endOfStream = false;
        _streams.Clear();
        RebuildStreams();
        return metadata;
    }

    private void RebuildStreams()
    {
        while (!_endOfStream)
        {
            PeekEventKey(out short streamIndex, out short streamFlags);
            if (streamIndex == EVENT_UNKRECORD_INDEX) { SkipUnknownEvent(); continue; }
            if (streamIndex == _totalReportedStreams + 1) { _endOfStream = true; break; }
            if (!_streams.ContainsKey(streamIndex))
                _streams[streamIndex] = ReadStreamDescription();
            else break;
        }
    }

    public FrameInfo? ReadNextEvent()
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
                    catch (InvalidDataException)
                    {
                        _errorCount++;
                        _reader.BaseStream.Position = posBefore;
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
                    if (result != null)
                    {
                        _lastValidPosition = _reader.BaseStream.Position;
                        return result;
                    }
                }
                catch (InvalidDataException)
                {
                    _errorCount++;
                    _reader.BaseStream.Position = dataPosBefore;
                    if (!ScanForNextValidEvent())
                    {
                        _endOfStream = true;
                        return null;
                    }
                    continue;
                }

                // ReadDataEvent returned null (error occurred), try to recover
                if (!_endOfStream)
                {
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
        }
        return null;
    }

    private bool ScanForNextValidEvent()
    {
        _scanRecoveryAttempts++;
        long startPos = _reader.BaseStream.Position;
        long maxScan = Math.Min(startPos + 0x100000, _reader.BaseStream.Length - 48);

        for (long pos = startPos + 4; pos < maxScan; pos++)
        {
            _reader.BaseStream.Position = pos;
            short testIndex = _reader.ReadInt16();
            short testFlags = _reader.ReadInt16();
            int testSize = _reader.ReadInt32();

            if (!IsValidStreamIndex(testIndex) || testFlags < 0 || testSize <= 0 || testSize > 50 * 1024 * 1024)
                continue;

            // Verify: try different tag sizes and check if next position also looks valid
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
                    // Double verify: check one more position ahead
                    long nextNextPos = nextPos + 24 + tagSize + nextSize;
                    if (nextNextPos + 8 <= _reader.BaseStream.Length)
                    {
                        _reader.BaseStream.Position = nextNextPos;
                        short nnIndex = _reader.ReadInt16();
                        if (!IsValidStreamIndex(nnIndex))
                            continue;
                    }

                    _reader.BaseStream.Position = pos;
                    _scanRecoverySuccesses++;
                    return true;
                }
            }
        }
        return false;
    }

    public void Reset()
    {
        _reader.BaseStream.Position = _eventStartAddress;
        _endOfStream = false;
        _streams.Clear();
        RebuildStreams();
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
                    return null; // incomplete decompression, skip frame
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

internal class XefStreamInfo
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
