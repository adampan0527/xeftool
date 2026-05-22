namespace XefTool.Models;

public enum StreamType
{
    Color,
    Depth,
    Ir,
    Audio,
    Body,
    BodyIndex,
    Unknown
}

public class FrameInfo
{
    public int FrameIndex { get; set; }
    public int EventIndex { get; set; }
    public TimeSpan RelativeTime { get; set; }
    public StreamType StreamType { get; set; }
    public string StreamName { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int DataSize { get; set; }
}

public class FileMetadata
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int StreamCount { get; set; }
    public List<StreamInfo> Streams { get; set; } = new();
}

public class StreamInfo
{
    public short Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid DataTypeId { get; set; }
    public bool IsCompressed { get; set; }
    public StreamType Type { get; set; }
}

public class ExportOptions
{
    public string InputFilePath { get; set; } = string.Empty;
    public string OutputRootPath { get; set; } = string.Empty;
    public bool ExportColor { get; set; } = true;
    public bool ExportDepth { get; set; } = true;
    public bool ExportIr { get; set; } = true;
    public string ColorFormat { get; set; } = "jpg";
}
