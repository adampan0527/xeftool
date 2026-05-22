# XefTool

Kinect V2 XEF 录像文件图像序列导出工具（Windows 桌面应用）。

[English](README.md)

## 功能特性

- **XEF 文件解析** — 支持读取压缩（zlib）和非压缩格式的 XEF 录像
- **多流导出** — 支持彩色（Color）、深度（Depth）、红外（IR）三种数据流
- **色彩转换** — 彩色帧 YUYV 转 RGB（1920x1080）
- **深度可视化** — 深度图伪彩色映射（512x424）
- **红外导出** — 灰度红外图像（512x424）
- **格式选择** — 彩色帧支持导出为 JPG 或 PNG
- **文件诊断** — 内置 XEF 文件完整性检测，支持错误自动恢复
- **Fluent UI** — 基于 WPF-UI 的现代化 Fluent Design 界面

## 项目结构

```
xefTool/
├── src/
│   ├── XefTool/                  # WPF 图形界面应用
│   │   ├── Converters/           # 值转换器（深度色彩映射等）
│   │   ├── Models/               # 数据模型（FrameInfo、FileMetadata 等）
│   │   ├── Services/             # 核心服务
│   │   │   ├── XefReader.cs      # XEF 二进制格式解析器
│   │   │   ├── ImageExporterService.cs  # 图像导出逻辑
│   │   │   └── DiagnosticsService.cs    # 文件诊断服务
│   │   └── ViewModels/           # MVVM 视图模型
│   └── XefDiagnostic/            # 命令行诊断工具
├── diagnose.bat                  # 快速诊断启动脚本
├── XefTool.slnx                  # 解决方案文件
└── LICENSE                       # MIT 许可证
```

## 环境要求

- Windows 10/11
- [.NET 10.0 SDK](https://dotnet.microsoft.com/) 或更高版本

## 编译

```bash
dotnet build XefTool.slnx -c Release
```

## 使用方法

### 图形界面应用

```bash
dotnet run --project src/XefTool -c Release
```

1. 点击文件夹按钮选择 `.xef` 文件
2. 选择输出目录
3. 勾选需要导出的数据流（彩色 / 深度 / 红外）
4. 选择彩色图像格式（JPG 或 PNG）
5. 点击"开始导出"

导出的图像保存在输出目录下的时间戳子文件夹中：

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

### 命令行诊断工具

```bash
# 诊断单个文件
dotnet run --project src/XefDiagnostic -- "路径\到\文件.xef"

# 批量诊断多个文件
dotnet run --project src/XefDiagnostic -- file1.xef file2.xef file3.xef

# 或使用批处理脚本
diagnose.bat "路径\到\文件.xef"
```

诊断工具会检查以下内容：
- 文件头有效性（签名：`EVENTS1`）
- 流定义信息（Color、Depth、IR、Body 等）
- 逐帧数据完整性
- 错误自动扫描恢复

## XEF 格式说明

XEF（Xbox Experience Framework）是 Kinect for Windows v2 使用的录像格式。二进制结构如下：

- **文件头** — 8 字节签名（`EVENTS1`）、流数量、元数据
- **流定义** — 类型 GUID、名称、压缩标志
- **事件数据** — 按时间戳顺序排列的帧数据

支持的数据流类型：

| 数据流 | 分辨率 | 像素格式 |
|--------|--------|----------|
| 彩色 (Color) | 1920x1080 | YUYV（2 bpp） |
| 深度 (Depth) | 512x424 | 16 位深度值 |
| 红外 (IR) | 512x424 | 16 位红外值 |
| 骨骼 (Body) | — | 骨骼跟踪数据 |

## 致谢

XEF 二进制解析逻辑移植自 [KinectXEFTools](https://github.com/EricMyers47/KinectXEFTools)（MIT 许可证）。

## 许可证

[MIT 许可证](LICENSE) — Copyright (c) 2026 Yan Pan
