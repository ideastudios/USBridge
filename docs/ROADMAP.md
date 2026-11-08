# ROADMAP

## 已实现（v1.2.0）

- 设备列表实时刷新（3 秒轮询 + WM_DEVICECHANGE 插拔事件即时触发）
- 智能一键操作：行内快捷按钮随状态切换「共享并连接 → 连接到 WSL → 断开连接」；双击设备行等效
- 共享 / 取消共享 / 连接到 WSL / 断开连接，未提权时自动单次 UAC，顶栏一键管理员重启
- ADB 模式：连接前 Windows `adb kill-server` → WSL 内 `modprobe vhci_hcd` → 连接 → WSL 内 root 重启 adb 并回显设备列表
- 断开连接后自动 Windows 端 `adb start-server`，设备交还 Windows 立即可用
- 「修复 ADB」一键手动修复 Windows + WSL 两侧 adb 环境
- 自动连接守护（`usbipd --auto-attach`），跨应用重启有效，可随时停止
- 便携捆绑：内置绿色 adb 与 usbipd-win 官方 MSI，缺失 usbipd 时「一键安装」静默部署
- 深色 / 浅色双主题（GitHub Dark / Light），实时切换、持久化，标题栏明暗跟随
- 系统托盘：左键唤回、右键快捷菜单、最小化/关闭到托盘可配置
- WSL 发行版选择（多发行版环境）
- 中文 / 英文双语命名与全中文界面；150% DPI 适配；彩色运行日志
- 单实例（重复启动自动唤回已有窗口）
- GitHub Actions 自动发布流水线（便携包 zip + SHA256 + feed）

## 候选（未实现）

- 应用内「检查更新」（feed 已就绪：`feed` 分支 `latest.json`）
- 单文件集成：将 adb 与 usbipd MSI 作为资源嵌入 exe，按需提取
- 跨会话自动连接守护的精确停止（WMI 按命令行枚举 usbipd 进程）
- 多实例模式（可配置关闭单实例锁）
- 设备历史别名 / 收藏置顶
- 多语言界面（i18n 资源化）
- 代码签名（消除 SmartScreen 未知发布者提示）
