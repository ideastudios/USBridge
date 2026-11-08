using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;

namespace WslUsbManager
{
    public enum DeviceStateKind { Unknown, NotShared, Shared, Attached, AttachedKeep }

    /// <summary>
    /// 一台 USB 设备（usbipd list 的一行），并携带 UI 所需的展示信息
    /// </summary>
    public class UsbDevice
    {
        public UsbDevice()
        {
            BusId = "";
            VidPid = "";
            Description = "";
            StateText = "";
        }

        // 注意：WPF 绑定只认属性，不能是字段
        public string BusId { get; set; }
        public string VidPid { get; set; }
        public string Description { get; set; }
        public DeviceStateKind State { get; set; }
        public bool IsAndroid { get; set; }
        public bool IsAutoAttach { get; set; }

        // 展示信息（由 Theme.ApplyDeviceVisuals 在 UI 线程填充）
        public string StateText { get; set; }
        public Brush StateFg { get; set; }
        public Brush StateBg { get; set; }

        public bool IsAttached
        {
            get { return State == DeviceStateKind.Attached || State == DeviceStateKind.AttachedKeep; }
        }

        public bool IsShared
        {
            get { return State == DeviceStateKind.Shared || State == DeviceStateKind.Attached || State == DeviceStateKind.AttachedKeep; }
        }

        public string QuickActionText
        {
            get
            {
                if (IsAttached) return "断开连接";
                if (State == DeviceStateKind.NotShared) return "共享并连接";
                return "连接到 WSL";
            }
        }

        public void ComputeAndroid()
        {
            string d = (Description ?? "").ToLowerInvariant();
            string v = (VidPid ?? "").ToLowerInvariant();
            IsAndroid =
                d.Contains("android") || d.Contains("adb") || d.Contains("mtp") ||
                d.Contains("hdb") || v.StartsWith("18d1:4ee");
        }
    }

    /// <summary>
    /// usbipd-win 命令行封装
    /// </summary>
    public static class Usbipd
    {
        public static string ExePath;
        public static string VersionText = "";
        private static bool _resolved;

        public static bool Available { get { return ExePath != null; } }

        public static void ResetResolve()
        {
            _resolved = false;
            ExePath = null;
            VersionText = "";
        }

        /// <summary>
        /// 查找便携包内置的 usbipd-win 安装包（exe 目录下 tools\usbipd\*.msi）
        /// </summary>
        public static string FindBundledMsi()
        {
            try
            {
                string dir = Path.Combine(App.BaseDir, "tools", "usbipd");
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "*.msi");
                    if (files.Length > 0) return files[0];
                }
            }
            catch { }
            return null;
        }

        public static string ResolveExe()
        {
            if (_resolved) return ExePath;
            _resolved = true;

            try
            {
                RunResult r = ProcessRunner.Run("where.exe", "usbipd.exe", 5000);
                if (r.Success && r.StdOut != null)
                {
                    string[] lines = r.StdOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        string t = line.Trim();
                        if (t.Length > 0 && File.Exists(t)) { ExePath = t; break; }
                    }
                }
            }
            catch { }

            if (ExePath == null)
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
                string lf = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string[] candidates = new string[]
                {
                    Path.Combine(pf, "usbipd-win", "usbipd.exe"),
                    pf86 == null ? "" : Path.Combine(pf86, "usbipd-win", "usbipd.exe"),
                    Path.Combine(lf, "usbipd-win", "usbipd.exe")
                };
                foreach (string c in candidates)
                {
                    if (c.Length > 0 && File.Exists(c)) { ExePath = c; break; }
                }
            }
            return ExePath;
        }

        public static async Task<string> DetectVersionAsync()
        {
            if (!Available) return "";
            try
            {
                RunResult r = await ProcessRunner.RunAsync(ExePath, "--version", 10000);
                if (r.Success && r.StdOut != null && r.StdOut.Length > 0)
                {
                    string first = r.StdOut.Split(new char[] { '\r', '\n' })[0].Trim();
                    // 形如 5.3.0-54+Branch... → 取主版本号
                    int dash = first.IndexOf('-');
                    if (dash > 0) first = first.Substring(0, dash);
                    VersionText = first;
                }
            }
            catch { }
            return VersionText;
        }

        // ---- 操作命令 ----

        public static Task<RunResult> BindAsync(string busId, bool elevated)
        {
            return RunArgs("bind --busid " + busId, elevated);
        }

        public static Task<RunResult> UnbindAsync(string busId, bool elevated)
        {
            return RunArgs("unbind --busid " + busId, elevated);
        }

        public static Task<RunResult> AttachAsync(string busId, string distro, bool autoAttach)
        {
            string args = "attach --wsl --busid " + busId;
            if (!string.IsNullOrEmpty(distro)) args += " --distribution " + Quote(distro);
            if (autoAttach) args += " --auto-attach";
            return RunArgs(args, false);
        }

        public static Task<RunResult> DetachAsync(string busId)
        {
            return RunArgs("detach --busid " + busId, false);
        }

        public static Process SpawnAutoAttach(string busId, string distro, Action<string> onLine, Action<string> onErrLine)
        {
            string args = "attach --wsl --auto-attach --busid " + busId;
            if (!string.IsNullOrEmpty(distro)) args += " --distribution " + Quote(distro);
            return ProcessRunner.SpawnDetached(ExePath, args, onLine, onErrLine);
        }

        private static Task<RunResult> RunArgs(string args, bool elevated)
        {
            if (!elevated) return ProcessRunner.RunAsync(ExePath, args, 30000);
            return Task.Run<RunResult>(delegate { return ProcessRunner.RunElevated(ExePath, args, 60000); });
        }

        private static string Quote(string s)
        {
            return "\"" + s + "\"";
        }

        // ---- 解析 usbipd list ----

        public static async Task<List<UsbDevice>> ListAsync()
        {
            RunResult r = await ProcessRunner.RunAsync(ExePath, "list", 20000);
            if (r.TimedOut) throw new Exception("usbipd list 超时");
            if (r.ExitCode != 0 && (r.StdOut == null || r.StdOut.Trim().Length == 0))
                throw new Exception("usbipd list 失败: " + r.Combined);
            return ParseDevices(r.StdOut ?? "");
        }

        public static List<UsbDevice> ParseDevices(string text)
        {
            List<UsbDevice> list = new List<UsbDevice>();
            Regex row = new Regex(@"^(\d+(?:[-.]\d+)+)\s+([0-9a-fA-F]{4}:[0-9a-fA-F]{4})\s+(.+)$");
            string[] states = new string[] { "Attached-keep", "Not shared", "Attached", "Shared" };

            string[] lines = (text ?? "").Split(new char[] { '\r', '\n' });
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                Match m = row.Match(line);
                if (!m.Success) continue;

                UsbDevice d = new UsbDevice();
                d.BusId = m.Groups[1].Value;
                d.VidPid = m.Groups[2].Value.ToLowerInvariant();
                string rest = m.Groups[3].Value.TrimEnd();

                d.State = DeviceStateKind.Unknown;
                foreach (string s in states)
                {
                    int idx = rest.LastIndexOf(s, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        string before = rest.Substring(0, idx).TrimEnd();
                        string after = rest.Substring(idx + s.Length).Trim();
                        // 仅当状态串位于行尾（其后无内容）且与前文之间有空白分隔时才认定
                        if (after.Length == 0 && (before.Length == 0 || rest.Length - s.Length > before.Length))
                        {
                            d.Description = before;
                            if (s.Equals("Attached-keep", StringComparison.OrdinalIgnoreCase)) d.State = DeviceStateKind.AttachedKeep;
                            else if (s.Equals("Not shared", StringComparison.OrdinalIgnoreCase)) d.State = DeviceStateKind.NotShared;
                            else if (s.Equals("Attached", StringComparison.OrdinalIgnoreCase)) d.State = DeviceStateKind.Attached;
                            else d.State = DeviceStateKind.Shared;
                            break;
                        }
                    }
                }
                if (d.State == DeviceStateKind.Unknown) d.Description = rest;
                if (d.Description.Length == 0) d.Description = "(未命名设备)";

                d.ComputeAndroid();
                list.Add(d);
            }
            return list;
        }
    }
}
