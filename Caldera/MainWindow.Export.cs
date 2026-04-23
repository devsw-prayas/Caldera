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
        private void ExportImage_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSession == null) return;

            var sfd = new SaveFileDialog
            {
                Title = "Export to Image (PNG)",
                Filter = "PNG files (*.png)|*.png",
                FileName = $"caldera_asm_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                DefaultExt = ".png"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    // Render the AsmOutput control to a bitmap
                    var target = AsmOutput;
                    double width = target.ActualWidth;
                    double height = target.ActualHeight;

                    if (width <= 0 || height <= 0)
                    {
                        width = 800;
                        height = 600;
                    }

                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)width, (int)height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);

                    rtb.Render(target);

                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

                    using (var stream = File.Create(sfd.FileName))
                    {
                        encoder.Save(stream);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to export image:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
