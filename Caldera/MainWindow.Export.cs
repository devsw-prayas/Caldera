using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Caldera
{
    public partial class MainWindow
    {
        private void ExportHtml_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSession == null) return;
            
            var sfd = new SaveFileDialog
            {
                Title = "Export to HTML",
                Filter = "HTML files (*.html)|*.html",
                FileName = $"caldera_export_{DateTime.Now:yyyyMMdd_HHmmss}.html",
                DefaultExt = ".html"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var html = Core.ExportService.GenerateHtml(_activeSession);
                    File.WriteAllText(sfd.FileName, html);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to export HTML:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportMarkdown_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSession == null) return;
            
            var sfd = new SaveFileDialog
            {
                Title = "Export to Markdown",
                Filter = "Markdown files (*.md)|*.md",
                FileName = $"caldera_export_{DateTime.Now:yyyyMMdd_HHmmss}.md",
                DefaultExt = ".md"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var md = Core.ExportService.GenerateMarkdown(_activeSession);
                    File.WriteAllText(sfd.FileName, md);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to export Markdown:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
