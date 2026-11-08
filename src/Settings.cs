using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WslUsbManager
{
    /// <summary>
    /// 极简键值设置存储（INI 风格），保存于 %APPDATA%\WslUsbManagerPro\settings.ini
    /// </summary>
    public static class Settings
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>();
        private static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "WslUsbManagerPro", "settings.ini");

        // ---- 设置键名 ----
        public const string KAdbPath = "AdbPath";               // adb.exe 完整路径（留空自动搜索）
        public const string KAdbFixOnAttach = "AdbFixOnAttach"; // ADB 模式：附加时自动执行修复流程
        public const string KAdbFixAlways = "AdbFixAlways";     // 对所有设备执行 ADB 修复（默认仅 Android）
        public const string KWslRestartAdb = "WslRestartAdb";   // 附加后在 WSL 内重启 adb 服务（root）
        public const string KWslLoadVhci = "WslLoadVhci";       // 附加前在 WSL 加载 vhci_hcd 模块
        public const string KAutoRefresh = "AutoRefresh";       // 自动刷新设备列表
        public const string KDistro = "Distro";                 // 目标 WSL 发行版（空 = 自动）
        public const string KLogOpen = "LogOpen";               // 日志面板展开状态
        public const string KDarkTheme = "DarkTheme";           // 深色主题（1 = 深色，0 = 浅色）
        public const string KTrayIcon = "TrayIcon";             // 启用系统托盘图标
        public const string KTrayMinimize = "TrayMinimize";     // 最小化时隐藏到托盘
        public const string KTrayClose = "TrayClose";           // 点击关闭时隐藏到托盘
        public const string KAutoAttachPrefix = "AutoAttach.";  // 自动附加进程 PID 记录

        public static void Load()
        {
            lock (Map)
            {
                Map.Clear();
                try
                {
                    if (!File.Exists(FilePath)) return;
                    string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                    foreach (string line in lines)
                    {
                        string t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("#") || t.StartsWith(";")) continue;
                        int i = t.IndexOf('=');
                        if (i <= 0) continue;
                        string k = t.Substring(0, i).Trim();
                        string v = t.Substring(i + 1).Trim();
                        Map[k] = v;
                    }
                }
                catch { /* 设置损坏则按默认处理 */ }
            }
        }

        public static void Save()
        {
            lock (Map)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("# WSL USB Manager 设置文件");
                    foreach (KeyValuePair<string, string> kv in Map)
                    {
                        sb.Append(kv.Key).Append('=').AppendLine(kv.Value);
                    }
                    File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
                }
                catch { /* 忽略写入失败 */ }
            }
        }

        public static string Get(string key, string def)
        {
            lock (Map)
            {
                string v;
                if (Map.TryGetValue(key, out v) && v != null) return v;
                return def;
            }
        }

        public static void Set(string key, string value)
        {
            lock (Map) { Map[key] = value == null ? "" : value; }
        }

        public static bool GetBool(string key, bool def)
        {
            string v = Get(key, null);
            if (v == null) return def;
            if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "on" || v == "yes") return true;
            if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "off" || v == "no") return false;
            return def;
        }

        public static void SetBool(string key, bool value)
        {
            Set(key, value ? "1" : "0");
        }

        /// <summary>
        /// 读取自动附加进程记录：busid -> usbipd 进程 PID
        /// </summary>
        public static Dictionary<string, int> GetAutoAttach()
        {
            Dictionary<string, int> result = new Dictionary<string, int>();
            lock (Map)
            {
                foreach (KeyValuePair<string, string> kv in Map)
                {
                    if (!kv.Key.StartsWith(KAutoAttachPrefix)) continue;
                    string busId = kv.Key.Substring(KAutoAttachPrefix.Length);
                    int pid;
                    if (busId.Length > 0 && int.TryParse(kv.Value, out pid))
                        result[busId] = pid;
                }
            }
            return result;
        }

        public static void SetAutoAttach(Dictionary<string, int> map)
        {
            lock (Map)
            {
                List<string> stale = new List<string>();
                foreach (string k in Map.Keys)
                    if (k.StartsWith(KAutoAttachPrefix)) stale.Add(k);
                foreach (string k in stale) Map.Remove(k);
                foreach (KeyValuePair<string, int> kv in map)
                    Map[KAutoAttachPrefix + kv.Key] = kv.Value.ToString();
            }
        }
    }
}
