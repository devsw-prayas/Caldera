using System;
using System.Windows;
using System.Windows.Threading;

namespace Caldera
{
    public partial class MainWindow
    {
        private DispatcherTimer? _autoCompileTimer;
        private bool _autoCompileEnabled = false;

        private void InitAutoCompile()
        {
            _autoCompileTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _autoCompileTimer.Tick += AutoCompileTimer_Tick;
        }

        private void AutoCompileToggle_Click(object sender, RoutedEventArgs e)
        {
            _autoCompileEnabled = !_autoCompileEnabled;
            AutoCompileToggle.Content = _autoCompileEnabled ? "⏲ Auto: ON" : "⏲ Auto: OFF";
            
            if (_autoCompileEnabled && _activeSession != null && _activeSession.IsDirty)
            {
                RunButton_Click(this, new RoutedEventArgs());
            }
        }

        private void TriggerDebouncedCompile()
        {
            if (!_autoCompileEnabled) return;
            _autoCompileTimer?.Stop();
            _autoCompileTimer?.Start();
        }

        private void AutoCompileTimer_Tick(object? sender, EventArgs e)
        {
            _autoCompileTimer?.Stop();
            if (_autoCompileEnabled && _activeSession != null)
            {
                RunButton_Click(this, new RoutedEventArgs());
            }
        }
    }
}
