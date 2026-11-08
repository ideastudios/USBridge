using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WslUsbManager
{
    /// <summary>
    /// WSL 命令封装（注意 wsl.exe 输出为 UTF-16，解码由 ProcessRunner 统一处理）
    /// </summary>
    public static class Wsl
    {
        /// <summary>
        /// 列出全部发行版名称
        /// </summary>
        public static List<string> ListDistros()
        {
            List<string> result = new List<string>();
            try
            {
                RunResult r = ProcessRunner.Run("wsl.exe", "--list --quiet", 30000);
                if (r.ExitCode != 0 || r.StdOut == null) return result;
                string[] lines = r.StdOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string t = line.Trim().Trim('\0').Trim();
                    if (t.Length == 0) continue;
                    if (t.Contains(" ")) continue; // 干扰行（错误提示等）不含合法发行版名
                    result.Add(t);
                }
            }
            catch { }
            return result;
        }

        public static bool IsInstalled()
        {
            try
            {
                RunResult r = ProcessRunner.Run("wsl.exe", "--status", 20000);
                return r.ExitCode == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// 在指定发行版（空 = 默认）内以 root 执行 bash 脚本
        /// </summary>
        public static Task<RunResult> RunRootAsync(string distro, string bashScript, int timeoutMs)
        {
            string args = "";
            if (!string.IsNullOrEmpty(distro)) args += "-d " + Quote(distro) + " ";
            args += "-u root -- bash -c " + Quote(bashScript);
            return ProcessRunner.RunAsync("wsl.exe", args, timeoutMs);
        }

        /// <summary>
        /// 加载 usbip 内核模块（vhci_hcd），失败通常代表已内置于内核，可忽略
        /// </summary>
        public static Task<RunResult> LoadVhciAsync(string distro)
        {
            return RunRootAsync(distro,
                "modprobe vhci_hcd 2>/dev/null; modprobe vhci-hcd 2>/dev/null; exit 0",
                25000);
        }

        /// <summary>
        /// 在 WSL 内重启 adb 服务并列出设备（以 root 运行，避免 udev 权限问题）。
        /// 若 WSL 未安装 adb，输出中会带 __WUSBM_NOADB__ 标记。
        /// </summary>
        public static Task<RunResult> RestartAdbAsync(string distro)
        {
            return RunRootAsync(distro,
                "command -v adb >/dev/null 2>&1 || { echo __WUSBM_NOADB__; exit 0; }; " +
                "adb kill-server >/dev/null 2>&1; adb start-server >/dev/null 2>&1; adb devices -l",
                45000);
        }

        private static string Quote(string s)
        {
            return "\"" + s + "\"";
        }
    }
}
