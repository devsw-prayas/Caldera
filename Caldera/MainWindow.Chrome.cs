using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── DWM P/Invokes ─────────────────────────────────────────────────────
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        // ── Window state ──────────────────────────────────────────────────────

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    () => RootBorder.Margin = GetMaximizedOverhang());
            else
            {
                RootBorder.Margin = new Thickness(0);
                MaxWidth = double.PositiveInfinity;
                MaxHeight = double.PositiveInfinity;
            }
        }

        private Thickness GetMaximizedOverhang()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            GetWindowRect(hwnd, out RECT winRect);
            var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(hMon, ref mi);
            var work = mi.rcWork;
            int left = work.Left - winRect.Left, top = work.Top - winRect.Top;
            int right = winRect.Right - work.Right, bottom = winRect.Bottom - work.Bottom;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return new Thickness(left, top, right, bottom);
            var m = source.CompositionTarget.TransformFromDevice;
            return new Thickness(left * m.M11, top * m.M22, right * m.M11, bottom * m.M22);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { MaximizeButton_Click(sender, e); return; }
            DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var dirty = _tabs.Where(t => t.IsDirty).ToList();
            if (dirty.Any())
            {
                var dlg = new UnsavedChangesDialog($"{dirty.Count} unsaved tab(s). Save before closing?", this);
                dlg.ShowDialog();
                var result = dlg.Result;
                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes)
                    foreach (var t in dirty) SaveSession(t);
            }
            Close();
        }

        // ── Output panel animation ─────────────────────────────────────────────

        private bool _outputVisible = false;

        public void ShowOutputPanel()
        {
            if (_outputVisible) return;
            _outputVisible = true;
            OutputPanel.Visibility = Visibility.Visible;
            OutputSplitter.Visibility = Visibility.Visible;
            var anim = new GridLengthAnimation
            {
                From = new GridLength(0),
                To = new GridLength(220),
                Duration = new Duration(TimeSpan.FromMilliseconds(280)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (s, ev) =>
            {
                OutputRow.BeginAnimation(System.Windows.Controls.RowDefinition.HeightProperty, null);
                OutputRow.Height = new GridLength(220);
            };
            OutputRow.BeginAnimation(System.Windows.Controls.RowDefinition.HeightProperty, anim);
        }

        public void HideOutputPanel()
        {
            if (!_outputVisible) return;
            double h = OutputRow.ActualHeight;
            var anim = new GridLengthAnimation
            {
                From = new GridLength(h),
                To = new GridLength(0),
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (s, ev) =>
            {
                OutputRow.BeginAnimation(System.Windows.Controls.RowDefinition.HeightProperty, null);
                OutputRow.Height = new GridLength(0);
                OutputPanel.Visibility = Visibility.Collapsed;
                OutputSplitter.Visibility = Visibility.Collapsed;
                _outputVisible = false;
            };
            OutputRow.BeginAnimation(System.Windows.Controls.RowDefinition.HeightProperty, anim);
        }
    }
}
