# XefTool

A Windows desktop tool for exporting image sequences from Kinect V2 XEF recording files.

[中文文档](README_CN.md)

## Features

- **XEF File Parsing** — Reads both compressed (zlib) and uncompressed XEF recordings
- **Multi-stream Export** — Supports Color, Depth, and Infrared (IR) streams
- **Color Conversion** — YUYV to RGB conversion for color frames (1920x1080)
- **Depth Visualization** — Color-mapped depth images (512x424)
- **IR Export** — Grayscale infrared images (512x424)
- **Format Options** — Export color frames as JPG or PNG
- **File Diagnostics** — Built-in XEF file integrity checker with error recovery
- **Fluent UI** — Modern WPF interface using WPF-UI (Fluent Design)

## Project Structure

```
xefTool/
├── src/
│   ├── XefTool/                  # WPF GUI application
│   │   ├── Converters/           # Value converters (depth color map, etc.)
│   │   ├── Models/               # Data models (FrameInfo, FileMetadata, etc.)
│   │   ├── Services/             # Core services
│   │   │   ├── XefReader.cs      # XEF binary format reader
│   │   │   ├── ImageExporterService.cs  # Image export logic
│   │   │   └── DiagnosticsService.cs    # File diagnostics
│   │   └── ViewModels/           # MVVM view models
│   └── XefDiagnostic/            # CLI diagnostic tool
├── diagnose.bat                  # Quick diagnostic launcher
├── XefTool.slnx                  # Solution file
└── LICENSE                       # MIT License
```

## Requirements

- Windows 10/11
- [.NET 10.0 SDK](https://dotnet.microsoft.com/) (or later)

## Build

```bash
dotnet build XefTool.slnx -c Release
```

## Usage

### GUI Application

```bash
dotnet run --project src/XefTool -c Release
```

1. Click the folder button to select a `.xef` file
2. Choose an output directory
3. Select which streams to export (Color / Depth / IR)
4. Choose color image format (JPG or PNG)
5. Click "Start Export"

Exported images are saved to a timestamped subfolder under the output directory:

```
output/
└── 2026_05_22_14_30_00/
    ├── color/
    │   ├── color_000001.jpg
    │   └── ...
    ├── depth/
    │   ├── depth_000001.png
    │   └── ...
    └── ir/
        ├── ir_000001.png
        └── ...
```

### CLI Diagnostic Tool

```bash
# Diagnose a single file
dotnet run --project src/XefDiagnostic -- "path\to\file.xef"

# Batch diagnose multiple files
dotnet run --project src/XefDiagnostic -- file1.xef file2.xef file3.xef

# Or use the batch script
diagnose.bat "path\to\file.xef"
```

The diagnostic tool checks:
- File header validity (signature: `EVENTS1`)
- Stream definitions (Color, Depth, IR, Body, etc.)
- Frame-by-frame data integrity
- Automatic error recovery via scanning

## XEF Format

XEF (Xbox Experience Framework) is the recording format used by Kinect for Windows v2. The binary format consists of:

- **Header** — 8-byte signature (`EVENTS1`), stream count, metadata
- **Stream Definitions** — Type GUIDs, names, compression flags
- **Events** — Sequential frame data with timestamps

Supported stream types:

| Stream | Resolution | Pixel Format |
|--------|-----------|--------------|
| Color  | 1920x1080 | YUYV (2 bpp) |
| Depth  | 512x424   | 16-bit depth |
| IR     | 512x424   | 16-bit IR    |
| Body   | —         | Skeleton data|

## Acknowledgments

XEF binary parsing logic ported from [KinectXEFTools](https://github.com/EricMyers47/KinectXEFTools) (MIT License).

## License

[MIT License](LICENSE) — Copyright (c) 2026 Yan Pan
