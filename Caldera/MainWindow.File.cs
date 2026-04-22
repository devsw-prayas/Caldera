using Microsoft.Win32;
using System.Windows;

namespace Caldera
{
    public partial class MainWindow
    {
        // ── File operations ───────────────────────────────────────────────────

        private void NewFile_Click(object sender, RoutedEventArgs e) => NewTab();

        private void OpenFile_Click(object sender, RoutedEventArgs e) => Open();

        private void SaveFile_Click(object sender, RoutedEventArgs e) => Save();

        private void SaveAsFile_Click(object sender, RoutedEventArgs e) => SaveAs();

        private void Open()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open Source File",
                Filter = "C++ Files (*.cpp;*.cxx;*.cc;*.h;*.hpp)|*.cpp;*.cxx;*.cc;*.h;*.hpp|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            var existing = _tabs.FirstOrDefault(t => t.FilePath == dlg.FileName);
            if (existing != null) { SourceTabControl.SelectedItem = existing; return; }

            var content = System.IO.File.ReadAllText(dlg.FileName);
            NewTab(dlg.FileName, content);
            _activeSession!.IsDirty = false;
            UpdateTitle();
        }

        private void Save()
        {
            if (_activeSession == null) return;
            SaveSession(_activeSession);
        }

        private void SaveAs()
        {
            if (_activeSession == null) return;
            SaveSessionAs(_activeSession);
        }

        private bool SaveSession(TabSession session)
        {
            if (session.FilePath == null)
                return SaveSessionAs(session);

            System.IO.File.WriteAllText(session.FilePath, session.Document.Text);
            session.IsDirty = false;
            UpdateTitle();
            RefreshTabHeader(session);
            return true;
        }

        private bool SaveSessionAs(TabSession session)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save Source File",
                Filter = "C++ Files (*.cpp)|*.cpp|Header Files (*.h;*.hpp)|*.h;*.hpp|All Files (*.*)|*.*",
                DefaultExt = ".cpp",
                FileName = session.FilePath ?? "untitled.cpp"
            };
            if (dlg.ShowDialog() != true) return false;

            session.FilePath = dlg.FileName;
            System.IO.File.WriteAllText(session.FilePath, session.Document.Text);
            session.IsDirty = false;
            UpdateTitle();
            RefreshTabHeader(session);
            return true;
        }
    }
}
