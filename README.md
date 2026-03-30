# AionMoneyCounter

**RU** | [EN](#english)

---

## О проекте

AionMoneyCounter — WPF-приложение для отслеживания дропа и подсчёта заработка в игре Aion (приватные серверы 4.x).

Приложение читает файл чата (`Chat.log`) в реальном времени, определяет выпавшие предметы по их ID и считает их стоимость на основе прайс-листа. Также отслеживает кинару с мобов и отображает итог в реальной валюте с учётом комиссии FunPay.

### Возможности

- Подсчёт стоимости дропа в реальном времени (RUB через FunPay)
- Поддержка RU и EN клиентов Aion
- Синхронизация цен с сервером + локальный прайс-лист
- Фильтр по расе (Асмодиане / Элийцы)
- Подсчёт кинары с мобов
- Пауза с пропуском лута во время паузы
- Сохранение и восстановление сессии при запуске
- Слежка за конкретными предметами (watched items)
- OBS-виджет для стриминга (таймер, заработок, дроп предметов)
- Ручная корректировка суммы

### Технологии

- .NET 10, WPF, MVVM (CommunityToolkit.Mvvm)
- Regex-парсинг лог-файла (windows-1251 / UTF-8)
- REST API для получения цен с сервера (FastAPI)
- OBS Browser Source виджет (HTML/JS)

---

## Установка

1. Скачать последний релиз из [Releases](../../releases)
2. Распаковать в любую папку
3. Открыть `config.json` и указать:
   - `LogFilePath` — путь к `Chat.log` вашей игры
   - `FunPayFactionId` — `"Асмодиане"` или `"Элийцы"`
4. Добавить цены предметов в файл `prices_Асмодиане.txt` или `prices_Элийцы.txt` в формате:
   ```
   ID_предмета;цена_в_кинаре
   ```
5. Запустить `AionMoneyCounter.exe`

### Формат прайс-листа

```
141000001;8000
186000237;39700
113501386;790000
```

### OBS виджет

Добавьте `obs_widget.html` как Browser Source в OBS (Local File). Виджет отображает таймер сессии, заработок и список дропа.

---

## Конфигурация

| Параметр | Описание |
|---|---|
| `LogFilePath` | Путь к Chat.log игры |
| `LogFileEncoding` | Кодировка лога (`windows-1251` для RU, `utf-8` для EN) |
| `DirectLootMode` | `true` — считать предметы сразу; `false` — ждать подтверждения из чата |
| `FunPayCommissionPercent` | Комиссия FunPay (по умолчанию 19%) |
| `WithdrawalCommissionPercent` | Комиссия вывода (по умолчанию 3%) |
| `InGameCurrencyRate` | Кинара за 1кк (по умолчанию 1 000 000) |
| `ItemPricesApiUrl` | URL сервера с ценами (опционально) |
| `DebugMode` | Подробный лог парсинга |

---

---

## English

## About

AionMoneyCounter is a WPF application for tracking item drops and calculating earnings in Aion (private servers 4.x).

The app reads the game's chat log (`Chat.log`) in real time, identifies dropped items by their ID, and calculates their value using a price list. It also tracks kinah dropped from mobs and displays the total in real currency accounting for FunPay marketplace commissions.

### Features

- Real-time drop value tracking (RUB via FunPay)
- Supports both RU and EN Aion clients
- Price sync from server + local price list fallback
- Faction filter (Asmodian / Elyos)
- Kinah from mobs tracking
- Pause with loot skip during pause
- Session save & restore on startup
- Watched items tracking
- OBS streaming widget (timer, earnings, item drop counters)
- Manual earnings adjustment

### Tech Stack

- .NET 10, WPF, MVVM (CommunityToolkit.Mvvm)
- Regex-based log parsing (windows-1251 / UTF-8)
- REST API for server price sync (FastAPI backend)
- OBS Browser Source widget (HTML/JS)

---

## Setup

1. Download the latest release from [Releases](../../releases)
2. Extract to any folder
3. Open `config.json` and set:
   - `LogFilePath` — path to your game's `Chat.log`
   - `FunPayFactionId` — `"Асмодиане"` or `"Элийцы"`
4. Add item prices to `prices_Асмодиане.txt` or `prices_Элийцы.txt`:
   ```
   item_id;price_in_kinah
   ```
5. Run `AionMoneyCounter.exe`

### Price List Format

```
141000001;8000
186000237;39700
113501386;790000
```

### OBS Widget

Add `obs_widget.html` as a Browser Source in OBS (Local File). The widget shows the session timer, earnings, and item drop list.

---

## Configuration

| Parameter | Description |
|---|---|
| `LogFilePath` | Path to the game's Chat.log |
| `LogFileEncoding` | Log encoding (`windows-1251` for RU client, `utf-8` for EN) |
| `DirectLootMode` | `true` — count items immediately; `false` — wait for chat confirmation |
| `FunPayCommissionPercent` | FunPay commission (default 19%) |
| `WithdrawalCommissionPercent` | Withdrawal commission (default 3%) |
| `InGameCurrencyRate` | Kinah per 1kk (default 1,000,000) |
| `ItemPricesApiUrl` | Optional server URL for price sync |
| `DebugMode` | Verbose parsing log |

---

## License

MIT
