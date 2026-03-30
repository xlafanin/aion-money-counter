// Скопируйте и замените всё содержимое файла MainViewModel.cs

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace AionMoneyCounter
{
    public record Faction(string Name, string Id)
    {
        public override string ToString() => Name;
    }
    public record LogMessage(string Text, Brush TextColor);

    public partial class MainViewModel : ObservableObject
    {
        private readonly LogProcessorService? _logService;
        private const int MaxLogMessages = 200;

        private static readonly Dictionary<int, Brush> _brushCache = new();
        private static Brush GetCachedBrush(System.Drawing.Color c)
        {
            int key = c.ToArgb();
            if (!_brushCache.TryGetValue(key, out var brush))
            {
                brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
                brush.Freeze();
                _brushCache[key] = brush;
            }
            return brush;
        }

        public List<Faction> Factions { get; } = new List<Faction>
        {
            new Faction("Асмодиане", "Асмодиане"),
            new Faction("Элийцы", "Элийцы")
        };

        [ObservableProperty] private Config _config = new();
        [ObservableProperty][NotifyPropertyChangedFor(nameof(IsNotRunning))] private bool _isRunning = false;
        public bool IsNotRunning => !IsRunning;
        [ObservableProperty] private string _statusText = "Остановлен";
        [ObservableProperty] private Brush _statusColor = Brushes.Gold;
        [ObservableProperty] private int _itemCount = 0;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PauseButtonText))]
        [NotifyPropertyChangedFor(nameof(PauseButtonLabel))]
        private bool _isPaused = false;
        public string PauseButtonText => IsPaused ? "▶" : "⏸";
        public string PauseButtonLabel => IsPaused ? "Продолжить" : "Пауза";
        [ObservableProperty][NotifyPropertyChangedFor(nameof(RealValue))] private double _totalGameValue = 0;
        [ObservableProperty] private double _realValue = 0;
        [ObservableProperty] private double _funPayPrice = 0;
        [ObservableProperty] private double _totalKinahDropped = 0;
        [ObservableProperty] private string _elapsedTime = "00:00:00";
        public ObservableCollection<LogMessage> LogMessages { get; } = new();

        public MainViewModel() { }

        public MainViewModel(LogProcessorService logService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            LoadSettings();

            _logService.OnLogMessage += (message, drawingColor) => Application.Current.Dispatcher.InvokeAsync(() => AddLogMessage(message, GetCachedBrush(drawingColor)));
            _logService.OnStatusChanged += (status) => Application.Current.Dispatcher.InvokeAsync(() => UpdateStatus(status));
            _logService.OnStatsUpdated += (count, gameValue, funPayPrice) => Application.Current.Dispatcher.InvokeAsync(() => UpdateStats(count, gameValue, funPayPrice));
            _logService.OnKinahDropUpdated += (total) => Application.Current.Dispatcher.InvokeAsync(() => TotalKinahDropped = total);
            _logService.OnElapsedTimeUpdated += (elapsed) => Application.Current.Dispatcher.InvokeAsync(() =>
                ElapsedTime = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}");
        }

        [RelayCommand(CanExecute = nameof(IsNotRunning))]
        private async Task Start()
        {
            LogMessages.Clear();
            ItemCount = 0;
            TotalGameValue = 0;
            FunPayPrice = 0;
            UpdateRealValue();

            SaveSettings();
            await _logService!.Start(Config);
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        private void Stop()
        {
            IsPaused = false;
            _logService!.SetPaused(false);
            _logService!.Stop();
        }

        public void StopCommandWithoutSave()
        {
            IsPaused = false;
            _logService!.SetPaused(false);
            _logService!.StopWithoutSave();
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        private void AddKinah(string? amountString)
        {
            string cleanAmount = new string(amountString?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());
            if (double.TryParse(cleanAmount, out double amount) && amount > 0)
            {
                _logService!.AddManualKinah(amount);
            }
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        private void SubtractKinah(string? amountString)
        {
            string cleanAmount = new string(amountString?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());
            if (double.TryParse(cleanAmount, out double amount) && amount > 0)
            {
                _logService!.SubtractManualKinah(amount);
            }
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        private void Pause()
        {
            IsPaused = !IsPaused;
            _logService!.SetPaused(IsPaused);
            UpdateStatus(IsPaused ? "Пауза" : "Работает");
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        private void NewSession()
        {
            ItemCount = 0;
            TotalGameValue = 0;
            TotalKinahDropped = 0;
            UpdateRealValue();
            _logService!.ResetSession();
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        private void RefreshPrices()
        {
            _logService!.RefreshItemPrices();
        }

        [RelayCommand]
        private void ClearLog() => LogMessages.Clear();

        [RelayCommand(CanExecute = nameof(IsNotRunning))]
        private void BrowseLogFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Log Files (*.log)|*.log|All files (*.*)|*.*",
                Title = "Выберите лог-файл Aion (Chat.log)"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                Config.LogFilePath = openFileDialog.FileName;
            }
        }

        private void AddLogMessage(string text, Brush color)
        {
            if (LogMessages.Count >= MaxLogMessages) LogMessages.RemoveAt(0);
            LogMessages.Add(new LogMessage(text, color));
        }

        private void UpdateStatus(string status)
        {
            bool shouldBeRunning;
            StatusText = status;

            switch (status)
            {
                case "Запущен":
                case "Работает":
                    StatusColor = Brushes.MediumSeaGreen;
                    shouldBeRunning = true;
                    break;
                case "Пауза":
                    StatusColor = Brushes.Orange;
                    shouldBeRunning = true;
                    break;
                default:
                    StatusColor = (status == "Остановлен") ? Brushes.Gold : Brushes.Tomato;
                    shouldBeRunning = false;
                    break;
            }

            IsRunning = shouldBeRunning;

            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            AddKinahCommand.NotifyCanExecuteChanged();
            SubtractKinahCommand.NotifyCanExecuteChanged();
            BrowseLogFileCommand.NotifyCanExecuteChanged();
            NewSessionCommand.NotifyCanExecuteChanged();
            RefreshPricesCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
        }

        private void UpdateStats(int count, double gameValue, double funPayPrice)
        {
            if (count != -1) ItemCount = count;
            if (gameValue != -1) TotalGameValue = gameValue;
            if (funPayPrice > 0) FunPayPrice = funPayPrice;
            UpdateRealValue();
        }

        private void UpdateRealValue()
        {
            if (Config.FunPayIntegrationEnabled && _funPayPrice > 0 && Config.InGameCurrencyRate > 0)
            {
                decimal grossValue = (decimal)(TotalGameValue / 1000000) * (decimal)_funPayPrice;

                decimal commission1 = 1 - ((decimal)Config.FunPayCommissionPercent / 100m);
                decimal commission2 = 1 - ((decimal)Config.WithdrawalCommissionPercent / 100m);

                decimal netValue = grossValue * commission1 * commission2;

                RealValue = (double)netValue;
            }
            else
            {
                RealValue = 0;
            }

            string outputPath = Config.OutputFilePath;
            string outputValue = RealValue.ToString("F2", CultureInfo.InvariantCulture);
            Task.Run(() =>
            {
                try
                {
                    File.WriteAllText(outputPath, outputValue);
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                        AddLogMessage($"[ERROR] Не удалось записать в файл '{outputPath}': {ex.Message}",
                            ex.Message.StartsWith("Процесс не может") ? Brushes.Yellow : Brushes.Red));
                }
            });
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists("config.json"))
                {
                    var json = File.ReadAllText("config.json", Encoding.GetEncoding("windows-1251"));
                    Config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
                }
            }
            catch (Exception ex) { AddLogMessage($"Ошибка загрузки config.json: {ex.Message}", Brushes.Red); }
        }

        private void SaveSettings()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                var json = JsonSerializer.Serialize(Config, options);
                File.WriteAllText("config.json", json, Encoding.GetEncoding("windows-1251"));
            }
            catch (Exception ex) { AddLogMessage($"Ошибка сохранения config.json: {ex.Message}", Brushes.Red); }
        }
    }
}