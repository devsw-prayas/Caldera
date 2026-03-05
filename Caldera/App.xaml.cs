using System.Windows;

namespace Caldera
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Load compiler paths immediately — these don't touch WPF resources
            var prefs = PreferencesStore.Load();
            CompilerPaths.Clang = prefs.ClangPath;
            CompilerPaths.Gpp = prefs.GppPath;
            CompilerPaths.Cl = prefs.ClPath;
            CompilerPaths.Mca = prefs.McaPath;

            // Defer theme/font apply until the main window is fully loaded
            // so that Application.Current.Resources is ready
            MainWindow mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Loaded += (s, _) => PreferencesStore.Apply(prefs);
            mainWindow.Show();
        }
    }
}