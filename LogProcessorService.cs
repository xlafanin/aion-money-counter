using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AionMoneyCounter
{
    public class LogProcessorService
    {
        public event Action<string, Color> OnLogMessage = delegate { };
        public event Action<int, double, double> OnStatsUpdated = delegate { };
        public event Action<double> OnKinahDropUpdated = delegate { };
        public event Action<TimeSpan> OnElapsedTimeUpdated = delegate { };
        public event Action<string> OnStatusChanged = delegate { };

        private Config _config = null!;
        private Dictionary<string, double> _itemPrices = null!;
        private CancellationTokenSource _cancellationTokenSource = null!;
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private double _totalInGameValue = 0;
        private int _totalItemCount = 0;
        private double _totalKinahDropped = 0;

        // Watched items: id → (name, count)
        private Dictionary<string, (string Name, int Count)> _watchedItems = new();
        private volatile bool _resetSessionRequested = false;
        private volatile bool _refreshPricesRequested = false;
        private volatile bool _isPaused = false;
        private readonly Dictionary<string, int> _sessionLoot = new();
        private string _sessionJsonPath = string.Empty;
        private TimeSpan _currentElapsed = TimeSpan.Zero;

        // --- ИЗМЕНЕНИЕ ЗДЕСЬ ---
        // Локальные переменные для хранения полных путей
        private string _fullOutputFilePath = string.Empty;
        private string _fullPriceFilePath = string.Empty;


        public async Task Start(Config config)
        {
            if (config is null)
            {
                Log(LogLevel.Error, "Конфигурация не была передана в сервис.");
                OnStatusChanged?.Invoke("Ошибка");
                return;
            }
            _config = config;

            // --- ИЗМЕНЕНИЕ ЗДЕСЬ ---
            // Преобразуем относительные пути в полные и сохраняем их в локальные поля,
            // не затрагивая сам объект настроек (_config).
            try
            {
                _fullOutputFilePath = Path.GetFullPath(_config.OutputFilePath);
                var faction = string.IsNullOrWhiteSpace(_config.FunPayFactionId) ? "default" : _config.FunPayFactionId;
                _fullPriceFilePath = Path.GetFullPath($"prices_{faction}.txt");
                _sessionJsonPath   = Path.Combine(Path.GetDirectoryName(_fullOutputFilePath)!, "session.json");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Недопустимое имя файла в настройках: {ex.Message}");
                OnStatusChanged?.Invoke("Ошибка");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _totalInGameValue = 0;
            _totalItemCount = 0;
            _totalKinahDropped = 0;
            LoadWatchedItems();

            Log(LogLevel.Info, "Запуск сервиса...");
            LoadLastSession();

            // Загружаем локальный файл как базу, сервер перезаписывает совпадающие ID
            var serverPrices = await LoadPricesFromServer(_config.ItemPricesApiUrl, _config.FunPayFactionId);
            var localPrices  = LoadPrices(_fullPriceFilePath);
            if (serverPrices == null && localPrices == null)
            {
                Log(LogLevel.Error, "Не удалось загрузить прайс-лист ни с сервера, ни из файла.");
                OnStatusChanged?.Invoke("Ошибка");
                return;
            }
            _itemPrices = localPrices ?? new Dictionary<string, double>();
            if (serverPrices != null)
                foreach (var kv in serverPrices)
                    _itemPrices[kv.Key] = kv.Value;

            OnStatusChanged?.Invoke("Запущен");
            try
            {
                await Task.WhenAll(
                    FunPayPriceUpdaterLoop(_cancellationTokenSource.Token),
                    LogMonitorLoop(_cancellationTokenSource.Token)
                );
            }
            catch (OperationCanceledException)
            {
                Log(LogLevel.Warn, "Сервис остановлен пользователем.");
            }
            finally
            {
                OnStatusChanged?.Invoke("Остановлен");
            }
        }

        public void Stop()
        {
            SaveLastSession();
            Log(LogLevel.Warn, "Инициирована остановка сервиса...");
            _cancellationTokenSource?.Cancel();
        }

        public void StopWithoutSave()
        {
            Log(LogLevel.Warn, "Остановка без сохранения сессии...");
            _cancellationTokenSource?.Cancel();
        }

        public void AddManualKinah(double amount)
        {
            if (amount > 0)
            {
                _totalInGameValue += amount;
                Log(LogLevel.Success, $"Вручную добавлено {amount:N0} кинар.");
                OnStatsUpdated?.Invoke(_totalItemCount, _totalInGameValue, -1);
            }
        }

        public void ResetSession()
        {
            SaveLastSession();
            _totalInGameValue = 0;
            _totalItemCount = 0;
            _totalKinahDropped = 0;
            _sessionLoot.Clear();
            foreach (var key in _watchedItems.Keys.ToList())
                _watchedItems[key] = (_watchedItems[key].Name, 0);
            WriteItemDrops();
            _resetSessionRequested = true;
        }

        private void LoadLastSession()
        {
            try
            {
                var path = Path.Combine(Path.GetDirectoryName(_fullOutputFilePath)!, "last_session.json");
                if (!File.Exists(path)) return;

                var json = File.ReadAllText(path, new System.Text.UTF8Encoding(false));
                var node = System.Text.Json.Nodes.JsonNode.Parse(json);
                if (node == null) return;

                string date = node["date"]?.ToString() ?? "?";
                string elapsed = node["elapsedTime"]?.ToString() ?? "00:00:00";
                int itemCount = node["itemCount"]?.GetValue<int>() ?? 0;
                long kinahFromItems = node["kinahFromItems"]?.GetValue<long>() ?? 0;
                long kinahFromMobs = node["kinahFromMobs"]?.GetValue<long>() ?? 0;

                _totalItemCount = itemCount;
                _totalInGameValue = kinahFromItems;
                _totalKinahDropped = kinahFromMobs;

                Log(LogLevel.Info, $"[Прошлая сессия] {date} | время: {elapsed} | предметов: {itemCount} | кинара с предметов: {kinahFromItems:N0} | кинара с мобов: {kinahFromMobs:N0}");

                if (itemCount > 0 || kinahFromItems > 0)
                    OnStatsUpdated?.Invoke(_totalItemCount, _totalInGameValue, -1);
                if (kinahFromMobs > 0)
                    OnKinahDropUpdated?.Invoke(_totalKinahDropped);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warn, $"[Сессия] Не удалось загрузить прошлую сессию: {ex.Message}");
            }
        }

        private void SaveLastSession()
        {
            if (_totalItemCount == 0 && _totalKinahDropped == 0 && _totalInGameValue == 0) return;
            try
            {
                var path = Path.Combine(Path.GetDirectoryName(_fullOutputFilePath)!, "last_session.json");
                var e = _currentElapsed;
                var timeStr = $"{(int)e.TotalHours:D2}:{e.Minutes:D2}:{e.Seconds:D2}";
                var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var json = "{\n" +
                    $"  \"date\": \"{date}\",\n" +
                    $"  \"elapsedTime\": \"{timeStr}\",\n" +
                    $"  \"itemCount\": {_totalItemCount},\n" +
                    $"  \"kinahFromItems\": {(long)_totalInGameValue},\n" +
                    $"  \"kinahFromMobs\": {(long)_totalKinahDropped}\n" +
                    "}";
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
                Log(LogLevel.Info, $"[Сессия сохранена] {timeStr} | предметов: {_totalItemCount} | кинара с мобов: {_totalKinahDropped:N0}");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"[Сессия] Не удалось сохранить сессию: {ex.Message}");
            }
        }

        private void LoadWatchedItems()
        {
            _watchedItems.Clear();
            string path = Path.GetFullPath(_config.WatchedItemsFilePath);
            if (!File.Exists(path))
            {
                Log(LogLevel.Info, $"Файл watched_items.txt не найден — слежка за предметами отключена.");
                return;
            }
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var parts = line.Split(';');
                string id = parts[0].Trim();
                string name = parts.Length >= 2 ? parts[1].Trim() : id;
                if (!string.IsNullOrEmpty(id))
                    _watchedItems[id] = (name, 0);
            }
            Log(LogLevel.Info, $"Слежка за предметами: {_watchedItems.Count} ID загружено.");
            WriteItemDrops();
        }

        private void WriteItemDrops()
        {
            if (_watchedItems.Count == 0) return;
            try
            {
                string path = Path.GetFullPath(_config.ItemDropsOutputPath);
                var lines = _watchedItems.Select(kv => $"{kv.Key};{kv.Value.Name};{kv.Value.Count}");
                File.WriteAllLines(path, lines, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warn, $"[Слежка] Не удалось записать item_drops: {ex.Message}");
            }
        }

        public void RefreshItemPrices()
        {
            _refreshPricesRequested = true;
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
        }

        private void WriteSessionJson(long effectiveStartMs, bool paused, long frozenMs = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(_sessionJsonPath)) return;
                var json = paused
                    ? $"{{\"startedAt\":{effectiveStartMs},\"paused\":true,\"frozenMs\":{frozenMs}}}"
                    : $"{{\"startedAt\":{effectiveStartMs},\"paused\":false}}";
                File.WriteAllText(_sessionJsonPath, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warn, $"[Сессия] Не удалось записать session.json: {ex.Message}");
            }
        }

        public void SubtractManualKinah(double amount)
        {
            if (amount > 0)
            {
                _totalInGameValue -= amount;
                Log(LogLevel.Warn, $"Вручную отнято {amount:N0} кинар.");
                OnStatsUpdated?.Invoke(_totalItemCount, _totalInGameValue, -1);
            }
        }

        // --- ИЗМЕНЕНИЕ ЗДЕСЬ ---
        // Везде, где используются пути, теперь стоят локальные переменные
        // с полными путями, а не _config.

        private async Task LogMonitorLoop(CancellationToken token)
        {
            long lastFilePosition = -1;
            var logBuffer = new List<LogEntry>();
            const int LOG_BUFFER_MAX_SECONDS = 30;
            bool wasPaused = false;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var lastAutoSave = DateTime.UtcNow;
            WriteSessionJson(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), false);
            try
            {

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Обновляем время: паузируем секундомер во время паузы
                    if (_isPaused && stopwatch.IsRunning)
                    {
                        stopwatch.Stop();
                        long effStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)stopwatch.Elapsed.TotalMilliseconds;
                        WriteSessionJson(effStart, true, (long)stopwatch.Elapsed.TotalMilliseconds);
                    }
                    else if (!_isPaused && !stopwatch.IsRunning)
                    {
                        stopwatch.Start();
                        long effStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)stopwatch.Elapsed.TotalMilliseconds;
                        WriteSessionJson(effStart, false);
                    }

                    _currentElapsed = stopwatch.Elapsed;
                    OnElapsedTimeUpdated?.Invoke(stopwatch.Elapsed);

                    if (_refreshPricesRequested)
                    {
                        _refreshPricesRequested = false;
                        var refreshedServer = await LoadPricesFromServer(_config.ItemPricesApiUrl, _config.FunPayFactionId);
                        var refreshedLocal  = LoadPrices(_fullPriceFilePath);
                        if (refreshedServer != null || refreshedLocal != null)
                        {
                            _itemPrices = refreshedLocal ?? new Dictionary<string, double>();
                            if (refreshedServer != null)
                                foreach (var kv in refreshedServer)
                                    _itemPrices[kv.Key] = kv.Value;
                            // Пересчитываем стоимость сессии по новым ценам
                            double recalculated = _sessionLoot.Sum(kv =>
                                _itemPrices.TryGetValue(kv.Key, out double p) ? p * kv.Value : 0);
                            _totalInGameValue = recalculated;
                            OnStatsUpdated?.Invoke(_totalItemCount, _totalInGameValue, -1);
                            Log(LogLevel.Info, $"[Цены] Обновлены и пересчитаны. Новая сумма: {recalculated:N0}");
                        }
                    }

                    if (_resetSessionRequested)
                    {
                        _resetSessionRequested = false;
                        lastFilePosition = -1;
                        logBuffer.Clear();
                        _sessionLoot.Clear();
                        _totalKinahDropped = 0;
                        stopwatch.Restart();
                        WriteSessionJson(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), false);
                        Log(LogLevel.Info, "Новая сессия начата.");
                        OnStatsUpdated?.Invoke(0, 0, -1);
                    }

                    if (_isPaused)
                    {
                        wasPaused = true;
                        await Task.Delay(TimeSpan.FromSeconds(_config.CheckIntervalSeconds), token);
                        continue;
                    }

                    // Только что сняли паузу — пропускаем всё накопленное в логе за время паузы
                    if (wasPaused)
                    {
                        wasPaused = false;
                        lastFilePosition = -1;
                        logBuffer.Clear();
                    }

                    if (File.Exists(_config.LogFilePath))
                    {
                        using (var fs = new FileStream(_config.LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            if (lastFilePosition == -1)
                                lastFilePosition = fs.Length;
                            else if (fs.Length < lastFilePosition)
                                lastFilePosition = 0;
                            fs.Seek(lastFilePosition, SeekOrigin.Begin);

                            using (var sr = new StreamReader(fs, GetLogEncoding()))
                            {
                                string? line;
                                while ((line = await sr.ReadLineAsync(token)) != null)
                                {
                                    ParseAndBufferLine(line, logBuffer);
                                }
                                lastFilePosition = fs.Position;
                            }
                        }

                        var (eventValue, eventCount) = FindAndProcessLootEvents(logBuffer);
                        if (eventValue > 0)
                        {
                            _totalInGameValue += eventValue;
                            _totalItemCount += eventCount;
                            OnStatsUpdated?.Invoke(_totalItemCount, _totalInGameValue, -1);
                        }
                    }
                    else
                    {
                        Log(LogLevel.Warn, $"Файл лога не найден: {_config.LogFilePath}");
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Log(LogLevel.Error, $"Ошибка в цикле мониторинга: {ex.Message}");
                }

                var cutoff = DateTime.Now.AddSeconds(-LOG_BUFFER_MAX_SECONDS);
                logBuffer.RemoveAll(e => e.Timestamp < cutoff);

                if (!_isPaused && (DateTime.UtcNow - lastAutoSave).TotalMinutes >= 5)
                {
                    SaveLastSession();
                    lastAutoSave = DateTime.UtcNow;
                }

                await Task.Delay(_config.CheckIntervalSeconds * 1000, token);
            }
            }
            finally
            {
                stopwatch.Stop();
                long effStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)stopwatch.Elapsed.TotalMilliseconds;
                WriteSessionJson(effStart, true, (long)stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        private async Task FunPayPriceUpdaterLoop(CancellationToken token)
        {
            double currentFunPayPrice = 0;
            while (!token.IsCancellationRequested)
            {
                if (_config.FunPayIntegrationEnabled)
                {
                    try
                    {
                        string apiUrl = $"{_config.FunPayApiUrl}?side={_config.FunPayFactionId}";
                        if (!string.IsNullOrEmpty(_config.FunPayServer))
                            apiUrl += $"&server={Uri.EscapeDataString(_config.FunPayServer)}";
                        var response = await _httpClient.GetAsync(apiUrl, token);
                        if (response.IsSuccessStatusCode)
                        {
                            string jsonString = await response.Content.ReadAsStringAsync(token);
                            var jsonNode = JsonNode.Parse(jsonString);
                            string? priceText = jsonNode?["min_price_rub"]?.ToString() ?? "0.0";

                            if (double.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out double newPrice) && currentFunPayPrice != newPrice)
                            {
                                currentFunPayPrice = newPrice;
                                Log(LogLevel.Success, $"[FunPay] Цена обновлена: {currentFunPayPrice:F2} {_config.CurrencySymbol}");
                                OnStatsUpdated?.Invoke(-1, -1, currentFunPayPrice);
                            }
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        Log(LogLevel.Error, $"Ошибка обновления цены FunPay: {ex.Message}");
                    }
                }
                await Task.Delay(_config.FunPayUpdateIntervalSeconds * 1000, token);
            }
        }

        private void ParseAndBufferLine(string line, List<LogEntry> buffer)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (line.Length >= 19 && DateTime.TryParseExact(line.Substring(0, 19), "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timestamp))
            {
                string content = line.Substring(21).Trim();
                buffer.Add(new LogEntry(timestamp, content));
                if (_config.DebugMode) Log(LogLevel.Debug, $"В буфер: {timestamp:T} | {content}");
            }
        }

        private (double totalValue, int totalCount) FindAndProcessLootEvents(List<LogEntry> buffer)
        {
            double sessionValue = 0;
            int sessionCount = 0;

            IEnumerable<LogEntry> itemCandidates;

            if (_config.DirectLootMode || _config.ConfirmationMessages.Count == 0)
            {
                // Прямой режим: считаем сразу все необработанные строки с префиксом
                itemCandidates = buffer.Where(e => !e.IsProcessed &&
                    _config.ItemLinePrefixes.Any(p => e.Content.StartsWith(p)));
            }
            else
            {
                // Режим подтверждения: ищем предметы в окне перед фразой-подтверждением
                var confirmationEntries = buffer
                    .Where(e => !e.IsProcessed && _config.ConfirmationMessages.Any(msg => e.Content.Contains(msg)))
                    .ToList();

                var collected = new List<LogEntry>();
                foreach (var confirmation in confirmationEntries)
                {
                    confirmation.IsProcessed = true;
                    var windowStart = confirmation.Timestamp.AddSeconds(-_config.LootLookbackSeconds);
                    var inWindow = buffer.Where(e => !e.IsProcessed &&
                        e.Timestamp >= windowStart && e.Timestamp <= confirmation.Timestamp &&
                        _config.ItemLinePrefixes.Any(p => e.Content.StartsWith(p)));
                    collected.AddRange(inWindow);
                }
                itemCandidates = collected;
            }

            foreach (var itemEntry in itemCandidates)
            {
                Match match = Regex.Match(itemEntry.Content, @"(?:(\d+)\s+)?\[item:(\d+);");
                if (match.Success)
                {
                    string itemId = match.Groups[2].Value;
                    // EN: "10 [item:...]" — кол-во перед скобкой (группа 1)
                    // RU: "[item:...] (10 шт.)" — кол-во после скобки
                    int qty;
                    if (int.TryParse(match.Groups[1].Value, out int qBefore) && qBefore > 0)
                        qty = qBefore;
                    else
                    {
                        var qAfter = Regex.Match(itemEntry.Content, @"\((\d+)\s*\S*\)");
                        qty = qAfter.Success && int.TryParse(qAfter.Groups[1].Value, out int qa) && qa > 0 ? qa : 1;
                    }

                    if (_itemPrices.TryGetValue(itemId, out double itemValue))
                    {
                        itemEntry.IsProcessed = true;
                        sessionValue += itemValue * qty;
                        sessionCount += qty;
                        _sessionLoot[itemId] = (_sessionLoot.TryGetValue(itemId, out int prev) ? prev : 0) + qty;
                        Log(LogLevel.Success, $"[ЛУТ] ID:{itemId} x{qty} (+{itemValue * qty:N0})");
                    }

                    if (_watchedItems.TryGetValue(itemId, out var watched))
                    {
                        itemEntry.IsProcessed = true;
                        _watchedItems[itemId] = (watched.Name, watched.Count + qty);
                        WriteItemDrops();
                        Log(LogLevel.Info, $"[СЛЕЖКА] {watched.Name} x{qty} (всего: {watched.Count + qty})");
                    }
                }
            }
            // Kinah drops — EN: "You have earned X Kinah." / RU: "Получено кинаров: X."
            var kinahEntries = buffer.Where(e => !e.IsProcessed &&
                (e.Content.StartsWith("You have earned") ||
                 e.Content.StartsWith("Получено кинаров") ||
                 Regex.IsMatch(e.Content, @"^\?+\s+\?+:"))).ToList();
            foreach (var entry in kinahEntries)
            {
                var m = Regex.Match(entry.Content,
                    @"(?:You have earned|[Пп]олучено кинаров:|\?+\s+\?+:)\s*([\d\s\u00a0]+?)(?:\s*Kinah)?\.");
                if (m.Success)
                {
                    string numStr = m.Groups[1].Value.Replace("\u00a0", "").Replace(" ", "");
                    if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double kinah) && kinah > 0)
                    {
                        entry.IsProcessed = true;
                        _totalKinahDropped += kinah;
                        if (_config.DebugMode) Log(LogLevel.Debug, $"[КИНАРА] +{kinah:N0}");
                        OnKinahDropUpdated?.Invoke(_totalKinahDropped);
                    }
                }
            }

            return (sessionValue, sessionCount);
        }

        private async Task<Dictionary<string, double>?> LoadPricesFromServer(string baseUrl, string faction)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(faction))
                return null;
            try
            {
                string url = $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(faction)}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Log(LogLevel.Warn, $"[Сервер] Прайс-лист недоступен ({(int)response.StatusCode}), использую локальный файл.");
                    return null;
                }
                string json = await response.Content.ReadAsStringAsync();
                var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(json);
                if (jsonNode is not System.Text.Json.Nodes.JsonObject obj) return null;

                var prices = new Dictionary<string, double>();
                foreach (var kv in obj)
                    if (double.TryParse(kv.Value?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double p))
                        prices[kv.Key] = p;

                Log(LogLevel.Success, $"[Сервер] Прайс-лист загружен для '{faction}': {prices.Count} позиций.");
                MergeAndSavePrices(_fullPriceFilePath, prices);
                return prices;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warn, $"[Сервер] Ошибка загрузки прайс-листа: {ex.Message}. Использую локальный файл.");
                return null;
            }
        }

        private void MergeAndSavePrices(string filePath, Dictionary<string, double> serverPrices)
        {
            try
            {
                var pricesEncoding = new System.Text.UTF8Encoding(false);
                var existingLines = File.Exists(filePath)
                    ? File.ReadAllLines(filePath, pricesEncoding).ToList()
                    : new List<string>();

                int added = 0, updated = 0;

                // Обновляем существующие строки если цена изменилась
                for (int i = 0; i < existingLines.Count; i++)
                {
                    var line = existingLines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    var parts = line.Split(';');
                    if (parts.Length < 2) continue;
                    string id = parts[0].Trim().TrimStart('\uFEFF');
                    if (!serverPrices.TryGetValue(id, out double newPrice)) continue;
                    if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double oldPrice)
                        || oldPrice != newPrice)
                    {
                        existingLines[i] = $"{id};{newPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                        updated++;
                    }
                }

                // Добавляем новые ID которых нет в файле
                var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in existingLines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    var parts = line.Split(';');
                    if (parts.Length >= 1) existingIds.Add(parts[0].Trim().TrimStart('\uFEFF'));
                }
                foreach (var kv in serverPrices)
                {
                    if (!existingIds.Contains(kv.Key))
                    {
                        existingLines.Add($"{kv.Key};{kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                        added++;
                    }
                }

                // Дедупликация — убираем повторяющиеся ID
                int beforeDedup = existingLines.Count;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = existingLines.Count - 1; i >= 0; i--)
                {
                    var p = existingLines[i].Split(';');
                    string lineId = p.Length >= 1 ? p[0].Trim().TrimStart('\uFEFF') : "";
                    if (!string.IsNullOrEmpty(lineId) && !seen.Add(lineId))
                        existingLines.RemoveAt(i);
                }
                int removed = beforeDedup - existingLines.Count;

                if (added > 0 || updated > 0 || removed > 0)
                {
                    File.WriteAllLines(filePath, existingLines, pricesEncoding);
                    Log(LogLevel.Info, $"[Сервер] Прайс-лист обновлён: +{added} новых, ~{updated} изменённых, -{removed} дублей.");
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warn, $"[Сервер] Не удалось обновить прайс-лист: {ex.Message}");
            }
        }

        private Dictionary<string, double>? LoadPrices(string filePath)
        {
            var prices = new Dictionary<string, double>();
            if (!File.Exists(filePath))
            {
                Log(LogLevel.Error, $"Файл прайс-листа не найден: {filePath}");
                return null;
            }
            try
            {
                var lines = File.ReadAllLines(filePath, GetLogEncoding());
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("#")) continue;
                    var parts = line.Split(';');
                    if (parts.Length == 2 && double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double price))
                    {
                        prices[parts[0].Trim()] = price;
                    }
                }
                Log(LogLevel.Info, $"Прайс-лист успешно загружен. Позиций: {prices.Count}");
                return prices;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Не удалось прочитать прайс-лист: {ex.Message}");
                return null;
            }
        }

        private Encoding GetLogEncoding()
        {
            try
            {
                if (!string.IsNullOrEmpty(_config.LogFileEncoding))
                {
                    return Encoding.GetEncoding(_config.LogFileEncoding);
                }
                return Encoding.UTF8;
            }
            catch
            {
                Log(LogLevel.Warn, $"Неизвестная кодировка '{_config.LogFileEncoding}'. Используется UTF-8.");
                return Encoding.UTF8;
            }
        }

        private enum LogLevel { Info, Success, Warn, Error, Debug }

        private void Log(LogLevel level, string message)
        {
            Color color = Color.Gainsboro;
            string prefix = "[INFO]   ";
            switch (level)
            {
                case LogLevel.Success: prefix = "[SUCCESS]"; color = Color.MediumSeaGreen; break;
                case LogLevel.Warn: prefix = "[WARN]   "; color = Color.Gold; break;
                case LogLevel.Error: prefix = "[ERROR]  "; color = Color.Tomato; break;
                case LogLevel.Debug: prefix = "[DEBUG]  "; color = Color.Gray; break;
            }
            if (level == LogLevel.Debug && !_config.DebugMode) return;
            OnLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {prefix} {message}", color);
        }

        private class LogEntry
        {
            public DateTime Timestamp { get; }
            public string Content { get; }
            public bool IsProcessed { get; set; }
            public LogEntry(DateTime ts, string content) { Timestamp = ts; Content = content; }
        }
    }
}