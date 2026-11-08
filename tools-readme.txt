USBridge（WSL USB 管家）便携版使用说明
======================================

【这是什么】
一个绿色免安装的 WSL USB 设备管理器：把 USB 设备（含 Android 手机 ADB 调试）
一键映射到 WSL 中，并自动处理「Windows 端 adb 占用导致 WSL 检测不到设备」的问题。

【怎么用】
1. 双击 USBridge.exe 即可运行（Win10 1903+ / Win11 无需安装任何运行库）。
2. 首次使用：若提示未找到 usbipd-win，点「一键安装」，
   会用本包内置的 tools\usbipd\ 下的官方 MSI 自动完成安装（需 UAC 授权）。
3. tools\adb\ 内置了 adb，无需另外安装 Android SDK。
4. WSL 侧建议安装 adb：在 WSL 终端执行  sudo apt install adb

【目录结构】
USBridge.exe               主程序
tools\adb\                 内置 adb.exe（绿色可用，优先于系统 PATH）
tools\usbipd\*.msi         usbipd-win 官方安装包（用于一键安装）

【目标电脑要求】
- Windows 10 (1903+) / Windows 11
- WSL2 已安装且至少有一个发行版（wsl --install）
- usbipd-win：可直接用「一键安装」（需联网仅限于无，安装包已内置，离线可用）

【常见问题】
- 手机连接到 WSL 后 WSL 里 adb devices 看不到？
  保持「ADB 模式」开启即可，它会自动：结束 Windows adb → 连接 → 在 WSL 内以 root 重启 adb。
  也可随时点「修复 ADB」手动执行一遍。
- Android Studio 开着时设备又消失？
  它会自动拉起 Windows 端 adb 重新占用设备，关掉它或再点一次「修复 ADB」。

【第三方组件与许可】
- usbipd-win MSI：GPL-3.0，作者 Dorssel 等
  源码：https://github.com/dorssel/usbipd-win
  本包未修改原始 MSI，仅随包再分发；GPL-3.0 允许此行为。
- Android platform-tools（adb.exe / AdbWinApi.dll / AdbWinUsbApi.dll）：Apache License 2.0
  源码：https://android.googlesource.com/platform/tools/
- 本程序（USBridge）代码可自由修改使用，参考项目：
  https://github.com/nickbeth/wsl-usb-manager
