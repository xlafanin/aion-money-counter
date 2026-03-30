using CommunityToolkit.Mvvm.ComponentModel;

namespace AionMoneyCounter
{
    public partial class Config : ObservableObject
    {
        [ObservableProperty]
        private string _logFilePath = "Сhat.log";

        [ObservableProperty]
        private string _outputFilePath = "counter.txt";

        [ObservableProperty]
        private int _checkIntervalSeconds = 1;

        // ЭТА СТРОКА ДОЛЖНА БЫТЬ "windows-1251"
        [ObservableProperty]
        private string _logFileEncoding = "windows-1251";

        [ObservableProperty]
        private string _currencySymbol = "RUB";

        [ObservableProperty]
        private bool _debugMode = false;

        [ObservableProperty]
        private bool _funPayIntegrationEnabled = true;

        [ObservableProperty]
        private string _funPayApiUrl = "http://64.188.107.139:8000/offers/17/min_price";

        // URL для загрузки прайс-листа предметов с брокер-бота
        // GET {ItemPricesApiUrl}/{FunPayFactionId} → {item_id: price}
        [ObservableProperty]
        private string _itemPricesApiUrl = "http://64.188.107.139:8000/item-prices";

        [ObservableProperty]
        private string _funPayFactionId = "Асмодиане";

        [ObservableProperty]
        private string _funPayServer = "";

        [ObservableProperty]
        private int _funPayUpdateIntervalSeconds = 300;

        [ObservableProperty]
        private double _inGameCurrencyRate = 1000000;

        [ObservableProperty]
        private double _funPayCommissionPercent = 19.0;

        [ObservableProperty]
        private double _withdrawalCommissionPercent = 3.0;

        [ObservableProperty]
        private List<string> _confirmationMessages = new List<string> { ": Все у меня!", ": ??? ? ????!", ": Looted!" };

        [ObservableProperty]
        private List<string> _itemLinePrefixes = new List<string>
        {
            "Получено:",
            "????????:",
            "You have acquired"
        };

        [ObservableProperty]
        private int _lootLookbackSeconds = 3;

        // Если true — предметы считаются сразу по префиксу строки, без ожидания подтверждения
        [ObservableProperty]
        private bool _directLootMode = false;

        [ObservableProperty]
        private string _watchedItemsFilePath = "watched_items.txt";

        [ObservableProperty]
        private string _itemDropsOutputPath = "item_drops.txt";
    }
}