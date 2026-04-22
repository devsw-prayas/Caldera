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

            if (_activeSession != null)
            {
                session.Compiler = _activeSession.Compiler;
                session.Std = _activeSession.Std;
                session.Flags = _activeSession.Flags;
                session.McaFlags = _activeSession.McaFlags;
            }

            AddAndActivateTab(session);
        }

        private void RestoreTab(PersistedTab pt)
        {
            var session = new TabSession
            {
                FilePath = pt.FilePath,
                Compiler = pt.Compiler != null ? pt.Compiler : "clang++",
                Std = pt.Std != null ? pt.Std : "c++20",
                Flags = pt.Flags != null ? pt.Flags : "-O2 -march=native",
                McaFlags = pt.McaFlags != null ? pt.McaFlags : "--mcpu=native"
            };
            if (pt.Content != null)
                session.Document.Text = pt.Content;

            AddAndActivateTab(session);
        }

        private void AddAndActivateTab(TabSession session)
        {
            _tabs.Add(session);
            SourceTabControl.ItemsSource = _tabs;
            SourceTabControl.SelectedItem = session;
            ActivateTab(session);
        }

        private void ActivateTab(TabSession session)
        {
            if (_activeSession == session) return;
            _activeSession = session;

            if (CompilerSelector != null)
            {
                foreach (CompilerInfo item in CompilerSelector.Items)
                    if (item.Name == session.Compiler)
                    { CompilerSelector.SelectedItem = item; break; }

                foreach (ComboBoxItem item in StdSelector.Items)
                    if (item.Content?.ToString() == session.Std)
                    { StdSelector.SelectedItem = item; break; }

                FlagsInput.Text = session.Flags;
                McaFlagsInput.Text = session.McaFlags;
            }

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
            TriggerDebouncedCompile();
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
