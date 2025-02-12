using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace saint.Board.ava.Views
{
    public partial class MainWindow : Window
    {
        
        public MainWindow()
        {
            InitializeComponent();
        }
        
        private void CleanButton_OnClick(object? sender, RoutedEventArgs e)
        {
            PressureCanvas.ClearBoard();
        }

        private void ResetButton_OnClick(object? sender, RoutedEventArgs e)
        {
            PressureCanvas.ResetView();
        }
    }
}