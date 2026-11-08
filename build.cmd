@echo off
rem ============================================================
rem  WSL USB Manager build script (ASCII only)
rem  Uses the built-in .NET Framework 4.8 compiler (csc.exe).
rem  No SDK / Visual Studio required.
rem ============================================================
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPFLIB=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF
rem Fallback for CI / machines with only the 4.8 targeting pack installed
if not exist "%WPFLIB%\PresentationFramework.dll" set WPFLIB=C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8
set OUTDIR=dist

if not exist "%CSC%" (
    echo [ERROR] compiler not found: %CSC%
    exit /b 1
)

if not exist "%OUTDIR%" mkdir "%OUTDIR%"

echo Building...
"%CSC%" /nologo /utf8output /codepage:65001 ^
  /target:winexe /platform:anycpu /optimize+ ^
  /win32manifest:app.manifest ^
  /win32icon:assets\logo.ico ^
  /out:"%OUTDIR%\USBridge.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Xaml.dll ^
  /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:"%WPFLIB%\WindowsBase.dll" ^
  /r:"%WPFLIB%\PresentationCore.dll" ^
  /r:"%WPFLIB%\PresentationFramework.dll" ^
  /resource:src\MainWindow.xaml ^
  /resource:src\SettingsDialog.xaml ^
  /resource:assets\logo.ico,App.logo.ico ^
  src\App.cs src\Main.cs src\ProcessRunner.cs src\Settings.cs src\Theme.cs src\Tray.cs src\Usbipd.cs src\Wsl.cs src\Adb.cs

if errorlevel 1 (
    echo.
    echo [FAILED] build errors occurred.
    exit /b 1
)

echo.
echo [OK] generated %OUTDIR%\USBridge.exe
endlocal
