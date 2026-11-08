using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WslUsbManager
{
    /// <summary>
    /// 主题系统：深色 / 浅色两套画刷，通过 Application 级 MergedDictionary + DynamicResource 实现实时切换
    /// </summary>
    public static class Theme
    {
        public static bool IsDark { get; private set; }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>应用主题（替换 Application 级资源字典，所有 DynamicResource 实时刷新）</summary>
        public static void Apply(bool dark)
        {
            IsDark = dark;
            ResourceDictionary rd = new ResourceDictionary();
            if (dark) FillDark(rd); else FillLight(rd);
            rd["ThemeIsDark"] = dark ? "1" : "0";
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(rd);
        }

        /// <summary>按 key 取主题画刷，找不到时用 fallbackHex 兜底</summary>
        public static Brush Brush(string key, string fallbackHex)
        {
            try
            {
                object v = Application.Current.Resources[key];
                Brush b = v as Brush;
                if (b != null) return b;
            }
            catch { }
            return Hex(fallbackHex);
        }

        /// <summary>给设备行设置状态文案与配色（须在 UI 线程调用）</summary>
        public static void ApplyDeviceVisuals(UsbDevice d)
        {
            switch (d.State)
            {
                case DeviceStateKind.Shared:
                    d.StateText = "已共享";
                    d.StateFg = Brush("StateSharedFg", "#79B8FF");
                    d.StateBg = Brush("StateSharedBg", "#2E4493F8");
                    break;
                case DeviceStateKind.Attached:
                    d.StateText = "已连接到 WSL";
                    d.StateFg = Brush("StateOkFg", "#56D364");
                    d.StateBg = Brush("StateOkBg", "#2E3FB950");
                    break;
                case DeviceStateKind.AttachedKeep:
                    d.StateText = "自动连接中";
                    d.StateFg = Brush("StateOkFg", "#56D364");
                    d.StateBg = Brush("StateOkBg", "#2E3FB950");
                    break;
                case DeviceStateKind.NotShared:
                    d.StateText = "未共享";
                    d.StateFg = Brush("StateNeutralFg", "#B0BAC6");
                    d.StateBg = Brush("StateNeutralBg", "#269EA7B2");
                    break;
                default:
                    d.StateText = "未知";
                    d.StateFg = Brush("StateNeutralFg", "#B0BAC6");
                    d.StateBg = Brush("StateNeutralBg", "#269EA7B2");
                    break;
            }
        }

        /// <summary>窗口标题栏明暗与圆角（Win10 1903+ / Win11），须在窗口句柄创建后调用</summary>
        public static void ApplyWindowChrome(Window w)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                int on = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, 4);
                int round = 2;
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);
            }
            catch { }
        }

        private static SolidColorBrush Hex(string hex)
        {
            SolidColorBrush s = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            s.Freeze();
            return s;
        }

        private static void FillDark(ResourceDictionary r)
        {
            // 基础
            r["BgWindow"] = Hex("#0D1117");
            r["BarBg"] = Hex("#11161D");
            r["BarBorder"] = Hex("#21262D");
            r["TextPrimary"] = Hex("#E6EDF3");
            r["TextDim"] = Hex("#9198A1");
            r["TextFaint"] = Hex("#6E7681");
            r["TextBody"] = Hex("#C9D1D9");
            r["Accent"] = Hex("#4493F8");
            r["AccentStrong"] = Hex("#1F6FEB");
            // 按钮
            r["BtnBg"] = Hex("#1C222B");
            r["BtnFg"] = Hex("#DCE3EB");
            r["BtnBorder"] = Hex("#2D333B");
            r["PrimaryBg"] = Hex("#1F6FEB");
            r["PrimaryFg"] = Hex("#FFFFFF");
            r["PrimaryBorder"] = Hex("#3B82F6");
            // 控件
            r["ControlBg"] = Hex("#161B22");
            r["ControlBorder"] = Hex("#2D333B");
            r["ControlBorderHover"] = Hex("#414B59");
            r["ControlTrack"] = Hex("#30363D");
            r["ThumbIdle"] = Hex("#8B949E");
            r["CheckBoxBorder"] = Hex("#444C56");
            r["InputBg"] = Hex("#0B0F14");
            r["ProgressTrack"] = Hex("#1B2129");
            // 列表 / 卡片
            r["ChipBg"] = Hex("#10151C");
            r["ChipBorder"] = Hex("#262D36");
            r["LinkBlue"] = Hex("#58A6FF");
            r["IconTileBg"] = Hex("#141B24");
            r["IconTileBorder"] = Hex("#232B36");
            r["RowSeparator"] = Hex("#161C24");
            r["RowHover"] = Hex("#121924");
            r["RowSelected"] = Hex("#12233A");
            r["MenuItemHover"] = Hex("#1F2630");
            r["MenuItemSelected"] = Hex("#14314F");
            r["BadgeOkBg"] = Hex("#12291D");
            r["BadgeOkBorder"] = Hex("#1E4629");
            r["OkGreen"] = Hex("#3FB950");
            // 警示条
            r["BannerWarnBg"] = Hex("#2B2313");
            r["BannerWarnBorder"] = Hex("#4A3B16");
            r["BannerWarnText"] = Hex("#E3B341");
            r["BannerErrBg"] = Hex("#2D1517");
            r["BannerErrBorder"] = Hex("#57282C");
            r["BannerErrText"] = Hex("#F85149");
            // 日志
            r["LogBg"] = Hex("#0B0F14");
            r["LogTime"] = Hex("#555D66");
            r["LogInfo"] = Hex("#C9D1D9");
            r["LogDim"] = Hex("#6E7681");
            r["LogOk"] = Hex("#3FB950");
            r["LogWarn"] = Hex("#D29922");
            r["LogErr"] = Hex("#F85149");
            r["LogCmd"] = Hex("#79C0FF");
            // 状态徽章（8 位 HEX = AARRGGBB：前两位是透明度）
            r["StateNeutralFg"] = Hex("#B0BAC6");
            r["StateNeutralBg"] = Hex("#269EA7B2");
            r["StateSharedFg"] = Hex("#79B8FF");
            r["StateSharedBg"] = Hex("#2E4493F8");
            r["StateOkFg"] = Hex("#56D364");
            r["StateOkBg"] = Hex("#2E3FB950");
        }

        private static void FillLight(ResourceDictionary r)
        {
            // 基础
            r["BgWindow"] = Hex("#F6F8FA");
            r["BarBg"] = Hex("#EEF1F4");
            r["BarBorder"] = Hex("#D8DEE4");
            r["TextPrimary"] = Hex("#1F2328");
            r["TextDim"] = Hex("#59636E");
            r["TextFaint"] = Hex("#8C959F");
            r["TextBody"] = Hex("#3D444D");
            r["Accent"] = Hex("#0969DA");
            r["AccentStrong"] = Hex("#1F6FEB");
            // 按钮
            r["BtnBg"] = Hex("#FFFFFF");
            r["BtnFg"] = Hex("#24292F");
            r["BtnBorder"] = Hex("#D0D7DE");
            r["PrimaryBg"] = Hex("#1F6FEB");
            r["PrimaryFg"] = Hex("#FFFFFF");
            r["PrimaryBorder"] = Hex("#1F6FEB");
            // 控件
            r["ControlBg"] = Hex("#FFFFFF");
            r["ControlBorder"] = Hex("#D0D7DE");
            r["ControlBorderHover"] = Hex("#8C959F");
            r["ControlTrack"] = Hex("#D5DCE2");
            r["ThumbIdle"] = Hex("#FFFFFF");
            r["CheckBoxBorder"] = Hex("#B3BCC5");
            r["InputBg"] = Hex("#FFFFFF");
            r["ProgressTrack"] = Hex("#D5DCE2");
            // 列表 / 卡片
            r["ChipBg"] = Hex("#EDF1F5");
            r["ChipBorder"] = Hex("#D5DCE2");
            r["LinkBlue"] = Hex("#0550AE");
            r["IconTileBg"] = Hex("#E7ECF2");
            r["IconTileBorder"] = Hex("#D5DCE2");
            r["RowSeparator"] = Hex("#E3E8EE");
            r["RowHover"] = Hex("#E9EEF4");
            r["RowSelected"] = Hex("#D9E6F8");
            r["MenuItemHover"] = Hex("#E9EEF4");
            r["MenuItemSelected"] = Hex("#CFE1F8");
            r["BadgeOkBg"] = Hex("#DDF3E5");
            r["BadgeOkBorder"] = Hex("#AADCC0");
            r["OkGreen"] = Hex("#1A7F37");
            // 警示条
            r["BannerWarnBg"] = Hex("#FFF8C5");
            r["BannerWarnBorder"] = Hex("#E2C25C");
            r["BannerWarnText"] = Hex("#7A5300");
            r["BannerErrBg"] = Hex("#FFEBE9");
            r["BannerErrBorder"] = Hex("#F1B8B9");
            r["BannerErrText"] = Hex("#C2292F");
            // 日志
            r["LogBg"] = Hex("#FFFFFF");
            r["LogTime"] = Hex("#8C959F");
            r["LogInfo"] = Hex("#24292F");
            r["LogDim"] = Hex("#8C959F");
            r["LogOk"] = Hex("#1A7F37");
            r["LogWarn"] = Hex("#9A6700");
            r["LogErr"] = Hex("#CF222E");
            r["LogCmd"] = Hex("#0550AE");
            // 状态徽章（8 位 HEX = AARRGGBB：前两位是透明度）
            r["StateNeutralFg"] = Hex("#59636E");
            r["StateNeutralBg"] = Hex("#1F6E7781");
            r["StateSharedFg"] = Hex("#0550AE");
            r["StateSharedBg"] = Hex("#1F0969DA");
            r["StateOkFg"] = Hex("#1A7F37");
            r["StateOkBg"] = Hex("#241A7F37");
        }
    }
}
