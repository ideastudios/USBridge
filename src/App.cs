using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WslUsbManager
{
    internal static class App
    {
        public const string AppName = "USBridge";
        public const string AppNameCn = "WSL USB 管家";
        public const string AppVersion = "1.2.0";
        public const string UsbipdHome = "https://github.com/dorssel/usbipd-win/releases";
        public const string ReferenceProject = "https://github.com/nickbeth/wsl-usb-manager";

        private static Mutex _mutex;
        private static string _baseDir;

        /// <summary>exe 所在目录（便携包内 adb / usbipd 安装包的查找基准）</summary>
        public static string BaseDir
        {
            get
            {
                if (_baseDir == null)
                {
                    try
                    {
                        _baseDir = Path.GetDirectoryName(
                            System.Reflection.Assembly.GetExecutingAssembly().Location);
                    }
                    catch { _baseDir = ""; }
                }
                return _baseDir == null ? "" : _baseDir;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [STAThread]
        private static void Main()
        {
            bool created = false;
            _mutex = new Mutex(true, "WslUsbManagerPro_SingleInstance", out created);
            if (!created)
            {
                // 单实例设计：再次启动时把已运行的窗口带到前台（避免两实例互抢自动附加记录）
                ActivateExistingInstance();
                return;
            }

            Application app = new Application();
            app.DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTask;

            Settings.Load();

            // 主题：默认深色；可用 --light / --dark 命令行参数临时强制（不写入设置）
            bool? forceDark = null;
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (a == "--light") forceDark = false;
                else if (a == "--dark") forceDark = true;
            }
            Theme.Apply(forceDark.HasValue ? forceDark.Value : Settings.GetBool(Settings.KDarkTheme, true));

            Window win = null;
            try
            {
                win = MainWindowController.Create();
            }
            catch (Exception ex)
            {
                LogCrash("初始化", ex);
                MessageBox.Show("界面初始化失败：\n" + ex.Message, AppName);
                return;
            }

            app.Run(win);
            GC.KeepAlive(_mutex);
        }

        private static void OnDispatcherException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash("UI", e.Exception);
            MessageBox.Show("发生未处理的异常，已忽略：\n\n" + e.Exception.Message, AppName);
            e.Handled = true;
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null) LogCrash("AppDomain", ex);
        }

        private static void OnUnobservedTask(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogCrash("Task", e.Exception);
            e.SetObserved();
        }

        private static void ActivateExistingInstance()
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                foreach (Process p in Process.GetProcessesByName(current.ProcessName))
                {
                    if (p.Id == current.Id) continue;
                    IntPtr h = IntPtr.Zero;
                    try { h = p.MainWindowHandle; } catch { }
                    if (h != IntPtr.Zero)
                    {
                        if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
                        SetForegroundWindow(h);
                        p.Dispose();
                        return;
                    }
                    p.Dispose();
                }
            }
            catch { }
            MessageBox.Show(AppName + " 已在运行。", AppName);
        }

        public static void LogCrash(string tag, Exception ex)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WslUsbManagerPro");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + tag + "] " + ex + "\r\n\r\n");
            }
            catch { }
        }

        public static void OpenUrl(string url)
        {
            try { ProcessStart(url); }
            catch { }
        }

        private static void ProcessStart(string url)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// 应用图标：优先加载内嵌的 assets\logo.ico（品牌 logo，多尺寸），
        /// 无内嵌资源时回退到程序化绘制的简版图形。
        /// </summary>
        public static ImageSource CreateAppIcon()
        {
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (System.IO.Stream s = asm.GetManifestResourceStream("App.logo.ico"))
                {
                    if (s != null)
                    {
                        System.Drawing.Icon ico = null;
                        try
                        {
                            try { ico = new System.Drawing.Icon(s, 256, 256); }
                            catch { s.Position = 0; ico = new System.Drawing.Icon(s); }
                            using (System.Drawing.Bitmap bmp = ico.ToBitmap())
                            {
                                IntPtr hBmp = bmp.GetHbitmap();
                                try
                                {
                                    BitmapSource bs = Imaging.CreateBitmapSourceFromHBitmap(
                                        hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                                    bs.Freeze();
                                    return bs;
                                }
                                finally { DeleteObject(hBmp); }
                            }
                        }
                        finally { if (ico != null) ico.Dispose(); }
                    }
                }
            }
            catch { }
            return CreateLegacyIcon();
        }

        /// <summary>兜底图标（深色圆角底 + 白色 USB 三叉戟简版）</summary>
        private static ImageSource CreateLegacyIcon()
        {
            try
            {
                DrawingVisual dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    Brush bg = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB));
                    dc.DrawRoundedRectangle(bg, null, new Rect(1, 1, 30, 30), 8, 8);

                    Pen w = new Pen(Brushes.White, 2.6);
                    w.StartLineCap = PenLineCap.Round;
                    w.EndLineCap = PenLineCap.Round;
                    w.LineJoin = PenLineJoin.Round;

                    dc.DrawLine(w, new Point(16, 9), new Point(16, 24));
                    dc.DrawLine(w, new Point(13.2, 12), new Point(16, 9));
                    dc.DrawLine(w, new Point(16, 9), new Point(18.8, 12));
                    dc.DrawLine(w, new Point(16, 15), new Point(11.5, 19));
                    dc.DrawLine(w, new Point(11.5, 19), new Point(11.5, 20.6));
                    dc.DrawEllipse(null, w, new Point(11.5, 23.0), 1.9, 1.9);
                    dc.DrawLine(w, new Point(16, 15), new Point(20.5, 19));
                    dc.DrawLine(w, new Point(20.5, 19), new Point(20.5, 20.6));
                    dc.DrawRectangle(null, w, new Rect(18.9, 20.7, 3.2, 3.2));
                }
                RenderTargetBitmap rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                return rtb;
            }
            catch { return null; }
        }
    }
}
