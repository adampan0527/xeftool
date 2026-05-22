using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XefTool.Models;
using XefTool.Services;

namespace XefTool.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ImageExporterService _exporter = new();
    private readonly DiagnosticsService _diagnostics = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _inputFilePath = string.Empty;
    [ObservableProperty] private string _outputDirectory = string.Empty;
    [ObservableProperty] private bool _exportColor = true;
    [ObservableProperty] private bool _exportDepth = true;
    [ObservableProperty] private bool _exportIr = true;
    [ObservableProperty] private string _colorFormat = "jpg";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isDiagnosing;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _fileInfoText = string.Empty;
    [ObservableProperty] private string _logText = string.Empty;
    [ObservableProperty] private int _totalFrames;
    [ObservableProperty] private int _processedFrames;
    [ObservableProperty] private string _closeButtonText = "关闭";

    public bool IsBusy => IsRunning || IsDiagnosing;

    private void AppendLog(string message)
    {
        if (LogText.Length > 0)
            LogText += "\n" + message;
        else
            LogText = message;
    }

    [RelayCommand]
    private void BrowseInputFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "XEF 文件 (*.xef)|*.xef|所有文件 (*.*)|*.*",
            Title = "选择 XEF 文件"
        };
        if (dlg.ShowDialog() == true)
        {
            InputFilePath = dlg.FileName;
            LoadFileMetadata();
        }
    }

    [RelayCommand]
    private void BrowseOutputDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择输出文件夹"
        };
        if (dialog.ShowDialog() == true)
        {
            OutputDirectory = dialog.FolderName;
        }
    }

    private void LoadFileMetadata()
    {
        try
        {
            using var reader = new XefReader(InputFilePath);
            var metadata = reader.GetMetadata();
            var streamNames = string.Join(", ", metadata.Streams.Select(s => s.Type.ToString()));
            FileInfoText = $"文件: {metadata.FileName}\n大小: {metadata.FileSize / (1024.0 * 1024.0):F1} MB\n流: {metadata.StreamCount} ({streamNames})";
            StatusText = $"已加载: {metadata.FileName}";
        }
        catch (Exception ex)
        {
            FileInfoText = string.Empty;
            StatusText = $"加载失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StartExportAsync()
    {
        if (string.IsNullOrWhiteSpace(InputFilePath) || string.IsNullOrWhiteSpace(OutputDirectory))
        {
            MessageBox.Show("请先选择输入文件和输出目录。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!File.Exists(InputFilePath))
        {
            MessageBox.Show("输入文件不存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        IsRunning = true;
        ProgressValue = 0;
        ProgressText = string.Empty;
        LogText = string.Empty;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            await Task.Run(() => RunExport(token), token);
            StatusText = "导出完成";
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
            AppendLog("导出已取消");
        }
        catch (Exception ex)
        {
            StatusText = $"导出失败: {ex.Message}";
            AppendLog($"错误: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CloseOrCancel()
    {
        if (IsBusy)
        {
            _cts?.Cancel();
            StatusText = "正在取消...";
        }
        else
        {
            Application.Current.MainWindow.Close();
        }
    }

    partial void OnIsRunningChanged(bool value)
    {
        CloseButtonText = IsBusy ? "取消" : "关闭";
        OnPropertyChanged(nameof(IsBusy));
    }

    partial void OnIsDiagnosingChanged(bool value)
    {
        CloseButtonText = IsBusy ? "取消" : "关闭";
        OnPropertyChanged(nameof(IsBusy));
    }

    private void RunExport(CancellationToken token)
    {
        var timestamp = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
        string basePath = Path.Combine(OutputDirectory, timestamp);
        Directory.CreateDirectory(basePath);

        InvokeOnUI(() => AppendLog($"开始导出到: {basePath}"));

        using var reader = new XefReader(InputFilePath);
        int colorCount = 0, depthCount = 0, irCount = 0;
        var sw = Stopwatch.StartNew();

        FrameInfo? frame;
        while ((frame = reader.ReadNextEvent()) != null)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                switch (frame.StreamType)
                {
                    case StreamType.Color when ExportColor:
                        _exporter.ExportFrame(frame, Path.Combine(basePath, "color"), ColorFormat);
                        colorCount++;
                        break;
                    case StreamType.Depth when ExportDepth:
                        _exporter.ExportFrame(frame, Path.Combine(basePath, "depth"), ColorFormat);
                        depthCount++;
                        break;
                    case StreamType.Ir when ExportIr:
                        _exporter.ExportFrame(frame, Path.Combine(basePath, "ir"), ColorFormat);
                        irCount++;
                        break;
                }

                int total = colorCount + depthCount + irCount;
                if (total % 50 == 0 && total > 0)
                {
                    var elapsed = sw.Elapsed;
                    string msg = $"已导出: 彩色 {colorCount}, 深度 {depthCount}, 红外 {irCount} | 用时 {elapsed:hh\\:mm\\:ss}";
                    InvokeOnUI(() => StatusText = msg);
                }
            }
            catch (Exception ex)
            {
                InvokeOnUI(() => AppendLog($"帧 {frame.FrameIndex} 导出失败: {ex.Message}"));
            }
        }

        sw.Stop();
        int grandTotal = colorCount + depthCount + irCount;
        string summary = $"导出完成: 彩色 {colorCount}, 深度 {depthCount}, 红外 {irCount} 帧, 共 {sw.Elapsed:hh\\:mm\\:ss}";
        InvokeOnUI(() =>
        {
            AppendLog(summary);
            ProgressText = summary;
            StatusText = summary;
        });
    }

    [RelayCommand]
    private async Task StartDiagnosticsAsync()
    {
        if (string.IsNullOrWhiteSpace(InputFilePath))
        {
            MessageBox.Show("请先选择输入文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!File.Exists(InputFilePath))
        {
            MessageBox.Show("输入文件不存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        IsDiagnosing = true;
        ProgressValue = 0;
        ProgressText = string.Empty;
        LogText = string.Empty;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var progress = new Progress<DiagnosticProgress>(p =>
            {
                InvokeOnUI(() =>
                {
                    StatusText = $"[{p.Phase}] {p.Message}";
                    ProgressValue = p.Percentage;
                    ProgressText = $"{p.Percentage:F0}%";
                });
            });

            var result = await _diagnostics.DiagnoseAsync(InputFilePath, progress, token);

            AppendLog("════════════════════════════════════════");
            AppendLog($"诊断完成: {result.FileName}");
            AppendLog($"  彩色帧: {result.ColorFrames}");
            AppendLog($"  深度帧: {result.DepthFrames}");
            AppendLog($"  红外帧: {result.IrFrames}");
            AppendLog($"  读取进度: {result.ReadPercentage:F2}%");
            AppendLog($"  错误次数: {result.ErrorCount}");
            AppendLog($"  扫描恢复: {result.ScanRecoverySuccesses}/{result.ScanRecoveryAttempts}");
            AppendLog($"  用时: {result.TotalElapsed:mm\\:ss\\.ff}");
            AppendLog("────────────────────────────────────────");
            AppendLog($"结论: {result.Conclusion}");

            StatusText = result.Health switch
            {
                DiagnosticHealth.Healthy => "诊断完成: 文件正常",
                DiagnosticHealth.Warning => $"诊断完成: 有警告 - {result.Conclusion}",
                DiagnosticHealth.Critical => $"诊断完成: 严重问题 - {result.Conclusion}",
                _ => "诊断完成"
            };
            ProgressValue = 100;
            ProgressText = "100%";
        }
        catch (OperationCanceledException)
        {
            StatusText = "诊断已取消";
            AppendLog("诊断已取消");
        }
        catch (Exception ex)
        {
            StatusText = $"诊断失败: {ex.Message}";
            AppendLog($"错误: {ex.Message}");
        }
        finally
        {
            IsDiagnosing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static void InvokeOnUI(Action action)
    {
        if (Application.Current?.Dispatcher != null)
            Application.Current.Dispatcher.Invoke(action);
        else
            action();
    }
}
