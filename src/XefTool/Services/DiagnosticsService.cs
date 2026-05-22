using System.Diagnostics;
using System.IO;
using XefTool.Models;

namespace XefTool.Services;

public class DiagnosticsService
{
    public async Task<DiagnosticsResult> DiagnoseAsync(
        string filePath,
        IProgress<DiagnosticProgress> progress,
        CancellationToken ct)
    {
        var result = new DiagnosticsResult
        {
            FileName = Path.GetFileName(filePath),
            FileSize = new FileInfo(filePath).Length
        };
        var sw = Stopwatch.StartNew();

        await Task.Run(() => RunDiagnosis(filePath, result, progress, ct), ct);

        sw.Stop();
        result.TotalElapsed = sw.Elapsed;

        long readBytes = result.LastValidPosition - result.EventStartAddress;
        if (result.TotalElapsed.TotalSeconds > 0 && readBytes > 0)
            result.ReadSpeedMBps = readBytes / result.TotalElapsed.TotalSeconds / (1024 * 1024);

        GenerateConclusion(result);
        return result;
    }

    private void RunDiagnosis(
        string filePath,
        DiagnosticsResult result,
        IProgress<DiagnosticProgress> progress,
        CancellationToken ct)
    {
        Report(progress, "阶段1", "读取文件头...", 0);

        using var reader = new XefReader(filePath);

        result.EventStartAddress = reader.EventStartAddress;
        Report(progress, "阶段1", $"文件头有效 | 事件数据起始: 0x{reader.EventStartAddress:X}", 5);

        Report(progress, "阶段2", "扫描流定义...", 8);
        var metadata = reader.GetMetadata();
        foreach (var stream in metadata.Streams)
        {
            var compressed = stream.IsCompressed ? " [压缩]" : "";
            result.Messages.Add($"  [{stream.Index}] {stream.Name} ({stream.Type}){compressed}");
        }
        Report(progress, "阶段2", $"发现 {metadata.StreamCount} 个流定义", 10);

        Report(progress, "阶段3", "逐帧读取诊断...", 12);

        int frameCount = 0;
        FrameInfo? frame;

        while ((frame = reader.ReadNextEvent()) != null)
        {
            ct.ThrowIfCancellationRequested();
            frameCount++;

            switch (frame.StreamType)
            {
                case StreamType.Color: result.ColorFrames++; break;
                case StreamType.Depth: result.DepthFrames++; break;
                case StreamType.Ir: result.IrFrames++; break;
                default: result.UnknownFrames++; break;
            }

            if (frameCount % 100 == 0)
            {
                double readProgress = (reader.CurrentPosition - reader.EventStartAddress) /
                    (double)(reader.FileLength - reader.EventStartAddress) * 100;
                double overallProgress = 12 + readProgress * 0.85;
                string msg = $"已处理 {frameCount} 帧 | 进度: {readProgress:F1}% | " +
                    $"彩色: {result.ColorFrames} 深度: {result.DepthFrames} 红外: {result.IrFrames}";
                Report(progress, "阶段3", msg, Math.Min(overallProgress, 97));
            }
        }

        // Collect diagnostic stats from reader
        result.ErrorCount = reader.DiagnosticErrorCount;
        result.ScanRecoveryAttempts = reader.DiagnosticScanAttempts;
        result.ScanRecoverySuccesses = reader.DiagnosticScanSuccesses;
        result.LastValidPosition = reader.DiagnosticLastValidPosition > 0
            ? reader.DiagnosticLastValidPosition
            : reader.CurrentPosition;
        result.TotalDataBytes = reader.FileLength - reader.EventStartAddress;

        long readBytes = result.LastValidPosition - reader.EventStartAddress;
        result.ReadPercentage = result.TotalDataBytes > 0
            ? (double)readBytes / result.TotalDataBytes * 100
            : 0;

        Report(progress, "阶段4", "诊断完成", 100);
    }

    private static void GenerateConclusion(DiagnosticsResult result)
    {
        if (result.ReadPercentage < 1.0)
        {
            result.Health = DiagnosticHealth.Critical;
            result.Conclusion = "文件读取在非常早期就终止，文件可能存在严重损坏、格式不兼容或不完整。";
            result.Messages.Add("结论: 文件严重损坏或不完整");
            result.Messages.Add("建议: 使用十六进制编辑器检查文件，验证文件是否完整传输");
        }
        else if (result.ReadPercentage < 90.0)
        {
            result.Health = DiagnosticHealth.Warning;
            result.Conclusion = $"文件读取提前终止 (仅读取 {result.ReadPercentage:F1}%)，文件中存在损坏区域。";
            result.Messages.Add($"结论: 文件部分损坏 (读取 {result.ReadPercentage:F1}%)");
            result.Messages.Add($"建议: 检查位置 0x{result.LastValidPosition:X} 之后的数据");
        }
        else
        {
            result.Health = result.ErrorCount > 0 ? DiagnosticHealth.Warning : DiagnosticHealth.Healthy;
            result.Conclusion = result.ErrorCount > 0
                ? $"文件基本完整 (读取 {result.ReadPercentage:F1}%)，存在 {result.ErrorCount} 个错误但已恢复。"
                : $"文件完整 (读取 {result.ReadPercentage:F1}%)";
            result.Messages.Add($"结论: 文件读取完成 ({result.ReadPercentage:F1}%)");
        }
    }

    private static void Report(IProgress<DiagnosticProgress> progress, string phase, string message, double percentage)
    {
        progress.Report(new DiagnosticProgress
        {
            Phase = phase,
            Message = message,
            Percentage = percentage
        });
    }
}
