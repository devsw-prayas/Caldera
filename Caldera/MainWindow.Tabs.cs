using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── Tab management ────────────────────────────────────────────────────

        private void NewTab(string? filePath = null, string? content = null)
        {
            var session = new TabSession { FilePath = filePath };
            if (content != null)
                session.Document.Text = content;

            _tabs.Add(session);
            SourceTabControl.ItemsSource = _tabs;
            SourceTabControl.SelectedItem = session;
            ActivateTab(session);
        }

        private void ActivateTab(TabSession session)
        {
            if (_activeSession == session) return;
            _activeSession = session;

            SourceEditor.Document = session.Document;

            AsmOutput.Text = session.AsmText;
            CompilerOutput.Text = session.CompilerText;
            McaOutput.Text = session.McaText;

            if (PinButton != null)
            {
                if (session.PinnedAsmText != null)
                { PinButton.Content = "⊟ unpin"; PinButton.ToolTip = $"Pinned: {session.PinnedLabel}"; }
                else
                { PinButton.Content = "⊞ pin"; PinButton.ToolTip = "Pin current ASM as baseline for diff"; }
            }

            UpdateAsmStats(session.AsmText);

            _asmHighlighter?.Clear();
            if (session.AsmMap.Count > 0)
                OnSourceCaretMoved(null, EventArgs.Empty);

            UpdateTitle();

            session.Document.TextChanged -= OnDocumentTextChanged;
            session.Document.TextChanged += OnDocumentTextChanged;
        }

        private void OnDocumentTextChanged(object? sender, EventArgs e)
        {
            if (_activeSession == null) return;
            _activeSession.IsDirty = true;
            UpdateTitle();
            RefreshTabHeader(_activeSession);
        }

        private void RefreshTabHeader(TabSession session)
        {
            var idx = _tabs.IndexOf(session);
            if (idx >= 0)
            {
                _tabs.RemoveAt(idx);
                _tabs.Insert(idx, session);
                SourceTabControl.SelectedItem = session;
            }
        }

        private void SourceTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceTabControl.SelectedItem is TabSession session)
                ActivateTab(session);
        }

        private void NewTabButton_Click(object sender, RoutedEventArgs e) => NewTab();

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.DataContext is TabSession session)
                TryCloseTab(session);
        }

        private void CloseActiveTab()
        {
            if (_activeSession != null)
                TryCloseTab(_activeSession);
        }

        private void TryCloseTab(TabSession session)
        {
            if (session.IsDirty)
            {
                var dlg = new UnsavedChangesDialog(
                    $"Save changes to {session.DisplayName.TrimEnd('•').TrimEnd(' ')}?", this);
                dlg.ShowDialog();
                var result = dlg.Result;
                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes && !SaveSession(session)) return;
            }

            var idx = _tabs.IndexOf(session);
            _tabs.Remove(session);

            if (_tabs.Count == 0)
                NewTab();
            else
                SourceTabControl.SelectedItem = _tabs[Math.Max(0, idx - 1)];
        }
    }
}
