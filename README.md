# SyabaVPN Bot

Telegram-бот на C# (.NET) для продажи и автоматической выдачи VPN-подписок. Бот сам создаёт клиентов на серверах через API панели **3x-ui**, генерирует конфиги **VLESS + Reality**, принимает оплату через **Robokassa** и ведёт учёт в **PostgreSQL**.

---

## Возможности

### Для пользователей
- Покупка подписки через Robokassa (тарифы от 100 ₽ / 38 дней до 850 ₽ / 365 дней)
- Автоматическая выдача VLESS-ссылки сразу после оплаты — без участия администратора
- Просмотр статуса подписки и срока её окончания
- Смена сервера (страны) в один клик с переносом клиента между панелями
- Промокоды на бонусные дни
- Реферальная программа: приглашённый друг приносит 3 дня подписки, а с его оплат начисляется 20 % на баланс
- Вывод реферального баланса (минимум 500 ₽) или конвертация баланса в дни подписки (курс 2.63 ₽ / день)
- Уведомления о скором окончании подписки и об её истечении

### Для администраторов
| Команда | Назначение |
|---|---|
| `/createpromo` | Создать промокод (формат: `PromoName 30`) |
| `/mailing` | Рассылка всем пользователям |
| `/mailingNoSub` | Рассылка пользователям без активной подписки |
| `/mailingToUser` | Личное сообщение пользователю по его Telegram ID |
| `/withdrawals` | Список заявок на вывод средств |
| `/approve_withdraw`, `/reject_withdraw` | Одобрить / отклонить заявку |
| `/userInfo` | Информация о пользователе |
| `/botStats` | Статистика по пользователям, серверам и платежам |

### Фоновые процессы
- `ProcessPaidPayments` — постоянно опрашивает таблицу платежей и активирует подписки по факту оплаты
- `ProcessExpiredSubscriptions` — удаляет клиентов с истёкшей подпиской из панелей 3x-ui
- `NotifyExpiringSoonSubscriptions` — предупреждает пользователей о скором окончании
- `IsMaintenanceWindow` — технологическое окно 03:50–04:05, на это время блокируются покупки и смена сервера

---

## Стек

- .NET / C#
- [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) — long polling
- [Npgsql](https://www.npgsql.org/) — PostgreSQL
- 3x-ui HTTP API (`/login`, `/panel/api/inbounds/list`, `/panel/api/inbounds/addClient`)
- Robokassa (подпись MD5)

---

## Установка

### 1. Требования
- .NET SDK 8.0+
- PostgreSQL 13+
- Один или несколько серверов с установленной панелью 3x-ui и настроенным inbound'ом VLESS + Reality
- Бот, созданный через [@BotFather](https://t.me/BotFather)
- Магазин, зарегистрированный в Robokassa

### 2. Зависимости

```bash
dotnet add package Telegram.Bot
dotnet add package Npgsql
```

### 3. Настройка

Заполните значения в начале `Program.cs`:

```csharp
static ITelegramBotClient client = new TelegramBotClient("Token");           // токен от BotFather
static string connectionString = "Host=127.0.0.1;Port=5432;Username=DB_UN;Password=DBpass;Database=DBname";
static string referralCode = "https://t.me/your_bot?start=";                 // ссылка на вашего бота
static int MyTgId = 000000000;                                               // ваш Telegram ID
static List<long> adminIds = new List<long> { 000000000 };                   // ID администраторов
```

И параметры кассы (примерно строка 1713):

```csharp
static string merchantLogin = "Логин кассы";
static string merchantPassword1 = "Пароль от кассы 1";
```

В методе `GenerateVlessLink` подставьте публичный ключ Reality вашего сервера, а в `randomShortId` — список ваших shortId из настроек inbound'а:

```csharp
"&pbk=YOUR_REALITY_PUBLIC_KEY"
```

### 4. Запуск

```bash
dotnet run
```

Для боевого режима рекомендуется systemd-сервис с `Restart=always`.

---

## Схема базы данных

Бот ожидает следующие таблицы:

| Таблица | Назначение | Ключевые поля |
|---|---|---|
| `users` | Пользователи бота и реферальные связи | `tg_id`, `username`, `referrer_tg_id`, `referral_code`, `balance` |
| `clients` | Активные подписки | `tg_id` (unique), `server_id`, `uuid`, `short_id`, `expires_at`, `status` |
| `servers` | Пул VPN-серверов | `id`, `name`, `country`, `max_users`, `is_active`, `panelurl`, `username`, `password`, `ip`, `sni`, `white_list`, `in_random`, `port` |
| `payments` | Счета Robokassa | `tg_id`, `amount`, `days`, `order_id`, `status` |
| `promocodes` | Промокоды | `code`, `bonus_days`, `max_uses`, `expires_at`, `is_active` |
| `promo_usages` | Факты активации промокодов | `tg_id`, `code` |
| `referral_rewards` | Начисленные реферальные бонусы | `referrer_tg_id`, `invited_tg_id`, `reward_days` |
| `withdrawal_requests` | Заявки на вывод средств | `tg_id`, `amount`, `status` |

Балансы и суммы платежей хранятся **в копейках**.

Учётные данные панелей 3x-ui хранятся в таблице `servers`, а не в коде — бот получает их оттуда при каждом обращении к API.

---

## Логика выдачи конфига

1. Пользователь выбирает тариф → создаётся запись в `payments` со статусом `pending` и формируется ссылка на оплату Robokassa с подписью `MD5(MerchantLogin:OutSum:InvId:Пароль1)`.
2. Фоновый воркер видит смену статуса на `paid`.
3. Выбирается наименее загруженный активный сервер (учитывается `max_users`).
4. Через API 3x-ui создаётся клиент с новым `uuid` и случайным `shortId`.
5. Пользователю отправляется собранная ссылка вида:
   ```
   vless://<uuid>@<ip>:<port>?encryption=none&security=reality&type=tcp&flow=xtls-rprx-vision&sni=<sni>&fp=chrome&pbk=<public_key>&sid=<short_id>#<country>
   ```
6. По истечении срока клиент автоматически удаляется из панели.

---

## Ограничения

- Одна активная подписка на один Telegram ID (ограничение `ON CONFLICT (tg_id)`).
- Конфигурация задаётся константами в коде, а не файлом настроек.
- Состояния диалогов хранятся в оперативной памяти (`ConcurrentDictionary`) и теряются при перезапуске.
- Весь код размещён в одном файле `Program.cs`.

---

## Лицензия

Вы можете свободно использовать, копировать, изменять и распространять этот проект, в том числе в личных и коммерческих целях.
Я не против, если вы будете использовать этот код в своих проектах или адаптировать его под собственные задачи.
