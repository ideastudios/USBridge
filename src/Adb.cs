using System;
using System.IO;
using System.Threading.Tasks;

namespace WslUsbManager
{
    /// <summary>
    /// Windows 端 adb.exe 定位与操作
    /// </summary>
    public static class Adb
    {
        private static string _cached;
        private static bool _resolved;

        /// <summary>
        /// 查找 adb.exe：设置路径 → 便携包 tools\adb → PATH → 常见 SDK 位置
        /// </summary>
        public static string Resolve()
        {
            if (_resolved) return _cached;
            _resolved = true;

            string s = Settings.Get(Settings.KAdbPath, "");
            if (s.Length > 0 && File.Exists(s)) { _cached = s; return _cached; }

            // 便携包内置的 adb（exe 目录下 tools\adb\）
            string bundled = Path.Combine(App.BaseDir, "tools", "adb", "adb.exe");
            if (File.Exists(bundled)) { _cached = bundled; return _cached; }

            try
            {
                RunResult r = ProcessRunner.Run("where.exe", "adb.exe", 5000);
                if (r.Success && r.StdOut != null)
                {
                    string[] lines = r.StdOut.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        string t = line.Trim();
                        if (t.Length > 0 && File.Exists(t)) { _cached = t; return _cached; }
                    }
                }
            }
            catch { }

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] candidates = new string[]
            {
                Path.Combine(local, "Android", "Sdk", "platform-tools", "adb.exe"),
                Path.Combine(pf, "Android", "android-sdk", "platform-tools", "adb.exe")
            };
            foreach (string c in candidates)
            {
                if (File.Exists(c)) { _cached = c; return _cached; }
            }
            return null;
        }

        public static void Invalidate() { _resolved = false; _cached = null; }

        /// <summary>
        /// 结束 Windows 端 adb 服务（释放对设备的占用，避免 WSL 检测不到）
        /// </summary>
        public static Task<RunResult> KillServerAsync(string adbPath)
        {
            return ProcessRunner.RunAsync(adbPath, "kill-server", 10000);
        }

        /// <summary>
        /// 启动 Windows 端 adb 服务（断开连接后调用，让 Windows 立即恢复 adb 设备可见性）
        /// </summary>
        public static Task<RunResult> StartServerAsync(string adbPath)
        {
            return ProcessRunner.RunAsync(adbPath, "start-server", 10000);
        }
    }
}
