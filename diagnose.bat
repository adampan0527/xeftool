@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================
echo     XEF 文件诊断工具
echo ============================================
echo.

if "%~1"=="" (
    echo 用法: diagnose.bat ^<文件路径^> [文件路径2] [文件路径3] ...
    echo.
    echo 示例:
    echo   diagnose.bat "XEF2JPEG_Input\chen\20260514_034238_00.xef"
    echo   diagnose.bat file1.xef file2.xef file3.xef
    echo.
    echo 或者直接诊断三个问题文件:
    echo   diagnose.bat "XEF2JPEG_Input\chen\20260514_034238_00.xef" "XEF2JPEG_Input\chen\20260514_043701_00.xef" "XEF2JPEG_Input\chen\20260514_045922_00.xef"
    echo.
    pause
    exit /b 1
)

echo 开始诊断...
echo.

dotnet run --project src/XefDiagnostic -c Release -- %*

echo.
echo ============================================
echo 诊断完成
echo ============================================
pause
