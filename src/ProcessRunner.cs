using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WslUsbManager
{
    /// <summary>
    /// 一次外部命令的执行结果
    /// </summary>
    public class RunResult
    {
        public int ExitCode;
        public string StdOut = "";
        public string StdErr = "";
        public bool TimedOut;
        public bool Cancelled;      // UAC 授权被用户取消
        public bool StartFailed;    // 进程未能启动

        public bool Success { get { return !TimedOut && !Cancelled && !StartFailed && ExitCode == 0; } }

        public string Combined
        {
            get
            {
                string o = StdOut == null ? "" : StdOut.Trim();
                string e = StdErr == null ? "" : StdErr.Trim();
                if (o.Length == 0) return e;
                if (e.Length == 0) return o;
                return o + "\n" + e;
            }
        }
    }

    /// <summary>
    /// 外部进程执行器：隐藏窗口、字节级输出解码（自动识别 UTF-8 / UTF-16 / ANSI）、超时控制、UAC 提权捕获
    /// </summary>
    public static class ProcessRunner
    {
        /// <summary>
        /// 智能解码：wsl.exe 输出 UTF-16LE，usbipd/adb 输出 UTF-8，兜底系统 ANSI
        /// </summary>
        public static string DecodeBytes(byte[] b)
        {
            if (b == null || b.Length == 0) return "";
            // UTF-16 BOM
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
                return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            // 无 BOM 的 UTF-16 启发式（wsl.exe）：前 128 字节中大量 0x00。
            // 注意必须先于 UTF-8 判断——UTF-16LE 编码的 ASCII 恰好也是合法 UTF-8。
            int n = Math.Min(b.Length, 128);
            int zeros = 0;
            for (int i = 0; i < n; i++) if (b[i] == 0) zeros++;
            if (zeros > n / 4 && b.Length % 2 == 0)
                return Encoding.Unicode.GetString(b);
            // 严格 UTF-8（usbipd / adb）
            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                return strict.GetString(b);
            }
            catch { }
            // 系统 ANSI（中文系统为 GBK）
            try { return Encoding.GetEncoding(0).GetString(b); }
            catch { return Encoding.Default.GetString(b); }
        }

        public static Task<RunResult> RunAsync(string exe, string args, int timeoutMs)
        {
            return Task.Run<RunResult>(delegate { return Run(exe, args, timeoutMs); });
        }

        public static RunResult Run(string exe, string args, int timeoutMs)
        {
            RunResult r = new RunResult();
            Process p = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                p = Process.Start(psi);

                Task<byte[]> tOut = Task.Factory.StartNew<byte[]>(delegate
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        p.StandardOutput.BaseStream.CopyTo(ms);
                        return ms.ToArray();
                    }
                });
                Task<byte[]> tErr = Task.Factory.StartNew<byte[]>(delegate
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        p.StandardError.BaseStream.CopyTo(ms);
                        return ms.ToArray();
                    }
                });

                bool done = p.WaitForExit(timeoutMs);
                if (done)
                {
                    p.WaitForExit(); // 确保异步输出刷新完毕
                }
                else
                {
                    try { p.Kill(); } catch { }
                    p.WaitForExit(3000);
                }

                try { Task.WaitAll(new Task[] { tOut, tErr }, 5000); }
                catch { }

                r.TimedOut = !done;
                try { r.ExitCode = p.ExitCode; } catch { r.ExitCode = -1; }
                try { r.StdOut = DecodeBytes(tOut.Result); } catch { r.StdOut = ""; }
                try { r.StdErr = DecodeBytes(tErr.Result); } catch { r.StdErr = ""; }
            }
            catch (Exception ex)
            {
                r.StartFailed = true;
                r.ExitCode = -1;
                r.StdErr = "启动进程失败: " + ex.Message;
            }
            finally
            {
                try { if (p != null) p.Dispose(); } catch { }
            }
            return r;
        }

        /// <summary>
        /// 以管理员身份运行并通过临时批处理捕获输出（会触发 UAC 弹窗）。
        /// 若用户在 UAC 中点取消，返回 Cancelled = true。
        /// </summary>
        public static RunResult RunElevated(string exe, string args, int timeoutMs)
        {
            RunResult r = new RunResult();
            string id = Guid.NewGuid().ToString("N");
            string outFile = Path.Combine(Path.GetTempPath(), "wusbm_" + id + ".out");
            string batFile = Path.Combine(Path.GetTempPath(), "wusbm_" + id + ".cmd");
            try
            {
                // 批处理用系统 ANSI 编码写入（cmd 按 ANSI 读取），路径含空格均已加引号
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.Append('"').Append(exe).Append("\" ").Append(args)
                  .Append(" > \"").Append(outFile).Append("\" 2>&1").AppendLine();
                sb.AppendLine("exit /b %errorlevel%");
                File.WriteAllText(batFile, sb.ToString(), Encoding.GetEncoding(0));

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = batFile;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;

                Process p = null;
                try
                {
                    p = Process.Start(psi);
                }
                catch (Win32Exception w)
                {
                    if (w.NativeErrorCode == 1223) // ERROR_CANCELLED
                    {
                        r.Cancelled = true;
                        r.ExitCode = 1223;
                        return r;
                    }
                    throw;
                }

                bool done = p.WaitForExit(timeoutMs);
                if (!done)
                {
                    try { p.Kill(); } catch { }
                    p.WaitForExit(3000);
                    r.TimedOut = true;
                }
                try { r.ExitCode = p.ExitCode; } catch { r.ExitCode = -1; }
                p.Dispose();

                if (File.Exists(outFile))
                    r.StdOut = DecodeBytes(File.ReadAllBytes(outFile));
            }
            catch (Win32Exception w)
            {
                if (w.NativeErrorCode == 1223) { r.Cancelled = true; }
                else { r.StartFailed = true; r.StdErr = "提权执行失败: " + w.Message; }
            }
            catch (Exception ex)
            {
                r.StartFailed = true;
                r.StdErr = "提权执行失败: " + ex.Message;
            }
            finally
            {
                try { if (File.Exists(batFile)) File.Delete(batFile); } catch { }
                try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }
            }
            return r;
        }

        /// <summary>
        /// 启动一个脱离本程序生命周期运行的后台进程（用于 usbipd --auto-attach）
        /// </summary>
        public static Process SpawnDetached(string exe, string args, Action<string> onLine, Action<string> onErrLine)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            Process p = Process.Start(psi);
            if (onLine != null)
            {
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) onLine(e.Data);
                };
                p.BeginOutputReadLine();
            }
            if (onErrLine != null)
            {
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) onErrLine(e.Data);
                };
                p.BeginErrorReadLine();
            }
            return p;
        }
    }
}
