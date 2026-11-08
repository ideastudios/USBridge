using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace WslUsbManager
{
    /// <summary>
    /// 主窗口控制器：加载运行时 XAML、接线事件、执行所有业务流程
    /// </summary>
    public class MainWindowController
    {
        private readonly Window _win;

        // ---- 主窗口控件 ----
        private Border UsbipdBanner; private Button UsbipdDlBtn; private Button InstallUsbipdBtn;
        private Border AdminBanner; private Button ElevateBtn;
        private Button RefreshBtn; private CheckBox AutoRefreshCheck;
        private ComboBox DistroCombo; private CheckBox AdbCheck;
        private Button FixAdbBtn; private Button SettingsBtn;
        private Button ThemeToggleBtn;
        private ListBox DeviceList; private TextBlock EmptyHint;
        private Button BindBtn; private Button UnbindBtn;
        private Button AttachBtn; private Button DetachBtn; private Button AutoAttachBtn;
        private Button LogHeaderBtn; private Button ClearLogBtn;
        private Border LogPanel; private RichTextBox LogBox;
        private ProgressBar BusyBar; private TextBlock StatusText; private TextBlock MetaText;

        // ---- 运行状态 ----
        private readonly DispatcherTimer _timer;           // 周期自动刷新
        private readonly DispatcherTimer _debounce;        // 设备插拔去抖
        private bool _busy;
        private bool _refreshing;
        private bool _populatingDistro;
        private bool _realExit;
        private bool _trayBalloonShown;
        private TrayController _tray;
        private string _selectedBusId;
        private string _lastDistroTag = "";
        private readonly Dictionary<string, int> _autoAttach = new Dictionary<string, int>();

        // ---- 日志画刷（随主题实时解析）----
        private static Brush LogTime { get { return Theme.Brush("LogTime", "#555D66"); } }
        private static Brush LogInfo { get { return Theme.Brush("LogInfo", "#C9D1D9"); } }
        private static Brush LogDim { get { return Theme.Brush("LogDim", "#6E7681"); } }
        private static Brush LogOk { get { return Theme.Brush("LogOk", "#3FB950"); } }
        private static Brush LogWarn { get { return Theme.Brush("LogWarn", "#D29922"); } }
        private static Brush LogErr { get { return Theme.Brush("LogErr", "#F85149"); } }
        private static Brush LogCmd { get { return Theme.Brush("LogCmd", "#79C0FF"); } }

        private static readonly FontFamily MonoFont = new FontFamily("Consolas");

        private MainWindowController(Window win)
        {
            _win = win;
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(3);
            _timer.Tick += OnTimerTick;
            _debounce = new DispatcherTimer();
            _debounce.Interval = TimeSpan.FromMilliseconds(900);
            _debounce.Tick += OnDebounceTick;
        }

        /// <summary>加载内嵌 XAML 并构建主窗口</summary>
        public static Window Create()
        {
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
            Window win;
            using (System.IO.Stream s = asm.GetManifestResourceStream("MainWindow.xaml"))
            using (System.IO.StreamReader sr = new System.IO.StreamReader(s, Encoding.UTF8))
            {
                win = (Window)XamlReader.Load(s);
            }
            MainWindowController c = new MainWindowController(win);
            c.Initialize();
            return win;
        }

        private static Window LoadDialog(string resourceName)
        {
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
            using (System.IO.Stream s = asm.GetManifestResourceStream(resourceName))
            {
                return (Window)XamlReader.Load(s);
            }
        }

        // ================= 初始化 =================

        private void Initialize()
        {
            UsbipdBanner = (Border)_win.FindName("UsbipdBanner");
            UsbipdDlBtn = (Button)_win.FindName("UsbipdDlBtn");
            InstallUsbipdBtn = (Button)_win.FindName("InstallUsbipdBtn");
            AdminBanner = (Border)_win.FindName("AdminBanner");
            ElevateBtn = (Button)_win.FindName("ElevateBtn");
            RefreshBtn = (Button)_win.FindName("RefreshBtn");
            AutoRefreshCheck = (CheckBox)_win.FindName("AutoRefreshCheck");
            DistroCombo = (ComboBox)_win.FindName("DistroCombo");
            AdbCheck = (CheckBox)_win.FindName("AdbCheck");
            FixAdbBtn = (Button)_win.FindName("FixAdbBtn");
            SettingsBtn = (Button)_win.FindName("SettingsBtn");
            ThemeToggleBtn = (Button)_win.FindName("ThemeToggleBtn");
            DeviceList = (ListBox)_win.FindName("DeviceList");
            EmptyHint = (TextBlock)_win.FindName("EmptyHint");
            BindBtn = (Button)_win.FindName("BindBtn");
            UnbindBtn = (Button)_win.FindName("UnbindBtn");
            AttachBtn = (Button)_win.FindName("AttachBtn");
            DetachBtn = (Button)_win.FindName("DetachBtn");
            AutoAttachBtn = (Button)_win.FindName("AutoAttachBtn");
            LogHeaderBtn = (Button)_win.FindName("LogHeaderBtn");
            ClearLogBtn = (Button)_win.FindName("ClearLogBtn");
            LogPanel = (Border)_win.FindName("LogPanel");
            LogBox = (RichTextBox)_win.FindName("LogBox");
            BusyBar = (ProgressBar)_win.FindName("BusyBar");
            StatusText = (TextBlock)_win.FindName("StatusText");
            MetaText = (TextBlock)_win.FindName("MetaText");

            _win.Icon = App.CreateAppIcon();
            _win.SourceInitialized += OnSourceInitialized;
            _win.Loaded += OnLoaded;
            _win.Closing += OnClosing;
            _win.KeyDown += OnKeyDown;
            _win.StateChanged += OnStateChanged;

            // 系统托盘
            _tray = new TrayController();
            ApplyTrayVisibility();

            RefreshBtn.Click += delegate { DoUserRefresh(); };
            UsbipdDlBtn.Click += delegate { App.OpenUrl(App.UsbipdHome); };
            InstallUsbipdBtn.Click += delegate { InstallUsbipdFromBundle(); };
            ElevateBtn.Click += delegate { RestartAsAdmin(); };
            InstallUsbipdBtn.Visibility = Usbipd.FindBundledMsi() != null ? Visibility.Visible : Visibility.Collapsed;
            FixAdbBtn.Click += delegate { RunFixAdbFlow(); };
            SettingsBtn.Click += delegate { ShowSettings(); };
            ThemeToggleBtn.Click += delegate { ToggleTheme(); };
            UpdateThemeButton();

            AutoRefreshCheck.Checked += delegate { OnAutoRefreshToggled(); };
            AutoRefreshCheck.Unchecked += delegate { OnAutoRefreshToggled(); };

            AdbCheck.Checked += delegate { Settings.SetBool(Settings.KAdbFixOnAttach, true); Settings.Save(); };
            AdbCheck.Unchecked += delegate { Settings.SetBool(Settings.KAdbFixOnAttach, false); Settings.Save(); };
            DistroCombo.SelectionChanged += OnDistroChanged;

            BindBtn.Click += delegate { RunBindFlow(); };
            UnbindBtn.Click += delegate { RunUnbindFlow(); };
            AttachBtn.Click += delegate { RunAttachFromButton(); };
            DetachBtn.Click += delegate { RunDetachFromButton(); };
            AutoAttachBtn.Click += delegate { RunToggleAutoAttach(); };

            DeviceList.SelectionChanged += OnSelectionChanged;
            DeviceList.MouseDoubleClick += OnListDoubleClick;
            DeviceList.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnListQuickClick));

            LogHeaderBtn.Click += delegate { ToggleLogPanel(); };
            ClearLogBtn.Click += delegate
            {
                LogBox.Document.Blocks.Clear();
            };

            // 状态应用
            AutoRefreshCheck.IsChecked = Settings.GetBool(Settings.KAutoRefresh, true);
            AdbCheck.IsChecked = Settings.GetBool(Settings.KAdbFixOnAttach, true);
            bool logOpen = Settings.GetBool(Settings.KLogOpen, true);
            LogPanel.Visibility = logOpen ? Visibility.Visible : Visibility.Collapsed;
            LogHeaderBtn.Content = logOpen ? "▾ 运行日志" : "▸ 运行日志";
            _timer.IsEnabled = AutoRefreshCheck.IsChecked == true;

            _autoAttach.Clear();
            foreach (KeyValuePair<string, int> kv in Settings.GetAutoAttach())
                _autoAttach[kv.Key] = kv.Value;

            FlowDocument doc = new FlowDocument();
            doc.PagePadding = new Thickness(0);
            LogBox.Document = doc;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            Theme.ApplyWindowChrome(_win);
            IntPtr hwnd = new WindowInteropHelper(_win).Handle;
            HwndSource src = HwndSource.FromHwnd(hwnd);
            if (src != null) src.AddHook(WndProc);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            AdminBanner.Visibility = IsElevated() ? Visibility.Collapsed : Visibility.Visible;

            Log(App.AppName + " v" + App.AppVersion + "（" + App.AppNameCn + "）", LogOk);
            Log("提示：双击设备行可一键连接 / 断开；「ADB 模式」自动解决 Android 设备连接后 WSL 检测不到的问题。", LogDim);

            if (Usbipd.ResolveExe() == null)
            {
                UsbipdBanner.Visibility = Visibility.Visible;
                SetActionsEnabled(false);
                Log("未找到 usbipd.exe，请安装 usbipd-win 后重启本程序。", LogErr);
                return;
            }

            Log("usbipd: " + Usbipd.ExePath, LogDim);
            string adb = Adb.Resolve();
            Log(adb != null ? "adb: " + adb : "未找到 adb.exe（可到设置中手动指定路径）", adb != null ? LogDim : LogWarn);

            await Usbipd.DetectVersionAsync();
            await RefreshSafeAsync();
            await PopulateDistroComboAsync();
            UpdateMeta();

            Log("就绪。", LogDim);

            // 供测试/演示：--settings 启动时直接打开设置窗口
            string[] args = Environment.GetCommandLineArgs();
            foreach (string a in args)
                if (a == "--settings") { ShowSettings(); break; }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            // 「关闭到托盘」：拦截关闭，仅隐藏窗口（从托盘菜单退出）
            if (!_realExit && Settings.GetBool(Settings.KTrayIcon, true) &&
                Settings.GetBool(Settings.KTrayClose, false))
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _timer.Stop();
            _debounce.Stop();
            if (_tray != null) { _tray.Dispose(); _tray = null; }
            Settings.Save();
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (_win.WindowState == WindowState.Minimized &&
                Settings.GetBool(Settings.KTrayIcon, true) &&
                Settings.GetBool(Settings.KTrayMinimize, true))
            {
                HideToTray();
            }
        }

        private void HideToTray()
        {
            // 注意顺序：先把最小化状态复位（窗口尚可见，瞬闪不可感知），再 Hide。
            // 若先 Hide 再改 WindowState，WPF 会对隐藏窗口调用 ShowWindow 导致窗口重新弹出。
            if (_win.WindowState == WindowState.Minimized)
                _win.WindowState = WindowState.Normal;
            _win.Hide();
            if (!_trayBalloonShown)
            {
                _trayBalloonShown = true;
                if (_tray != null && _tray.Initialized)
                    _tray.ShowBalloon(App.AppName, "已隐藏到系统托盘，点击托盘图标可重新打开。");
            }
        }

        private void ShowMainWindow()
        {
            _win.Show();
            if (_win.WindowState == WindowState.Minimized)
                _win.WindowState = WindowState.Normal;
            _win.Activate();
        }

        private void ExitFromTray()
        {
            _realExit = true;
            _win.Close();
        }

        private void ApplyTrayVisibility()
        {
            bool enabled = Settings.GetBool(Settings.KTrayIcon, true);
            if (enabled && !_tray.Initialized)
            {
                _tray.Init(App.CreateAppIcon(), App.AppName + " · " + App.AppNameCn,
                    delegate { ShowMainWindow(); },
                    delegate { DoUserRefresh(); },
                    delegate { RunFixAdbFlow(); },
                    delegate { ToggleTheme(); },
                    delegate { ExitFromTray(); });
            }
            _tray.Visible = enabled;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5) DoUserRefresh();
        }

        private void OnAutoRefreshToggled()
        {
            bool on = AutoRefreshCheck.IsChecked == true;
            Settings.SetBool(Settings.KAutoRefresh, on);
            Settings.Save();
            _timer.IsEnabled = on;
        }

        private void ToggleTheme()
        {
            bool dark = !Theme.IsDark;
            Theme.Apply(dark);
            Settings.SetBool(Settings.KDarkTheme, dark);
            Settings.Save();
            Theme.ApplyWindowChrome(_win);
            UpdateThemeButton();
            Log(dark ? "已切换到深色模式。" : "已切换到浅色模式。", LogDim);

            // 重新套用设备行状态徽章配色（无需重新查询 usbipd）
            List<UsbDevice> items = DeviceList.ItemsSource as List<UsbDevice>;
            if (items != null)
            {
                foreach (UsbDevice d in items)
                    Theme.ApplyDeviceVisuals(d);
                DeviceList.Items.Refresh();
            }
        }

        private void UpdateThemeButton()
        {
            ThemeToggleBtn.Content = Theme.IsDark ? "浅色模式" : "深色模式";
        }

        private void ToggleLogPanel()
        {
            bool open = LogPanel.Visibility != Visibility.Visible;
            LogPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            LogHeaderBtn.Content = open ? "▾ 运行日志" : "▸ 运行日志";
            Settings.SetBool(Settings.KLogOpen, open);
            Settings.Save();
        }

        // ================= WMI / 消息钩子 =================

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVNODES_CHANGED = 0x0007;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE && wParam.ToInt64() == DBT_DEVNODES_CHANGED)
            {
                _debounce.Stop();
                _debounce.Start();
            }
            return IntPtr.Zero;
        }

        private void OnDebounceTick(object sender, EventArgs e)
        {
            _debounce.Stop();
            if (!_busy && !_refreshing && Usbipd.Available)
                FireRefresh(false);
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (!_busy && !_refreshing && Usbipd.Available && AutoRefreshCheck.IsChecked == true)
                FireRefresh(false);
        }

        private void DoUserRefresh()
        {
            if (!Usbipd.Available)
            {
                if (Usbipd.ResolveExe() != null)
                {
                    UsbipdBanner.Visibility = Visibility.Collapsed;
                    SetActionsEnabled(true);
                }
                else
                {
                    Log("仍未找到 usbipd.exe。", LogWarn);
                    return;
                }
            }
            FireRefresh(true);
        }

        /// <summary>发起一次刷新（不等待完成，内部去重）</summary>
        private async void FireRefresh(bool userInitiated)
        {
            if (userInitiated)
            {
                if (!BeginBusy("正在刷新设备列表")) return;
                try { await RefreshSafeAsync(); }
                finally { EndBusy(); }
            }
            else
            {
                await RefreshSafeAsync();
            }
        }

        // ================= 刷新 =================

        private async Task RefreshSafeAsync()
        {
            if (_refreshing || !Usbipd.Available) return;
            _refreshing = true;
            try
            {
                List<UsbDevice> list = await Usbipd.ListAsync();
                PruneAutoAttach();
                foreach (UsbDevice d in list)
                {
                    d.IsAutoAttach = d.State == DeviceStateKind.AttachedKeep || IsAutoAttachActive(d.BusId);
                    Theme.ApplyDeviceVisuals(d);
                }

                // 保存选中项
                UsbDevice sel = DeviceList.SelectedItem as UsbDevice;
                string selId = sel != null ? sel.BusId : _selectedBusId;

                DeviceList.ItemsSource = list;

                // 恢复选中
                UsbDevice restore = null;
                if (selId != null)
                {
                    foreach (UsbDevice d in list)
                        if (d.BusId == selId) { restore = d; break; }
                }
                DeviceList.SelectedItem = restore;
                if (restore != null) DeviceList.ScrollIntoView(restore);

                EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateMeta();
                UpdateAutoAttachButtonText();
            }
            catch (Exception ex)
            {
                Log("刷新失败: " + ex.Message, LogErr);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void UpdateMeta()
        {
            string perm = IsElevated() ? "管理员" : "普通权限";
            string ver = Usbipd.VersionText.Length > 0 ? "usbipd " + Usbipd.VersionText : "usbipd";
            MetaText.Text = ver + " · " + perm;
        }

        // ================= 选择 / 快捷操作 =================

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UsbDevice d = DeviceList.SelectedItem as UsbDevice;
            _selectedBusId = d != null ? d.BusId : null;
            UpdateAutoAttachButtonText();
        }

        private void OnListQuickClick(object sender, RoutedEventArgs e)
        {
            Button b = e.OriginalSource as Button;
            if (b == null) return;
            if (!(b.Tag is string) || (string)b.Tag != "quick") return;
            UsbDevice d = b.DataContext as UsbDevice;
            if (d != null) RunQuickAction(d);
        }

        private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Button) return;
            UsbDevice d = DeviceList.SelectedItem as UsbDevice;
            if (d != null) RunQuickAction(d);
        }

        private void RunQuickAction(UsbDevice d)
        {
            if (d.IsAttached) RunDetachFlow(d);
            else RunAttachFlow(d);
        }

        private UsbDevice SelectedDevice(string opName)
        {
            UsbDevice d = DeviceList.SelectedItem as UsbDevice;
            if (d == null) Log("请先在列表中选择一台设备（" + opName + "）", LogDim);
            return d;
        }

        private void RunAttachFromButton()
        {
            UsbDevice d = SelectedDevice("连接");
            if (d != null) RunAttachFlow(d);
        }

        private void RunDetachFromButton()
        {
            UsbDevice d = SelectedDevice("断开");
            if (d != null) RunDetachFlow(d);
        }

        // ================= 核心流程 =================

        private bool ShouldAdbFix(UsbDevice d)
        {
            if (!Settings.GetBool(Settings.KAdbFixOnAttach, true)) return false;
            return d.IsAndroid || Settings.GetBool(Settings.KAdbFixAlways, false);
        }

        /// <summary>共享 → (ADB修复) → 连接 → (WSL adb 重启)</summary>
        private async void RunAttachFlow(UsbDevice d)
        {
            if (!BeginBusy("正在连接设备 " + d.BusId)) return;
            try
            {
                if (!d.IsShared)
                {
                    Log("正在共享 " + d.BusId + " …", LogCmd);
                    RunResult rb = await BindResult(d.BusId);
                    LogResult(rb, "共享");
                    if (!rb.Success) return;
                    d.State = DeviceStateKind.Shared;
                }

                if (ShouldAdbFix(d))
                    await DoAdbPreFix();
                if (Settings.GetBool(Settings.KWslLoadVhci, true))
                    await DoVhciLoad();

                Log("正在将 " + d.BusId + " 连接到 WSL" +
                    (d.IsAndroid && Settings.GetBool(Settings.KAdbFixOnAttach, true) ? "（Android 设备，已启用 ADB 模式）" : "") + " …", LogCmd);
                RunResult ra = await Usbipd.AttachAsync(d.BusId, CurrentDistro(), false);
                if (ra.Success)
                    Log("✓ 设备 " + d.BusId + " 已连接到 WSL", LogOk);
                else
                    LogResult(ra, "连接");

                if (ra.Success && ShouldAdbFix(d))
                    await DoWslAdbRestart();
            }
            catch (Exception ex)
            {
                Log("连接流程异常: " + ex.Message, LogErr);
            }
            finally
            {
                EndBusy();
            }
            await RefreshSafeAsync();
        }

        private async void RunDetachFlow(UsbDevice d)
        {
            if (!BeginBusy("正在断开设备 " + d.BusId)) return;
            try
            {
                if (IsAutoAttachActive(d.BusId))
                    StopAutoAttach(d.BusId, true, false);

                Log("正在断开 " + d.BusId + " 与 WSL 的连接 …", LogCmd);
                RunResult r = await Usbipd.DetachAsync(d.BusId);
                if (r.Success)
                {
                    Log("✓ 已断开连接，设备 " + d.BusId + " 已交还 Windows", LogOk);
                    if (ShouldAdbFix(d))
                        await DoAdbPostFix();
                }
                else
                    LogResult(r, "断开");
            }
            catch (Exception ex)
            {
                Log("断开流程异常: " + ex.Message, LogErr);
            }
            finally
            {
                EndBusy();
            }
            await RefreshSafeAsync();
        }

        private async void RunBindFlow()
        {
            UsbDevice d = SelectedDevice("共享");
            if (d == null) return;
            if (d.IsShared) { Log("设备 " + d.BusId + " 已处于共享状态，无需重复操作。", LogDim); return; }
            if (!BeginBusy("正在共享 " + d.BusId)) return;
            try
            {
                RunResult r = await BindResult(d.BusId);
                LogResult(r, "共享");
            }
            catch (Exception ex)
            {
                Log("共享异常: " + ex.Message, LogErr);
            }
            finally
            {
                EndBusy();
            }
            await RefreshSafeAsync();
        }

        private async void RunUnbindFlow()
        {
            UsbDevice d = SelectedDevice("取消共享");
            if (d == null) return;
            if (!BeginBusy("正在取消共享 " + d.BusId)) return;
            try
            {
                if (d.IsAttached)
                {
                    Log("设备当前已连接，先断开…", LogCmd);
                    RunResult rd = await Usbipd.DetachAsync(d.BusId);
                    if (!rd.Success) LogResult(rd, "断开");
                }
                Log("正在取消共享 " + d.BusId + " …", LogCmd);
                RunResult r = await UnbindResult(d.BusId);
                LogResult(r, "取消共享");
            }
            catch (Exception ex)
            {
                Log("取消共享异常: " + ex.Message, LogErr);
            }
            finally
            {
                EndBusy();
            }
            await RefreshSafeAsync();
        }

        private async void RunFixAdbFlow()
        {
            if (!BeginBusy("正在修复 ADB 环境")) return;
            try
            {
                bool didSomething = false;
                string adbPath = Adb.Resolve();
                if (adbPath != null)
                {
                    Log("正在结束 Windows 端 adb 进程（adb kill-server）…", LogCmd);
                    RunResult r = await Adb.KillServerAsync(adbPath);
                    if (r.Success) Log("✓ Windows 端 adb 服务已停止", LogOk);
                    else Log("Windows 端 adb 执行异常：" + Truncate(r.Combined, 300), LogWarn);
                    didSomething = true;
                }
                else
                {
                    Log("未找到 adb.exe，跳过 Windows 端步骤（可在设置中指定路径）", LogWarn);
                }

                if (CurrentDistro().Length > 0 || await HasAnyDistro())
                {
                    await DoWslAdbRestart();
                    didSomething = true;
                }
                if (!didSomething) Log("未执行任何操作。", LogDim);
            }
            catch (Exception ex)
            {
                Log("修复 ADB 异常: " + ex.Message, LogErr);
            }
            finally
            {
                EndBusy();
            }
            await RefreshSafeAsync();
        }

        // ---- 提权共享/取消共享封装 ----

        private Task<RunResult> BindResult(string busId)
        {
            return Usbipd.BindAsync(busId, !IsElevated());
        }

        private Task<RunResult> UnbindResult(string busId)
        {
            return Usbipd.UnbindAsync(busId, !IsElevated());
        }

        // ---- ADB 修复步骤 ----

        private async Task DoAdbPreFix()
        {
            string adbPath = Adb.Resolve();
            if (adbPath == null)
            {
                Log("未找到 adb.exe，跳过「结束 Windows adb」步骤（可到设置中指定路径）", LogWarn);
                return;
            }
            Log("ADB 模式：正在结束 Windows 端 adb 进程，释放设备占用…", LogCmd);
            RunResult r = await Adb.KillServerAsync(adbPath);
            if (r.Success) Log("✓ Windows 端 adb 服务已停止", LogOk);
            else Log("Windows 端 adb kill-server 返回异常（一般不影响继续）", LogWarn);
        }

        /// <summary>断开连接后启动 Windows 端 adb 服务，让 Windows 立即恢复 adb 设备可见性</summary>
        private async Task DoAdbPostFix()
        {
            string adbPath = Adb.Resolve();
            if (adbPath == null) return;
            Log("正在启动 Windows 端 adb 服务（adb start-server）…", LogCmd);
            RunResult r = await Adb.StartServerAsync(adbPath);
            if (r.Success) Log("✓ Windows 端 adb 服务已就绪，设备可立即使用", LogOk);
            else Log("Windows 端 adb start-server 异常：" + Truncate(r.Combined, 300), LogWarn);
        }

        private async Task DoVhciLoad()
        {
            string distro = CurrentDistro();
            try
            {
                RunResult r = await Wsl.LoadVhciAsync(distro);
                if (!r.Success) Log("提示：在 WSL 内加载 vhci_hcd 模块失败（若已内置内核可忽略）", LogDim);
            }
            catch { }
        }

        private async Task DoWslAdbRestart()
        {
            string distro = CurrentDistro();
            if (!Settings.GetBool(Settings.KWslRestartAdb, true)) return;
            Log("正在重启 WSL 内 adb 服务（root）…", LogCmd);
            try
            {
                RunResult r = await Wsl.RestartAdbAsync(distro);
                string outp = r.Combined;
                if (outp.Contains("__WUSBM_NOADB__"))
                {
                    Log("WSL 中未安装 adb。可在 WSL 内执行：sudo apt install adb", LogWarn);
                    return;
                }
                if (r.Success)
                {
                    Log("✓ WSL 内 adb 服务已重启，当前设备列表：", LogOk);
                    string[] lines = outp.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    int shown = 0;
                    foreach (string line in lines)
                    {
                        string t = line.Trim();
                        if (t.Length == 0 || shown >= 8) continue;
                        Log("  " + t, LogDim);
                        shown++;
                    }
                    if (shown == 0) Log("  （adb devices 无输出）", LogDim);
                }
                else
                {
                    Log("WSL 内 adb 重启失败：" + Truncate(outp, 300), LogWarn);
                }
            }
            catch (Exception ex)
            {
                Log("WSL 内 adb 重启异常: " + ex.Message, LogWarn);
            }
        }

        private async Task<bool> HasAnyDistro()
        {
            return await Task.Run<bool>(delegate { return Wsl.ListDistros().Count > 0; });
        }

        // ================= 自动附加 =================

        private bool IsAutoAttachActive(string busId)
        {
            int pid;
            if (!_autoAttach.TryGetValue(busId, out pid)) return false;
            return IsUsbipdAlive(pid);
        }

        private static bool IsUsbipdAlive(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    if (p == null) return false;
                    if (p.HasExited) return false;
                    return p.ProcessName.Equals("usbipd", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        private void PruneAutoAttach()
        {
            List<string> dead = new List<string>();
            foreach (KeyValuePair<string, int> kv in _autoAttach)
                if (!IsUsbipdAlive(kv.Value)) dead.Add(kv.Key);
            if (dead.Count == 0) return;
            foreach (string k in dead) _autoAttach.Remove(k);
            Settings.SetAutoAttach(_autoAttach);
            Settings.Save();
        }

        private void SaveAutoAttach()
        {
            Settings.SetAutoAttach(_autoAttach);
            Settings.Save();
        }

        private void UpdateAutoAttachButtonText()
        {
            UsbDevice d = DeviceList.SelectedItem as UsbDevice;
            bool active = d != null && (IsAutoAttachActive(d.BusId) || d.State == DeviceStateKind.AttachedKeep);
            AutoAttachBtn.Content = active ? "停止自动连接" : "自动连接";
        }

        private async void RunToggleAutoAttach()
        {
            UsbDevice d = SelectedDevice("自动连接");
            if (d == null) return;
            bool active = IsAutoAttachActive(d.BusId) || d.State == DeviceStateKind.AttachedKeep;
            if (active)
            {
                if (!BeginBusy("正在停止自动连接")) return;
                try
                {
                    StopAutoAttach(d.BusId, true, true);
                }
                finally { EndBusy(); }
                await RefreshSafeAsync();
                return;
            }

            if (!BeginBusy("正在启动自动连接")) return;
            try
            {
                if (!d.IsShared)
                {
                    Log("正在共享 " + d.BusId + " …", LogCmd);
                    RunResult rb = await BindResult(d.BusId);
                    LogResult(rb, "共享");
                    if (!rb.Success) return;
                }
                Log("正在启动自动连接守护（usbipd --auto-attach）…", LogCmd);
                Process p = null;
                try
                {
                    p = Usbipd.SpawnAutoAttach(d.BusId, CurrentDistro(),
                        delegate(string line) { SafeLogThread("  [auto] " + line, LogDim); },
                        delegate(string line) { SafeLogThread("  [auto] " + line, LogWarn); });
                }
                catch (Exception ex)
                {
                    Log("启动自动连接失败: " + ex.Message, LogErr);
                    return;
                }
                await Task.Delay(1500);
                if (p.HasExited)
                {
                    Log("自动连接进程已退出（退出码 " + p.ExitCode + "），请检查 usbipd 版本是否支持 --auto-attach。", LogErr);
                    return;
                }
                _autoAttach[d.BusId] = p.Id;
                SaveAutoAttach();
                Log("✓ 自动连接已启动（后台 usbipd 进程 PID " + p.Id + "）。该设备每次插入都会自动连接到 WSL。", LogOk);
            }
            catch (Exception ex)
            {
                Log("自动连接异常: " + ex.Message, LogErr);
            }
            finally
            {
                EndBusy();
            }
            await RefreshSafeAsync();
        }

        /// <summary>
        /// 停止某设备的自动连接守护。
        /// alsoDetach=true 时同时发起分离（供「停止自动连接」入口使用）；
        /// 断开连接流程内调用时传 false，由流程自身执行唯一的 detach，避免双重分离。
        /// </summary>
        private void StopAutoAttach(string busId, bool withLog, bool alsoDetach)
        {
            int pid;
            if (_autoAttach.TryGetValue(busId, out pid))
            {
                try
                {
                    using (Process p = Process.GetProcessById(pid))
                    {
                        if (p != null && !p.HasExited &&
                            p.ProcessName.Equals("usbipd", StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            if (withLog) Log("✓ 已停止自动连接（结束 usbipd 进程 PID " + pid + "）", LogOk);
                        }
                    }
                }
                catch { }
                _autoAttach.Remove(busId);
                SaveAutoAttach();
            }
            if (alsoDetach)
                DetachQuietly(busId);
        }

        /// <summary>后台分离设备（异常就地吞掉，不产生未观察任务）</summary>
        private static async void DetachQuietly(string busId)
        {
            try { await Usbipd.DetachAsync(busId); } catch { }
        }

        private void SafeLogThread(string msg, Brush kind)
        {
            try
            {
                _win.Dispatcher.BeginInvoke(new Action(delegate { Log(msg, kind); }));
            }
            catch { }
        }

        // ================= 设置 =================

        private void OnDistroChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_populatingDistro) return;
            ComboBoxItem item = DistroCombo.SelectedItem as ComboBoxItem;
            if (item == null) return;
            string tag = item.Tag as string;
            if (tag == null) tag = "";
            if (tag != _lastDistroTag)
            {
                _lastDistroTag = tag;
                Settings.Set(Settings.KDistro, tag);
                Settings.Save();
            }
        }

        private async Task PopulateDistroComboAsync()
        {
            List<string> distros = await Task.Run<List<string>>(delegate { return Wsl.ListDistros(); });
            _populatingDistro = true;
            try
            {
                DistroCombo.Items.Clear();
                ComboBoxItem auto = new ComboBoxItem();
                auto.Content = "自动检测（默认发行版）";
                auto.Tag = "";
                DistroCombo.Items.Add(auto);

                foreach (string d in distros)
                {
                    ComboBoxItem it = new ComboBoxItem();
                    it.Content = d;
                    it.Tag = d;
                    DistroCombo.Items.Add(it);
                }

                string saved = Settings.Get(Settings.KDistro, "");
                _lastDistroTag = saved;
                int idx = 0;
                for (int i = 0; i < DistroCombo.Items.Count; i++)
                {
                    ComboBoxItem it = DistroCombo.Items[i] as ComboBoxItem;
                    if (it != null && (it.Tag as string) == saved) { idx = i; break; }
                }
                DistroCombo.SelectedIndex = idx;

                if (distros.Count == 0)
                    Log("未检测到 WSL 发行版，请确认 WSL 已安装并至少有一个发行版。", LogWarn);
                else
                    Log("WSL 发行版：" + string.Join(", ", distros.ToArray()), LogDim);
            }
            finally
            {
                _populatingDistro = false;
            }
        }

        private string CurrentDistro()
        {
            ComboBoxItem item = DistroCombo.SelectedItem as ComboBoxItem;
            if (item == null) return "";
            string tag = item.Tag as string;
            return tag == null ? "" : tag;
        }

        private void ShowSettings()
        {
            Window dlg = LoadDialog("SettingsDialog.xaml");
            dlg.Icon = _win.Icon;
            TextBox adbBox = (TextBox)dlg.FindName("AdbPathBox");
            Button browseBtn = (Button)dlg.FindName("BrowseBtn");
            Button detectBtn = (Button)dlg.FindName("DetectBtn");
            CheckBox fixAlways = (CheckBox)dlg.FindName("ChkFixAlways");
            CheckBox wslAdb = (CheckBox)dlg.FindName("ChkWslAdb");
            CheckBox vhci = (CheckBox)dlg.FindName("ChkVhci");
            CheckBox trayIcon = (CheckBox)dlg.FindName("ChkTrayIcon");
            CheckBox trayMinimize = (CheckBox)dlg.FindName("ChkTrayMinimize");
            CheckBox trayClose = (CheckBox)dlg.FindName("ChkTrayClose");
            Button okBtn = (Button)dlg.FindName("OkBtn");
            Button cancelBtn = (Button)dlg.FindName("CancelBtn");
            TextBlock linkText = (TextBlock)dlg.FindName("LinkText");

            adbBox.Text = Settings.Get(Settings.KAdbPath, "");
            fixAlways.IsChecked = Settings.GetBool(Settings.KAdbFixAlways, false);
            wslAdb.IsChecked = Settings.GetBool(Settings.KWslRestartAdb, true);
            vhci.IsChecked = Settings.GetBool(Settings.KWslLoadVhci, true);
            trayIcon.IsChecked = Settings.GetBool(Settings.KTrayIcon, true);
            trayMinimize.IsChecked = Settings.GetBool(Settings.KTrayMinimize, true);
            trayClose.IsChecked = Settings.GetBool(Settings.KTrayClose, false);

            foreach (Inline inc in linkText.Inlines)
            {
                Hyperlink h = inc as Hyperlink;
                if (h == null) continue;
                h.RequestNavigate += delegate(object s, System.Windows.Navigation.RequestNavigateEventArgs e2)
                {
                    App.OpenUrl(e2.Uri.AbsoluteUri);
                    e2.Handled = true;
                };
            }

            browseBtn.Click += delegate
            {
                Microsoft.Win32.OpenFileDialog fd = new Microsoft.Win32.OpenFileDialog();
                fd.Title = "选择 adb.exe";
                fd.Filter = "adb.exe|adb.exe|所有文件|*.*";
                fd.CheckFileExists = true;
                if (fd.ShowDialog() == true) adbBox.Text = fd.FileName;
            };

            detectBtn.Click += delegate
            {
                Adb.Invalidate();
                string p = Adb.Resolve();
                if (p != null) adbBox.Text = p;
                else MessageBox.Show(dlg, "未能在 PATH 与常见位置找到 adb.exe。", App.AppName);
            };

            okBtn.Click += delegate
            {
                Settings.Set(Settings.KAdbPath, adbBox.Text.Trim());
                Settings.SetBool(Settings.KAdbFixAlways, fixAlways.IsChecked == true);
                Settings.SetBool(Settings.KWslRestartAdb, wslAdb.IsChecked == true);
                Settings.SetBool(Settings.KWslLoadVhci, vhci.IsChecked == true);
                Settings.SetBool(Settings.KTrayIcon, trayIcon.IsChecked == true);
                Settings.SetBool(Settings.KTrayMinimize, trayMinimize.IsChecked == true);
                Settings.SetBool(Settings.KTrayClose, trayClose.IsChecked == true);
                Settings.Save();
                Adb.Invalidate();
                ApplyTrayVisibility();
                dlg.DialogResult = true;
            };
            cancelBtn.Click += delegate { dlg.DialogResult = false; };

            dlg.Owner = _win;
            dlg.SourceInitialized += delegate { Theme.ApplyWindowChrome(dlg); };
            dlg.ShowDialog();
            if (dlg.DialogResult == true)
            {
                string p2 = Adb.Resolve();
                Log("设置已保存。" + (p2 != null ? "adb: " + p2 : "adb: 未找到"), LogDim);
            }
        }

        /// <summary>使用便携包内置的 MSI 静默安装 usbipd-win（UAC 提权），完成后自动就绪</summary>
        private async void InstallUsbipdFromBundle()
        {
            string msi = Usbipd.FindBundledMsi();
            if (msi == null) return;
            if (!BeginBusy("正在安装 usbipd-win")) return;
            try
            {
                Log("正在静默安装 usbipd-win（" + System.IO.Path.GetFileName(msi) + "）…", LogCmd);
                RunResult r = await Task.Run<RunResult>(delegate
                {
                    return ProcessRunner.RunElevated("msiexec.exe",
                        "/i \"" + msi + "\" /qn /norestart", 180000);
                });

                if (r.Cancelled)
                {
                    Log("已取消 UAC 授权，安装中止。", LogWarn);
                    return;
                }
                if (!r.Success && r.ExitCode != 3010)
                {
                    Log("✗ usbipd-win 安装失败（退出码 " + r.ExitCode + "）。", LogErr);
                    Log("可改为双击安装包手动安装：" + msi, LogDim);
                    return;
                }
                Log("✓ usbipd-win 安装完成" + (r.ExitCode == 3010 ? "（建议重启系统后使用）" : ""), LogOk);

                Usbipd.ResetResolve();
                if (Usbipd.ResolveExe() != null)
                {
                    UsbipdBanner.Visibility = Visibility.Collapsed;
                    SetActionsEnabled(true);
                    await Usbipd.DetectVersionAsync();
                    UpdateMeta();
                    await RefreshSafeAsync();
                    Log("usbipd 已就绪: " + Usbipd.ExePath, LogDim);
                    Log("就绪。", LogDim);
                }
                else
                {
                    Log("安装后仍未找到 usbipd.exe，请重启本程序或重启系统。", LogErr);
                }
            }
            catch (Exception ex)
            {
                Log("安装 usbipd-win 异常: " + ex.Message, LogErr);
            }
            finally
            {
                EndBusy();
            }
        }

        private void RestartAsAdmin()
        {
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                _realExit = true; // 绕过「关闭到托盘」拦截，让旧实例真正退出以释放单实例锁
                _win.Close();
            }
            catch (Win32Exception w)
            {
                if (w.NativeErrorCode != 1223)
                    Log("以管理员身份重启失败: " + w.Message, LogErr);
            }
            catch (Exception ex)
            {
                Log("以管理员身份重启失败: " + ex.Message, LogErr);
            }
        }

        private static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal p = new WindowsPrincipal(id);
                    return p.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        // ================= 忙碌状态 =================

        private void SetActionsEnabled(bool on)
        {
            RefreshBtn.IsEnabled = on;
            DistroCombo.IsEnabled = on;
            AdbCheck.IsEnabled = on;
            FixAdbBtn.IsEnabled = on;
            SettingsBtn.IsEnabled = on;
            ThemeToggleBtn.IsEnabled = on;
            BindBtn.IsEnabled = on;
            UnbindBtn.IsEnabled = on;
            AttachBtn.IsEnabled = on;
            DetachBtn.IsEnabled = on;
            AutoAttachBtn.IsEnabled = on;
            DeviceList.IsEnabled = on;
        }

        private bool BeginBusy(string label)
        {
            if (_busy) return false;
            _busy = true;
            SetActionsEnabled(false);
            BusyBar.Visibility = Visibility.Visible;
            StatusText.Text = label + "…";
            return true;
        }

        private void EndBusy()
        {
            _busy = false;
            SetActionsEnabled(Usbipd.Available);
            BusyBar.Visibility = Visibility.Collapsed;
            StatusText.Text = "就绪";
        }

        // ================= 日志 =================

        private void Log(string msg, Brush kind)
        {
            Paragraph p = new Paragraph();
            p.Margin = new Thickness(0, 1, 0, 1);

            Run ts = new Run(DateTime.Now.ToString("HH:mm:ss") + "  ");
            ts.Foreground = LogTime;
            ts.FontFamily = MonoFont;
            ts.FontSize = 10.5;
            p.Inlines.Add(ts);

            Run r = new Run(msg);
            r.Foreground = kind;
            p.Inlines.Add(r);

            LogBox.Document.Blocks.Add(p);
            while (LogBox.Document.Blocks.Count > 400)
                LogBox.Document.Blocks.Remove(LogBox.Document.Blocks.FirstBlock);
            LogBox.ScrollToEnd();
        }

        private void LogResult(RunResult r, string what)
        {
            if (r == null) return;
            if (r.Success)
            {
                Log("✓ " + what + "成功", LogOk);
                return;
            }
            if (r.Cancelled)
            {
                Log("✗ " + what + "中止（UAC 授权被取消）", LogWarn);
                return;
            }
            string detail = r.Combined;
            if (detail.Length == 0)
            {
                if (r.TimedOut) detail = "执行超时";
                else if (r.StartFailed) detail = "进程启动失败";
                else detail = "退出码 " + r.ExitCode;
            }
            Log("✗ " + what + "失败: " + Truncate(detail, 600), LogErr);
            if (detail.ToLowerInvariant().Contains("admin"))
                Log("提示：该操作需要管理员权限，可点击顶栏「以管理员身份重启」。", LogWarn);
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length <= max) return s;
            return s.Substring(0, max) + " …";
        }
    }
}
