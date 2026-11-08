using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WslUsbManager
{
    /// <summary>
    /// 系统托盘图标：左键唤回主窗口，右键快捷菜单
    /// </summary>
    public class TrayController : IDisposable
    {
        private NotifyIcon _icon;
        private ContextMenuStrip _menu;

        public bool Initialized { get { return _icon != null; } }

        public bool Visible
        {
            get { return _icon != null && _icon.Visible; }
            set { if (_icon != null) _icon.Visible = value; }
        }

        public void Init(ImageSource wpfIcon, string tooltip,
            Action onOpen, Action onRefresh, Action onFixAdb, Action onToggleTheme, Action onExit)
        {
            if (_icon != null) return;

            _menu = new ContextMenuStrip();
            _menu.Items.Add("打开主窗口", null, delegate { Fire(onOpen); });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("刷新设备", null, delegate { Fire(onRefresh); });
            _menu.Items.Add("修复 ADB", null, delegate { Fire(onFixAdb); });
            _menu.Items.Add("切换深 / 浅色主题", null, delegate { Fire(onToggleTheme); });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("退出", null, delegate { Fire(onExit); });

            _icon = new NotifyIcon();
            _icon.Text = tooltip == null ? "WSL USB Manager" : tooltip;
            _icon.Icon = ToGdiIcon(wpfIcon);
            _icon.ContextMenuStrip = _menu;
            _icon.Visible = true;
            _icon.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) Fire(onOpen);
            };
            _icon.DoubleClick += delegate { Fire(onOpen); };
        }

        public void ShowBalloon(string title, string text)
        {
            if (_icon != null)
                _icon.ShowBalloonTip(2500, title, text, ToolTipIcon.Info);
        }

        public void Dispose()
        {
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
            if (_menu != null)
            {
                _menu.Dispose();
                _menu = null;
            }
        }

        private static void Fire(Action a)
        {
            if (a != null) a();
        }

        /// <summary>把 WPF 渲染的图标转成 GDI Icon（托盘用）</summary>
        private static Icon ToGdiIcon(ImageSource src)
        {
            try
            {
                RenderTargetBitmap rtb = src as RenderTargetBitmap;
                if (rtb == null) return SystemIcons.Application;
                int w = rtb.PixelWidth;
                int h = rtb.PixelHeight;
                byte[] pixels = new byte[w * h * 4];
                rtb.CopyPixels(pixels, w * 4, 0);
                Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h),
                    ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                Marshal.Copy(pixels, 0, bd.Scan0, pixels.Length);
                bmp.UnlockBits(bd);
                IntPtr hIcon = bmp.GetHicon();
                Icon icon = Icon.FromHandle(hIcon);
                return icon;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }
    }
}
