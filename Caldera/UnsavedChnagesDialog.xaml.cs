using System.Windows;
using System.Windows.Input;

namespace Caldera
{
    public partial class UnsavedChangesDialog : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;

        public UnsavedChangesDialog(string message, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            MessageText.Text = message;
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            Close();
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();

        // Escape = cancel
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Result = MessageBoxResult.Cancel; Close(); }
            base.OnKeyDown(e);
        }
    }
}