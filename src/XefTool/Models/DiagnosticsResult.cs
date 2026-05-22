namespace XefTool.Models;

public class DiagnosticsResult
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int ColorFrames { get; set; }
    public int DepthFrames { get; set; }
    public int IrFrames { get; set; }
    public int UnknownFrames { get; set; }
    public int SkippedFrames { get; set; }
    public int ErrorCount { get; set; }
    public int ScanRecoveryAttempts { get; set; }
    public int ScanRecoverySuccesses { get; set; }
    public long LastValidPosition { get; set; }
    public long EventStartAddress { get; set; }
    public long TotalDataBytes { get; set; }
    public double ReadPercentage { get; set; }
    public TimeSpan TotalElapsed { get; set; }
    public double ReadSpeedMBps { get; set; }
    public List<string> Messages { get; set; } = new();
    public string Conclusion { get; set; } = string.Empty;
    public DiagnosticHealth Health { get; set; }
    public int TotalFrames => ColorFrames + DepthFrames + IrFrames + UnknownFrames;
}

public enum DiagnosticHealth
{
    Healthy,
    Warning,
    Critical
}

public class DiagnosticProgress
{
    public string Phase { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double Percentage { get; set; }
}
