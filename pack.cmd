@echo off
rem ============================================================
rem  WSL USB Manager portable bundle packer (ASCII only)
rem  1) build exe  2) fetch adb + usbipd-win msi  3) assemble
rem  4) zip -> dist\WslUsbManager-portable.zip
rem ============================================================
setlocal EnableExtensions
cd /d "%~dp0"

if not exist cache mkdir cache

echo [1/4] Building app...
call build.cmd
if errorlevel 1 exit /b 1

set ROOT=pack\USBridge
echo [2/4] Preparing portable layout...
if exist pack rmdir /s /q pack 2>nul
mkdir "%ROOT%\tools\adb" 2>nul
mkdir "%ROOT%\tools\usbipd" 2>nul
copy /y dist\USBridge.exe "%ROOT%\" >nul
if errorlevel 1 exit /b 1
copy /y tools-readme.txt "%ROOT%\README.txt" >nul
if errorlevel 1 exit /b 1

echo [3/4] Fetching tools...
if not exist cache\platform-tools.zip (
    echo   - downloading Android platform-tools ...
    curl -L --fail --retry 2 -o cache\platform-tools.zip https://dl.google.com/android/repository/platform-tools-latest-windows.zip
    if errorlevel 1 exit /b 1
)
if not exist cache\platform-tools\adb.exe (
    echo   - extracting platform-tools ...
    powershell -NoProfile -Command "Expand-Archive -Path 'cache\platform-tools.zip' -DestinationPath 'cache' -Force"
    if errorlevel 1 exit /b 1
)
copy /y cache\platform-tools\adb.exe "%ROOT%\tools\adb\" >nul
if errorlevel 1 exit /b 1
copy /y cache\platform-tools\AdbWinApi.dll "%ROOT%\tools\adb\" >nul
if errorlevel 1 exit /b 1
copy /y cache\platform-tools\AdbWinUsbApi.dll "%ROOT%\tools\adb\" >nul
if errorlevel 1 exit /b 1

if exist cache\usbipd.msi goto msi_ready
echo   - resolving latest usbipd-win release ...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $req=[System.Net.HttpWebRequest]::Create('https://github.com/dorssel/usbipd-win/releases/latest'); $req.AllowAutoRedirect=$false; $loc=$req.GetResponse().Headers['Location']; Write-Output $loc.Split('/')[-1].TrimStart('v')" > cache\usbipd_ver.txt
if errorlevel 1 (
    echo   [WARN] resolve failed. Put usbipd-win MSI manually into cache\usbipd.msi and re-run.
    exit /b 1
)
set /p MSIVER=<cache\usbipd_ver.txt
echo   - fetching asset list for usbipd-win v%MSIVER% ...
curl -sL --fail --retry 2 "https://github.com/dorssel/usbipd-win/releases/expanded_assets/v%MSIVER%" -o cache\assets.html
if errorlevel 1 (
    echo   [WARN] fetch failed. Put usbipd-win MSI manually into cache\usbipd.msi and re-run.
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$c=Get-Content 'cache\assets.html' -Raw; $m=[regex]::Match($c,'releases/download/[^> ]+x64\.msi'); if(-not $m.Success){ $m=[regex]::Match($c,'releases/download/[^> ]+\.msi') }; if(-not $m.Success){throw 'no msi asset found'}; $u=$m.Value.TrimEnd([char]34); if(-not $u.StartsWith('http')){ $u='https://github.com/dorssel/usbipd-win/'+$u }; Write-Output $u" > cache\usbipd_url.txt
if errorlevel 1 (
    echo   [WARN] no msi asset found on release page. Put usbipd-win MSI manually into cache\usbipd.msi and re-run.
    exit /b 1
)
set /p MSIURL=<cache\usbipd_url.txt
echo   - downloading %MSIURL%
curl -L --fail --retry 2 -o cache\usbipd.msi "%MSIURL%"
if errorlevel 1 (
    echo   [WARN] download failed. Put usbipd-win MSI manually into cache\usbipd.msi and re-run.
    exit /b 1
)
:msi_ready
copy /y cache\usbipd.msi "%ROOT%\tools\usbipd\" >nul
if errorlevel 1 exit /b 1

echo [4/4] Creating zip...
powershell -NoProfile -Command "Compress-Archive -Path 'pack\USBridge' -DestinationPath 'dist\USBridge-portable.zip' -Force"
if errorlevel 1 exit /b 1

echo.
echo [OK] portable bundle ready:
echo      folder : %ROOT%
echo      zip    : dist\USBridge-portable.zip
echo.
echo      tools\adb\adb.exe            - bundled adb (portable, no install needed)
echo      tools\usbipd\usbipd-win.msi  - will be offered as one-click install on target PC
endlocal
