using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AionMoneyCounter
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;

            viewModel.LogMessages.CollectionChanged += (s, e) =>
            {
                if (LogListView.Items.Count > 0)
                    Dispatcher.InvokeAsync(() => LogListView.ScrollIntoView(LogListView.Items[^1]), DispatcherPriority.Background);
            };
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel.IsRunning)
            {
                var result = MessageBox.Show(
                    "Сохранить текущую сессию перед выходом?",
                    "Выход",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (result == MessageBoxResult.Yes)
                    _viewModel.StopCommand.Execute(null);  // Stop вызывает SaveLastSession
                else
                    _viewModel.StopCommandWithoutSave();   // просто отменяем токен
            }

            base.OnClosing(e);
        }

        private static readonly NumberFormatInfo _dotThousands = new NumberFormatInfo
        {
            NumberGroupSeparator = ".",
            NumberGroupSizes = new[] { 3 },
            NumberDecimalDigits = 0
        };

        private void ManualKinahAmount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            textBox.TextChanged -= ManualKinahAmount_TextChanged;

            int selectionStart = textBox.SelectionStart;
            string originalText = textBox.Text;

            string cleanText = new string(originalText.Where(char.IsDigit).ToArray());

            if (long.TryParse(cleanText, out long number))
            {
                string formattedText = number.ToString("N", _dotThousands);
                textBox.Text = formattedText;

                int newSelectionStart = selectionStart + (formattedText.Length - originalText.Length);
                textBox.SelectionStart = Math.Max(0, newSelectionStart);
            }
            else if (string.IsNullOrEmpty(cleanText))
            {
                textBox.Text = "";
            }

            textBox.TextChanged += ManualKinahAmount_TextChanged;
        }
    }
}