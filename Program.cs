using Npgsql;
using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Program
{
    public class Client
    {
        public long TgId { get; set; }
        public int ServerId { get; set; }
        public Guid Uuid { get; set; }
        public string ShortId { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public string Status { get; set; } = null!;
    }
    public class Server
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Country { get; set; } = null!;
        public int MaxUsers { get; set; }
        public bool IsActive { get; set; }

        public string PanelUrl { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string Ip { get; set; } = null!;
        public string SNI { get; set; } = null!;
        public bool IsWhiteList { get; set; }
        public int Port { get; set; }
    }
    public class SystemStats
    {
        public long TotalUsers { get; set; }
        public long TotalClients { get; set; }
        public long ActiveClients { get; set; }
        public long ExpiredClients { get; set; }
        public long ActiveServers { get; set; }
        public long TotalServers { get; set; }
        public long PendingPayments { get; set; }
        public long PaidPayments { get; set; }
        public long WithdrawalRequests { get; set; }
        public long PendingWithdrawals { get; set; }
    }
    class Program
    {
        static ITelegramBotClient client = new TelegramBotClient("Token"); // Токен ТГ бота из BotFather
        static string connectionString = "Host=127.0.0.1;Port=5432;Username=DB_UN;Password=DBpass;Database=DBname"; //Данные для подключения к БД
        static Random rnd = new Random();
        static string referralCode = "https://t.me/syabaVPN_bot?start="; // Ссылка  на вашего бота для создания реферальный ссылок
        static string publicKeyVPN = "PublicKey"; // Ваш публичный ключ vpn
        static ConcurrentDictionary<long, string> userStates = new();
        static int MyTgId = 1991980696;
        static async Task Main(string[] args)
        {
            await SkipOldUpdates();
            if (IsMaintenanceWindow())
            {
                await ProcessExpiredSubscriptions(CancellationToken.None);
                await NotifyExpiringSoonSubscriptions(CancellationToken.None);
            }
            var cts = new CancellationTokenSource();
            client.StartReceiving(Update, Error);

            _ = Task.Run(() => ProcessPaidPayments(cts.Token));

            await Task.Delay(Timeout.Infinite, cts.Token);
        }

        // Список ID администраторов
        static List<long> adminIds = new List<long> { 1991980696 };

        //Булевый метод который возвращает айди админов
        static bool IsAdmin(long userId)
        {
            return adminIds.Contains(userId);
        }
        static string commands = "/buy - ✨ Приобрести VPN\n" +
                                  "/promo - 🎟️ Использовать промокод\n" +
                                  "/ref - 🫂 Реферальная ссылка\n" +
                                  "/status - 📊 Моя подписка\n" +
                                  "/changeServer - 🔄 Сменить сервер\n" +
                                  "/howToActivate - 💡 Как активировать\n" +
                                  "/support - 🌚 Тех.Поддержка\n" +
                                  "/news - 🗺️ Основной канал\n" +
                                  "/userAgreement - 📖 Пользовательское Соглашение";

        static ReplyKeyboardMarkup keyboard = new ReplyKeyboardMarkup(new[]
                {
                        new KeyboardButton[] { "✨ Приобрести VPN"},
                        new KeyboardButton[] { "🎟️ Использовать промокод"},
                        new KeyboardButton[]{ "📊 Моя подписка"},
                        new KeyboardButton[]{ "🔄 Сменить сервер"},
                        new KeyboardButton[]{ "🫂 Реферальная ссылка"},
                        new KeyboardButton[]{ "💡 Как активировать", "🗺️ Основной канал" },
                        new KeyboardButton[]{ "🌚 Тех.Поддержка", "📖 Пользовательское Соглашение"},
                    })
        {
            ResizeKeyboard = true
        };
        static ReplyKeyboardMarkup AdminKeyboard = new ReplyKeyboardMarkup(new[]
                {
                        new KeyboardButton[] { "✨ Приобрести VPN"},
                        new KeyboardButton[] { "🎟️ Использовать промокод"},
                        new KeyboardButton[]{ "📊 Моя подписка"},
                        new KeyboardButton[]{ "🔄 Сменить сервер"},
                        new KeyboardButton[]{ "🫂 Реферальная ссылка"},
                        new KeyboardButton[]{ "💡 Как активировать", "🗺️ Основной канал" },
                        new KeyboardButton[]{ "🌚 Тех.Поддержка", "📖 Пользовательское Соглашение"},
                        new KeyboardButton[]{ "🎃 Команды Администратора"},
                    })
        {
            ResizeKeyboard = true
        };

        // Обработка полученных обновлений, которых может быть много :)
        async static Task Update(ITelegramBotClient client, Update update, CancellationToken token)
        {
            var message = update.Message;

            if (update.CallbackQuery != null)
            {
                var cb = update.CallbackQuery;
                var userId = cb.From.Id;

                if (cb.Data!.StartsWith("select_server:"))
                {
                    if (!userStates.TryGetValue(userId, out var st) || st != "SELECT_SERVER")
                    {
                        await client.AnswerCallbackQuery(cb.Id, "❌ Сессия устарела");
                        return;
                    }

                    int serverId = int.Parse(cb.Data.Split(':')[1]);

                    await client.EditMessageText(
                        chatId: cb.Message!.Chat.Id,
                        messageId: cb.Message.MessageId,
                        text: "🔄 Меняю сервер, подождите..."
                    );

                    string result = await ChangeUserServerManual(userId, serverId);

                    await client.SendMessage(
                        cb.Message.Chat.Id,
                        $"👍 Смена прошла успешно!\n\n<code>{result}</code>",
                        parseMode: ParseMode.Html
                    );

                    userStates.TryRemove(cb.From.Id, out _);
                    await client.AnswerCallbackQuery(cb.Id);
                }
                else if (cb.Data!.StartsWith("buy:"))
                {
                    var parts = cb.Data.Split(':');
                    int price = int.Parse(parts[1]);
                    int days = int.Parse(parts[2]);

                    string payLink = await GenerateRoboKassaLink(cb.From.Id, price, days);

                    if (payLink == "ErrorGen")
                    {
                        await client.AnswerCallbackQuery(cb.Id, "❌ У вас уже есть активная ссылка");
                        return;
                    }

                    var payKeyboard = new InlineKeyboardMarkup(
                        InlineKeyboardButton.WithUrl("💳 Оплатить", payLink)
                    );

                    await client.EditMessageText(
                        chatId: cb.Message!.Chat.Id,
                        messageId: cb.Message.MessageId,
                        text:
                            $"✨ Подписка SyabaVPN\n" +
                            $"💰 Стоимость: {price}₽\n" +
                            $"⏳ Срок: {days} дней\n\n" +
                            "После оплаты доступ будет выдан автоматически.",
                        replyMarkup: payKeyboard
                    );

                    await client.AnswerCallbackQuery(cb.Id);
                    return;
                }
                else if (cb.Data == "convert_confirm")
                {
                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();
                    await using var tx = await conn.BeginTransactionAsync();

                    try
                    {
                        // Получаем актуальный баланс (с блокировкой строки)
                        var balCmd = new NpgsqlCommand(
                            "SELECT balance FROM users WHERE tg_id = @tg FOR UPDATE",
                            conn, tx);
                        balCmd.Parameters.AddWithValue("tg", userId);

                        var balanceObj = await balCmd.ExecuteScalarAsync();
                        if (balanceObj == null || Convert.ToInt64(balanceObj) <= 0)
                        {
                            await client.AnswerCallbackQuery(cb.Id, "Баланс изменился или стал нулевым", showAlert: true);
                            await tx.RollbackAsync();
                            return;
                        }

                        long balance = Convert.ToInt64(balanceObj);
                        int days = (int)(balance / 263);  // 2.63 ₽ = 263 копеек за день

                        if (days <= 0)
                        {
                            await client.AnswerCallbackQuery(cb.Id, "Недостаточно средств после пересчёта", showAlert: true);
                            await tx.RollbackAsync();
                            return;
                        }

                        // Списываем весь баланс
                        var deductCmd = new NpgsqlCommand(
                            "UPDATE users SET balance = 0 WHERE tg_id = @tg",
                            conn, tx);
                        deductCmd.Parameters.AddWithValue("tg", userId);
                        await deductCmd.ExecuteNonQueryAsync();

                        // Продлеваем / создаём подписку
                        bool hadSub = await HasActiveSubscription(userId);

                        if (hadSub)
                        {
                            await UpdateSubscription(userId, days);
                        }
                        else
                        {
                            var server = await GetActiveServer();
                            if (server == null)
                            {
                                await tx.RollbackAsync();
                                await client.AnswerCallbackQuery(cb.Id, "Нет доступных серверов", showAlert: true);
                                return;
                            }

                            var sub = await CreateSubscription(userId, server.Id, days);
                            await AddUserVia3XUIApi(server.PanelUrl, server.Username, server.Password, sub.uuid, userId);
                        }

                        await tx.CommitAsync();

                        string msg = hadSub
                            ? $"✅ Успешно конвертировано!\nПодписка продлена на {days} дней.\nБаланс обнулён."
                            : $"✅ Успешно конвертировано!\nАктивирована подписка на {days} дней.\nПроверьте конфиг: /status";

                        await client.EditMessageText(
                            cb.Message!.Chat.Id,
                            cb.Message.MessageId,
                            msg,
                            parseMode: ParseMode.Markdown
                        );

                        await client.AnswerCallbackQuery(cb.Id, "Конвертация выполнена!");

                        // Уведомление админу (опционально)
                        await client.SendMessage(
                            MyTgId,
                            $"💱 Пользователь `{userId}` конвертировал баланс → {days} дней",
                            parseMode: ParseMode.Markdown
                        );
                    }
                    catch (Exception ex)
                    {
                        await tx.RollbackAsync();
                        Console.WriteLine($"Ошибка конвертации для {userId}: {ex.Message}");
                        await client.AnswerCallbackQuery(cb.Id, "Произошла ошибка при конвертации", showAlert: true);
                    }

                    userStates.TryRemove(userId, out _);
                }

                return;
            }

            if (message == null)
                return;
            if (message.From == null)
                return;

            if (message.Chat.Type == ChatType.Group || message.Chat.Type == ChatType.Supergroup)
            {
                return;
            }

            // Проверка на команду /start
            if (message.Text != null && message.Text.StartsWith("/start"))
            {
                var parts = message.Text.Split(' ', 2);
                string? referralCode = parts.Length > 1 ? parts[1] : null;

                await RegisterUser(message.From.Id, message.From.Username, referralCode);

                if (!IsAdmin(message.Chat.Id))
                {
                    await client.SendMessage(message.Chat.Id,
                    "🧭 Офф. канал, тут все новости: https://t.me/syabaVPN");
                    await client.SendMessage(message.Chat.Id,
                    "🔐SyabaVPN \n" + $"Выберите, что вас интересует: \n\n{commands}",
                    replyMarkup: keyboard);
                }
                else
                {
                    await client.SendMessage(message.Chat.Id,
                    "🧭 Офф. канал, тут все новости: https://t.me/syabaVPN");
                    await client.SendMessage(message.Chat.Id,
                    "🔐SyabaVPN \n" + $"✨Выберите, что вас интересует: \n\n{commands}",
                    replyMarkup: AdminKeyboard);
                }

                await TryGiveStartBonus(message.From.Id);
            }
            else
            {
                switch (message.Text)
                {
                    case "/status":
                    case "📊 Моя подписка":
                        {
                            if (IsMaintenanceWindow())
                            {
                                await client.SendMessage(
                                    message.Chat.Id,
                                    "🛠 Сейчас проводится техническое обслуживание.\n" +
                                    "📊 Действие временно заблокировано(до 4.05 по МСК)."
                                );
                                break;
                            }
                            bool hasSub = await HasActiveSubscription(message.From.Id);
                            if (!hasSub)
                            {
                                await client.SendMessage(
                                    message.Chat.Id,
                                    "❌ У вас нет активной подписки.\nИспользуйте /buy для покупки."
                                );
                                break;
                            }

                            var status = await GetSubscriptionStatus(message.From.Id);

                            string? config = await GetUserConfig(message.From.Id);

                            if (config == null)
                            {
                                await client.SendMessage(
                                    message.Chat.Id,
                                    "❌ Ошибка при генерации конфига, обратитесь в тех.поддержку."
                                );
                                break;
                            }
                            await client.SendMessage(message.Chat.Id, $"{status}\n📬Вы также можете поделиться подпиской с другом через команду /share\n\n🔐 Ваш VPN-конфиг:\n\n<code>{config}</code>", parseMode: ParseMode.Html);
                            break;
                        }
                    case "/support":
                    case "🌚 Тех.Поддержка":
                        await client.SendMessage(message.Chat.Id, "👐 Если у вас появились вопросы, обращайтесь к @syabaVPN_support\n🕐 Время работы - в свободное время)");
                        break;
                    case "/buy":
                    case "✨ Приобрести VPN":
                        if (IsMaintenanceWindow())
                        {
                            await client.SendMessage(
                                message.Chat.Id,
                                "🛠 Сейчас проводится техническое обслуживание.\n" +
                                "✨ Действие временно заблокировано(до 4.05 по МСК)."
                            );
                            break;
                        }
                        await client.SendMessage(message.Chat.Id, "На данный момент онлайн оплаты временно не доступны, пожалуйста, обратитесь сюда -> /support или по кнопке Тех.Поддержка");
                        if (!IsAdmin(message.From.Id))
                            break;

                        var tariffKeyboard = new InlineKeyboardMarkup(new[]
    {
    new[]
    {
        InlineKeyboardButton.WithCallbackData("💳 100₽ / 38 дней", "buy:100:38")
    },
    new[]
    {
        InlineKeyboardButton.WithCallbackData("💳 200₽ / 80 дней", "buy:200:80")
    },
    new[]
    {
        InlineKeyboardButton.WithCallbackData("💳 300₽ / 120 дней", "buy:300:120")
    },
    new[]
    {
        InlineKeyboardButton.WithCallbackData("💳 500₽ / 210 дней", "buy:500:210")
    },
    new[]
    {
        InlineKeyboardButton.WithCallbackData("💳 850₽ / 365 дней", "buy:850:365")
    }
});

                        await client.SendMessage(
                            message.Chat.Id,
                            "✨ Выберите тариф:",
                            replyMarkup: tariffKeyboard
                        );
                        break;
                    case "/userAgreement":
                    case "📖 Пользовательское Соглашение":
                        await client.SendMessage(message.Chat.Id, $"💾 Вся информацию тут: https://t.me/syabaVPN_userAgreement/2");
                        break;
                    case "/howToActivate":
                    case "💡 Как активировать":
                        await client.SendMessage(message.Chat.Id, $"💾 Вся информацию тут: https://t.me/syabaVPN_activation/2");
                        break;
                    case "/news":
                    case "🗺️ Основной канал":
                        await client.SendMessage(message.Chat.Id, $"🧭 Переходи сюда, тут все новости: https://t.me/syabaVPN");
                        break;
                    case "/changeServer":
                    case "🔄 Сменить сервер":
                        if (IsMaintenanceWindow())
                        {
                            await client.SendMessage(
                                message.Chat.Id,
                                "🛠 Сейчас проводится техническое обслуживание.\n" +
                                "🔄 Действие временно заблокировано(до 4.05 по МСК)."
                            );
                            break;
                        }

                        if (!await HasActiveSubscription(message.From.Id))
                        {
                            await client.SendMessage(message.Chat.Id,
                                "❌ У вас нет активной подписки.\nИспользуйте /buy для покупки.");
                            break;
                        }

                        var servers = await GetAvailableServers(isWhiteList: false);

                        if (servers.Count == 0)
                        {
                            await client.SendMessage(message.Chat.Id,
                                "❌ Нет доступных серверов.");
                            break;
                        }

                        userStates[message.From.Id] = "SELECT_SERVER";

                        var buttons = servers
                            .Select(s =>
                                new[]
                                {
                InlineKeyboardButton.WithCallbackData(
                    $"{s.Country}",
                    $"select_server:{s.Id}"
                )
                                })
                            .ToArray();

                        await client.SendMessage(
                            message.Chat.Id,
                            "🌍 Выберите сервер:",
                            replyMarkup: new InlineKeyboardMarkup(buttons)
                        );
                        break;
                    case "🎃 Команды Администратора":
                        if (!IsAdmin(message.From.Id))
                            break;

                        await client.SendMessage(message.Chat.Id, "/createpromo - создать промокод\n" +
                            "/mailing - рассылка всем пользователям\n" +
                            "/mailingNoSub - рассылка всем пользователям без подписки\n" +
                            "/mailingToUser - Сообщение пользователю\n" +
                            "/withdrawals - Заявки на вывод\n" +
                            "/approve_withdraw - Принять заявку\n" +
                            "/reject_withdraw - Отклонить заявку\n" +
                            "/userInfo - информация пользователя\n" +
                            "/botStats - информация про бота и пользователей");
                        break;
                    case "/changeDPIserver":
                    case "⚡ Сменить DPI сервер (Только LTE)":
                        if (IsMaintenanceWindow())
                        {
                            await client.SendMessage(
                                message.Chat.Id,
                                "🛠 Сейчас проводится техническое обслуживание.\n" +
                                "🔄 Действие временно заблокировано(до 4.05 по МСК)."
                            );
                            break;
                        }
                        if (!IsAdmin(message.From.Id))
                            break;

                        if (!await HasActiveSubscription(message.From.Id))
                        {
                            await client.SendMessage(message.Chat.Id,
                                "❌ У вас нет активной подписки.\nИспользуйте /buy для покупки.");
                            break;
                        }

                        var serversWhiteList = await GetAvailableServers(isWhiteList: true);

                        if (serversWhiteList.Count == 0)
                        {
                            await client.SendMessage(message.Chat.Id,
                                "❌ Нет доступных серверов.");
                            break;
                        }

                        userStates[message.From.Id] = "SELECT_SERVER";

                        var buttonsWhiteList = serversWhiteList
                            .Select(s =>
                                new[]
                                {
                InlineKeyboardButton.WithCallbackData(
                    $"{s.Country}",
                    $"select_server:{s.Id}"
                )
                                })
                            .ToArray();

                        await client.SendMessage(
                            message.Chat.Id,
                            "🌍 Выберите сервер:",
                            replyMarkup: new InlineKeyboardMarkup(buttonsWhiteList)
                        );
                        break;
                    case "/promo":
                    case "🎟️ Использовать промокод":
                        if (IsMaintenanceWindow())
                        {
                            await client.SendMessage(
                                message.Chat.Id,
                                "🛠 Сейчас проводится техническое обслуживание.\n" +
                                "🎟️ Действие временно заблокировано(до 4.05 по МСК)."
                            );
                            break;
                        }

                        await client.SendMessage(
                            message.Chat.Id,
                            "🎟️ Введите промокод:"
                        );
                        userStates[message.From.Id] = "WAIT_PROMO";
                        break;
                    case "/createpromo":
                        if (!IsAdmin(message.From.Id))
                            break;
                        await client.SendMessage(message.Chat.Id, $"Название промокода и дни. Пример:'PromoName 30'");
                        userStates[message.From.Id] = "CREATE_PROMO";
                        break;
                    case "🫂 Реферальная ссылка":
                    case "/ref":
                        if (IsMaintenanceWindow())
                        {
                            await client.SendMessage(
                                message.Chat.Id,
                                "🛠 Сейчас проводится техническое обслуживание.\n" +
                                "🔄 Действие временно заблокировано(до 4.05 по МСК)."
                            );
                            break;
                        }
                        var code = GenerateReferralCode(message.From.Id);
                        await client.SendMessage(
                            message.Chat.Id,
                            $"👥 Ваша реферальная ссылка:\n" +
                            $"{referralCode}{code}\n\n" +
                            "🎁 За каждого оплатившего друга — +3 дня!\n" +
                            "🎁 20% с суммы оплаты вашего друга\n"
                        );

                        long balance2 = await GetUserBalance(message.From.Id);
                        await client.SendMessage(
                            message.Chat.Id,
                            $"💰 Ваш баланс:\n\n" +
                            $"💸 {balance2 / 100.0:F2} ₽\n\n" +
                            $"🔃 Вы можете либо вывести деньги, либо конвертировать их в дни подписки\n" +
                            $"📤 Минимум для вывода средств: 500 ₽\n" +
                            $" • Для конвертации используйте команду /convert\n" +
                            $" • Для вывода средств используйте команду /withdraw"
                        );
                        break;
                    case "/share":
                        {
                            if (IsMaintenanceWindow())
                            {
                                await client.SendMessage(
                                    message.Chat.Id,
                                    "🛠 Сейчас проводится техническое обслуживание.\n" +
                                    "📊 Действие временно заблокировано(до 4.05 по МСК)."
                                );
                                break;
                            }
                            await client.SendMessage(
                                    message.Chat.Id,
                                    "Данная команда временно не доступна (до 19 марта.)"
                                );
                            if (!IsAdmin(message.From.Id))
                                break;

                            if (!await HasActiveSubscription(message.From.Id))
                            {
                                await client.SendMessage(
                                    message.Chat.Id,
                                    "❌ У вас нет активной подписки."
                                );
                                break;
                            }

                            int daysLeft = await GetRemainingDays(message.From.Id);

                            if (daysLeft <= 10)
                            {
                                await client.SendMessage(
                                    message.Chat.Id,
                                    $"❌ Поделиться можно только если осталось больше 10 дней.\n" +
                                    $"⏳ Сейчас у вас: {daysLeft} дн."
                                );
                                break;
                            }

                            await client.SendMessage(
                                message.Chat.Id,
                                "🤝 Введите Telegram ID пользователя и количество дней\nПример: 1991980696 65\nГде '1991980696' - ID пользователя, '65' - количество дней.\n🫆Узнать ваш ID можно командой /id"
                            );

                            userStates[message.From.Id] = "WAIT_SHARE_TARGET";
                            break;
                        }
                    case "/id":
                        {
                            await client.SendMessage(message.Chat.Id, $"🫆 Ваш ID:\n\n<code>{message.From.Id}</code>", parseMode: ParseMode.Html);
                            break;
                        }
                    case "/mailing":
                        {
                            if (!IsAdmin(message.From.Id))
                                break;
                            await client.SendMessage(message.Chat.Id, $"Введите сообщение для рассылки всем пользователям:", parseMode: ParseMode.Html);
                            userStates[message.From.Id] = "WAIT_MALLING";
                            break;
                        }
                    case "/mailingNoSub":
                        {
                            if (!IsAdmin(message.From.Id))
                                break;
                            await client.SendMessage(message.Chat.Id, $"Введите сообщение для рассылки всем пользователям без подписки:", parseMode: ParseMode.Html);
                            userStates[message.From.Id] = "WAIT_MALLING_NOSUB";
                            break;
                        }
                    case "/mailingToUser":
                        {
                            await client.SendMessage(message.Chat.Id, $"Введите сообщение для пользователя 'id + сообщение':", parseMode: ParseMode.Html);
                            userStates[message.From.Id] = "WAIT_MALLING_TOUSER";
                            break;
                        }
                    case "/withdraw":
                        {
                            if (IsMaintenanceWindow())
                            {
                                await client.SendMessage(
                                    message.Chat.Id,
                                    "🛠 Сейчас проводится техническое обслуживание.\n" +
                                    "📅 Конвертация временно недоступна (до 4:05 по МСК)."
                                );
                                break;
                            }

                            const long MIN_WITHDRAW = 500 * 100; // 500 ₽

                            // 1. Проверяем существующую заявку
                            string? existingStatus = await GetActiveWithdrawalStatus(message.From.Id);
                            if (existingStatus != null)
                            {
                                string statusText = existingStatus switch
                                {
                                    "pending" => "⏳ В обработке",
                                    "approved" => "✅ Одобрена",
                                    _ => existingStatus
                                };

                                await client.SendMessage(
                                    message.Chat.Id,
                                    $"📨 У вас уже есть активная заявка на вывод.\n\n" +
                                    $"📌 Статус: {statusText}"
                                );
                                break;
                            }

                            // 2. Проверяем баланс
                            long balance = await GetUserBalance(message.From.Id);
                            if (balance < MIN_WITHDRAW)
                            {
                                await client.SendMessage(
                                    message.Chat.Id,
                                    $"❌ Минимальная сумма для вывода: 500 ₽\n" +
                                    $"💰 Сейчас: {balance / 100.0:F2} ₽"
                                );
                                break;
                            }

                            // 3. Создаём заявку на ВСЮ сумму (без списания)
                            await using var conn = new NpgsqlConnection(connectionString);
                            await conn.OpenAsync();

                            var insert = new NpgsqlCommand(@"
        INSERT INTO withdrawal_requests (tg_id, amount)
        VALUES (@tg, @amount);
    ", conn);

                            insert.Parameters.AddWithValue("tg", message.From.Id);
                            insert.Parameters.AddWithValue("amount", balance); // ВСЯ сумма

                            await insert.ExecuteNonQueryAsync();

                            // 4. Сообщение пользователю
                            await client.SendMessage(
                                message.Chat.Id,
                                "📨 Заявка на вывод средств принята!\n\n" +
                                $"💰 Сумма вывода: {balance / 100.0:F2} ₽\n\n" +
                                "📌 Для подтверждения и получения выплаты, пожалуйста,\n" +
                                "напишите в техподдержку: /support\n\n" +
                                "💬 В сообщении укажите:\n" +
                                "• ваш Telegram ID (/id)\n" +
                                "• реквизиты для выплаты"
                            );

                            // 5. Уведомление админу
                            await client.SendMessage(
                                MyTgId,
                                $"📤 НОВАЯ ЗАЯВКА НА ВЫВОД\n" +
                                $"👤 Пользователь: `{message.From.Id}`\n" +
                                $"💰 Сумма: {balance / 100.0:F2} ₽\n" +
                                $"📌 Статус: pending",
                                parseMode: ParseMode.Markdown
                            );

                            break;
                        }
                    case "/withdrawals":
                        {
                            if (!IsAdmin(message.From.Id))
                                break;

                            await using var conn = new NpgsqlConnection(connectionString);
                            await conn.OpenAsync();

                            var cmd = new NpgsqlCommand(@"
        SELECT id, tg_id, amount, status, created_at
        FROM withdrawal_requests
        ORDER BY created_at DESC
        LIMIT 10;
    ", conn);

                            await using var reader = await cmd.ExecuteReaderAsync();

                            if (!reader.HasRows)
                            {
                                await client.SendMessage(message.Chat.Id, "📭 Заявок на вывод нет.");
                                break;
                            }

                            var sb = new StringBuilder("📤 Заявки на вывод:\n\n");

                            while (await reader.ReadAsync())
                            {
                                sb.AppendLine(
                                    $"🆔 ID: `{reader.GetInt32(0)}`\n" +
                                    $"👤 User: `{reader.GetInt64(1)}`\n" +
                                    $"💰 {reader.GetInt64(2) / 100.0:F2} ₽\n" +
                                    $"📌 Статус: {reader.GetString(3)}\n" +
                                    $"🕒 {reader.GetDateTime(4):dd.MM.yyyy HH:mm}\n"
                                );
                            }

                            await client.SendMessage(
                                message.Chat.Id,
                                sb.ToString(),
                                parseMode: ParseMode.Markdown
                            );

                            break;
                        }
                    case "/convert":
                        if (IsMaintenanceWindow())
                        {
                            await client.SendMessage(
                                message.Chat.Id,
                                "🛠 Сейчас проводится техническое обслуживание.\n" +
                                "📅 Конвертация временно недоступна (до 4:05 по МСК)."
                            );
                            break;
                        }

                        long balance3 = await GetUserBalance(message.From.Id);
                        if (balance3 <= 0)
                        {
                            await client.SendMessage(
                                message.Chat.Id,
                                "💸 На вашем балансе 0 ₽ — конвертировать нечего."
                            );
                            break;
                        }

                        int daysPossible = await ConvertBalanceToDays(message.From.Id);
                        if (daysPossible <= 0)
                        {
                            await client.SendMessage(
                                message.Chat.Id,
                                $"💰 Ваш баланс: {balance3 / 100.0:F2} ₽\n\n" +
                                "⚠️ Этой суммы недостаточно даже на 1 день подписки.\n" +
                                $"Минимально нужно ~{263 / 100.0:F2} ₽"
                            );
                            break;
                        }

                        // Подтверждение
                        var confirmKeyboard = new InlineKeyboardMarkup(
                            InlineKeyboardButton.WithCallbackData(
                                $"✅ Конвертировать → {daysPossible} дней",
                                "convert_confirm"
                            )
                        );

                        await client.SendMessage(
                            message.Chat.Id,
                            $"💱 Конвертация баланса в дни подписки\n\n" +
                            $"Текущий баланс: {balance3 / 100.0:F2} ₽\n" +
                            $"Курс: 1 день = 2.63 ₽\n" +
                            $"Вы получите: {daysPossible} дней\n\n" +
                            "Подтвердите действие:",
                            parseMode: ParseMode.Markdown,
                            replyMarkup: confirmKeyboard
                        );
                        break;
                    case "/userInfo":
                        {
                            if (!IsAdmin(message.From.Id))
                                break;

                            await client.SendMessage(
                                message.Chat.Id,
                                "🧾 Введите Telegram ID пользователя:"
                            );

                            userStates[message.From.Id] = "WAIT_USER_INFO";
                            break;
                        }
                    case "/botStats":
                        {
                            if (!IsAdmin(message.From.Id))
                                break;

                            var stats = await GetSystemStats();

                            await client.SendMessage(message.Chat.Id,
                                $"📊 Системная статистика\n\n" +
                                $"👤 Пользователи: {stats.TotalUsers}\n" +
                                $"🔐 Клиенты: {stats.ActiveClients} / {stats.TotalClients}\n" +
                                $"🌍 Серверы: {stats.ActiveServers} / {stats.TotalServers}\n" +
                                $"💳 Платежи: {stats.PaidPayments} (ожидают: {stats.PendingPayments})\n" +
                                $"📤 Выводы: {stats.WithdrawalRequests} (ожидают: {stats.PendingWithdrawals})",
                                parseMode: ParseMode.Markdown
                            );
                            break;
                        }
                    case string s when s.StartsWith("/approve_withdraw"):
                        {
                            if (!IsAdmin(message.From.Id))
                                break;

                            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length != 2 || !int.TryParse(parts[1], out int requestId))
                            {
                                await client.SendMessage(message.Chat.Id, "❌ Использование: /approve_withdraw <id>");
                                break;
                            }

                            await using var conn = new NpgsqlConnection(connectionString);
                            await conn.OpenAsync();
                            await using var tx = await conn.BeginTransactionAsync();

                            try
                            {
                                var get = new NpgsqlCommand(@"
            SELECT tg_id, amount, status
            FROM withdrawal_requests
            WHERE id = @id
            FOR UPDATE;
        ", conn, tx);

                                get.Parameters.AddWithValue("id", requestId);

                                await using var reader = await get.ExecuteReaderAsync();
                                if (!await reader.ReadAsync())
                                {
                                    await client.SendMessage(message.Chat.Id, "❌ Заявка не найдена.");
                                    return;
                                }

                                long tgId = reader.GetInt64(0);
                                long amount = reader.GetInt64(1);
                                string status = reader.GetString(2);

                                if (status != "pending")
                                {
                                    await client.SendMessage(message.Chat.Id, $"❌ Заявка уже обработана ({status})");
                                    return;
                                }

                                reader.Close();

                                // списываем баланс
                                var deduct = new NpgsqlCommand(@"
            UPDATE users
            SET balance = balance - @amount
            WHERE tg_id = @tg;
        ", conn, tx);

                                deduct.Parameters.AddWithValue("amount", amount);
                                deduct.Parameters.AddWithValue("tg", tgId);
                                await deduct.ExecuteNonQueryAsync();

                                // обновляем статус
                                var update2 = new NpgsqlCommand(@"
            UPDATE withdrawal_requests
            SET status = 'paid', processed_at = NOW()
            WHERE id = @id;
        ", conn, tx);

                                update2.Parameters.AddWithValue("id", requestId);
                                await update2.ExecuteNonQueryAsync();

                                await tx.CommitAsync();

                                await client.SendMessage(message.Chat.Id, $"✅ Заявка {requestId} одобрена.");
                                await client.SendMessage(
                                    tgId,
                                    $"💸 Ваша заявка на вывод {amount / 100.0:F2} ₽ одобрена и выплачена.\nСпасибо!"
                                );
                            }
                            catch
                            {
                                await tx.RollbackAsync();
                                throw;
                            }

                            break;
                        }
                    case string s when s.StartsWith("/reject_withdraw"):
                        {
                            if (!IsAdmin(message.From.Id))
                                break;

                            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length != 2 || !int.TryParse(parts[1], out int requestId))
                            {
                                await client.SendMessage(message.Chat.Id, "❌ Использование: /reject_withdraw <id>");
                                break;
                            }

                            await using var conn = new NpgsqlConnection(connectionString);
                            await conn.OpenAsync();

                            var cmd = new NpgsqlCommand(@"
        UPDATE withdrawal_requests
        SET status = 'rejected', processed_at = NOW()
        WHERE id = @id
        RETURNING tg_id;
    ", conn);

                            cmd.Parameters.AddWithValue("id", requestId);

                            var tgId = await cmd.ExecuteScalarAsync() as long?;
                            if (tgId == null)
                            {
                                await client.SendMessage(message.Chat.Id, "❌ Заявка не найдена.");
                                break;
                            }

                            await client.SendMessage(message.Chat.Id, $"❌ Заявка {requestId} отклонена.");
                            await client.SendMessage(
                                tgId.Value,
                                "❌ Ваша заявка на вывод отклонена.\nСвяжитесь с техподдержкой для уточнения."
                            );

                            break;
                        }
                    default:
                        if (!userStates.TryGetValue(message.From.Id, out var state))
                        {
                            await client.SendMessage(message.Chat.Id, "Извините, я не понимаю эту команду 🤖\nВведите /help, чтобы посмотреть список доступных команд.");
                            break;
                        }
                        if (message.Text == null)
                            break;

                        switch (state)
                        {
                            case "WAIT_PROMO":
                                if (IsMaintenanceWindow())
                                {
                                    await client.SendMessage(
                                        message.Chat.Id,
                                        "🛠 Сейчас проводится техническое обслуживание.\n" +
                                        "🎟️ Действие временно заблокировано(до 4.05 по МСК)."
                                    );
                                    break;
                                }

                                if (message.Text == null)
                                {
                                    await client.SendMessage(message.Chat.Id, $"Нужно ввести промокод!");
                                    break;
                                }

                                string promoCode = message.Text.Trim().ToUpper();

                                var result = await ApplyPromoCode(message.From.Id, promoCode);

                                await client.SendMessage(message.Chat.Id, result);

                                userStates.TryRemove(message.From.Id, out _);
                                break;
                            case "CREATE_PROMO":
                                if (message.Text == null)
                                {
                                    await client.SendMessage(
                                        message.Chat.Id,
                                        "❌ Неверный формат.\nПример:\n`FREE7 7`",
                                        parseMode: ParseMode.Markdown
                                    );
                                    break;
                                }

                                var parts = message.Text
                                    .Trim()
                                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                                if (parts.Length != 2)
                                {
                                    await client.SendMessage(
                                        message.Chat.Id,
                                        "❌ Неверный формат.\nИспользуйте:\n`ПРОМОКОД ДНИ`\n\nПример:\n`FREE30 30`",
                                        parseMode: ParseMode.Markdown
                                    );
                                    break;
                                }

                                string promoCode2 = parts[0].ToUpper();

                                if (!int.TryParse(parts[1], out int days) || days <= 0)
                                {
                                    await client.SendMessage(
                                        message.Chat.Id,
                                        "❌ Количество дней должно быть положительным числом.\nПример:\n`FREE14 14`",
                                        parseMode: ParseMode.Markdown
                                    );
                                    break;
                                }

                                var result2 = await CreatePromoCode(promoCode2, days);

                                await client.SendMessage(message.Chat.Id, result2);

                                userStates.TryRemove(message.From.Id, out _);
                                break;
                            case "WAIT_SHARE_TARGET":
                                if (message.Text == null)
                                    return;

                                var parts2 = message.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                                if (parts2.Length != 2 || !long.TryParse(parts2[0], out long targetTgId) || !int.TryParse(parts2[1], out int transferDays))
                                {
                                    await client.SendMessage(message.Chat.Id, "❌ Неверный формат. Используйте: <Telegram ID> <количество дней>");
                                    break;
                                }
                                if (transferDays <= 0)
                                {
                                    await client.SendMessage(message.Chat.Id, "❌ Количество дней должно быть больше 0");
                                    break;
                                }
                                if (message.From.Id == targetTgId)
                                {
                                    await client.SendMessage(message.Chat.Id, "❌ Нельзя передать дни самому себе :)");
                                    break;
                                }
                                bool userExists = await UserExists(targetTgId);
                                if (!userExists)
                                {
                                    await client.SendMessage(message.Chat.Id, "❌ Этот пользователь ещё не зарегистрирован.\nПусть он откроет бота и нажмёт /start 🙂");
                                    userStates.TryRemove(message.From.Id, out _);
                                    break;
                                }

                                int daysLeft = await GetRemainingDays(message.From.Id);
                                if (daysLeft < transferDays)
                                {
                                    await client.SendMessage(message.Chat.Id, "❌ У вас недостаточно дней.");
                                    userStates.TryRemove(message.From.Id, out _);
                                    break;
                                }

                                // УМЕНЬШАЕМ подписку отправителя
                                await ReduceSubscriptionDays(message.From.Id, transferDays);

                                // Добавляем подписку получателю
                                if (await HasActiveSubscription(targetTgId))
                                {
                                    await UpdateSubscription(targetTgId, transferDays);
                                }
                                else
                                {
                                    var server = await GetActiveServer();
                                    if (server == null)
                                    {
                                        await client.SendMessage(message.Chat.Id, "❌ Сервер недоступен.");
                                        break;
                                    }

                                    var sub = await CreateSubscription(targetTgId, server.Id, transferDays);
                                    await AddUserVia3XUIApi(server.PanelUrl, server.Username, server.Password, sub.uuid, targetTgId);
                                }

                                await client.SendMessage(
                                    message.Chat.Id,
                                    $"✅ Вы передали {transferDays} дней подписки пользователю `{targetTgId}`",
                                    parseMode: ParseMode.Markdown
                                );

                                await client.SendMessage(
                                    targetTgId,
                                    $"🎁 Вам подарили {transferDays} дней подписки!\nИспользуйте /status"
                                );

                                await client.SendMessage(MyTgId,
                                    $"✅ Пользователь: `{message.From.Id}` передал подписку пользователю `{targetTgId}` на {transferDays} дней.",
                                    parseMode: ParseMode.Markdown
                                );

                                userStates.TryRemove(message.From.Id, out _);
                                break;
                            case "WAIT_MALLING":
                                _ = Task.Run(async () =>
                                {
                                    await foreach (var tgId in GetAllUserTgIdsAsync())
                                    {
                                        await SendWithLimit(async () =>
                                        {
                                            try
                                            {
                                                await client.SendMessage(tgId, message.Text);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"❌ Рассылка не доставлена пользователю {tgId}: {ex.Message}");
                                            }
                                        });
                                    }
                                });

                                userStates.TryRemove(message.From.Id, out _);
                                break;
                            case "WAIT_MALLING_NOSUB":
                                {
                                    _ = Task.Run(async () =>
                                    {
                                        await foreach (var tgId in GetUsersWithoutOrExpiredSubscriptionAsync())
                                        {
                                            await SendWithLimit(async () =>
                                            {
                                                try
                                                {
                                                    await client.SendMessage(tgId, message.Text);
                                                }
                                                catch (Exception ex)
                                                {
                                                    Console.WriteLine($"❌ Рассылка не доставлена пользователю {tgId}: {ex.Message}");
                                                }
                                            });
                                        }
                                    });

                                    userStates.TryRemove(message.From.Id, out _);
                                    break;
                                }
                            case "WAIT_MALLING_TOUSER":
                                {
                                    if (message.Text == null)
                                        break;

                                    // Ожидаемый формат: "<tg_id> <сообщение>"
                                    var parts3 = message.Text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

                                    if (parts3.Length < 2 || !long.TryParse(parts3[0], out long targetTgId2))
                                    {
                                        await client.SendMessage(
                                            message.Chat.Id,
                                            "❌ Неверный формат.\nИспользуйте:\n<id пользователя> <сообщение>\n\nПример:\n1991980696 Привет!"
                                        );
                                        break;
                                    }

                                    string textToSend = parts3[1];

                                    bool exists = await UserExists(targetTgId2);
                                    if (!exists)
                                    {
                                        await client.SendMessage(
                                            message.Chat.Id,
                                            "❌ Пользователь с таким ID не найден в базе."
                                        );
                                        userStates.TryRemove(message.From.Id, out _);
                                        break;
                                    }

                                    try
                                    {
                                        await client.SendMessage(targetTgId2, textToSend + "\nЕсли хотите ответить на данное сообщение, напишите сюда -> @syabaVPN_support");
                                        await client.SendMessage(
                                            message.Chat.Id,
                                            $"✅ Сообщение отправлено пользователю `{targetTgId2}`",
                                            parseMode: ParseMode.Markdown
                                        );
                                    }
                                    catch (Exception ex)
                                    {
                                        await client.SendMessage(
                                            message.Chat.Id,
                                            $"❌ Не удалось отправить сообщение пользователю `{targetTgId2}`\n{ex.Message}",
                                            parseMode: ParseMode.Markdown
                                        );
                                    }

                                    userStates.TryRemove(message.From.Id, out _);
                                    break;
                                }
                            case "WAIT_USER_INFO":
                                {
                                    if (message.Text == null || !long.TryParse(message.Text.Trim(), out long targetTgId3))
                                    {
                                        await client.SendMessage(
                                            message.Chat.Id,
                                            "❌ Неверный ID.\nВведите числовой Telegram ID."
                                        );
                                        break;
                                    }

                                    bool exists = await UserExists(targetTgId3);
                                    if (!exists)
                                    {
                                        await client.SendMessage(
                                            message.Chat.Id,
                                            "❌ Пользователь не найден в базе."
                                        );
                                        userStates.TryRemove(message.From.Id, out _);
                                        break;
                                    }

                                    var clientData = await GetClientByTgId(targetTgId3);

                                    if (clientData == null)
                                    {
                                        await client.SendMessage(
                                            message.Chat.Id,
                                            $"👤 Пользователь `{targetTgId3}` зарегистрирован,\n" +
                                            "❌ Подписки нет.",
                                            parseMode: ParseMode.Markdown
                                        );
                                        userStates.TryRemove(message.From.Id, out _);
                                        break;
                                    }

                                    var server = await GetServerById(clientData.ServerId);

                                    string statusText;
                                    if (clientData.ExpiresAt > DateTime.UtcNow)
                                    {
                                        var remaining = clientData.ExpiresAt - DateTime.UtcNow;
                                        int days2 = (int)Math.Ceiling(remaining.TotalDays);

                                        statusText =
                                            "✅ Подписка активна\n" +
                                            $"⏳ Осталось: {days2} дн.\n" +
                                            $"📅 До: {clientData.ExpiresAt:dd.MM.yyyy HH:mm} UTC";
                                    }
                                    else
                                    {
                                        statusText =
                                            "❌ Подписка истекла\n" +
                                            $"📅 Истекла: {clientData.ExpiresAt:dd.MM.yyyy HH:mm} UTC";
                                    }

                                    string serverText = server != null
                                        ? $"🌍 Сервер: {server.Country} (ID: {server.Id})"
                                        : "⚠️ Сервер не найден";

                                    string safeUserId = targetTgId3.ToString();
                                    string safeStatus = WebUtility.HtmlEncode(statusText);
                                    string safeServer = WebUtility.HtmlEncode(serverText);

                                    string text =
                                        $"🧾 Информация о пользователе <b>{safeUserId}</b>\n\n" +
                                        $"{safeStatus}\n" +
                                        $"{safeServer}";

                                    await client.SendMessage(
                                        message.Chat.Id,
                                        text,
                                        parseMode: ParseMode.Html
                                    );

                                    userStates.TryRemove(message.From.Id, out _);
                                    break;
                                }
                            default:
                                await client.SendMessage(message.Chat.Id, "Извините, я не понимаю эту команду 🤖\nВведите /help, чтобы посмотреть список доступных команд.");
                                break;
                        }
                        break;
                }
            }
        }
        // Обработка ошибок которых не должно быть в проекте :D
        private static Task Error(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken token)
        {
            Console.WriteLine($"Ошибка: {exception.Message}");
            return Task.CompletedTask;
        }
        private static async Task SkipOldUpdates()
        {
            try
            {
                var updates = await client.GetUpdates();
                if (updates.Any())
                {
                    // Устанавливаем offset на следующий после последнего UpdateId
                    var lastUpdateId = updates.Last().Id;
                    await client.GetUpdates(offset: lastUpdateId + 1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при установке offset: {ex.Message}");
            }
        }
        // При старте записывает пользователей в базу
        static async Task RegisterUser(long tgId, string? username, string? referralCode = null)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            long? referrerId = null;

            if (!string.IsNullOrEmpty(referralCode))
            {
                var refCmd = new NpgsqlCommand(
                    "SELECT tg_id FROM users WHERE referral_code = @code",
                    conn
                );
                refCmd.Parameters.AddWithValue("code", referralCode);

                var result = await refCmd.ExecuteScalarAsync();
                if (result is long id && id != tgId)
                    referrerId = id;
            }

            var cmd = new NpgsqlCommand(@"
        INSERT INTO users (tg_id, username, referrer_tg_id, referral_code)
        VALUES (@tg, @u, @ref, @mycode)
        ON CONFLICT (tg_id) DO NOTHING;
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);
            cmd.Parameters.AddWithValue("u", (object?)username ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ref", (object?)referrerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("mycode", GenerateReferralCode(tgId));

            await cmd.ExecuteNonQueryAsync();
        }
        // Существует ли человек в базе
        static async Task<bool> UserExists(long tgId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(
                "SELECT 1 FROM users WHERE tg_id = @tg LIMIT 1;",
                conn
            );
            cmd.Parameters.AddWithValue("tg", tgId);

            var result = await cmd.ExecuteScalarAsync();
            return result != null;
        }
        // Проверяет статус подписки
        static async Task<string> GetSubscriptionStatus(long tgId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        SELECT expires_at
        FROM clients
        WHERE tg_id = @tg AND status = 'active'
        ORDER BY expires_at DESC
        LIMIT 1;
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);

            var result = await cmd.ExecuteScalarAsync();

            if (result == null)
                return "❌ У вас нет активной подписки.\nИспользуйте /buy для покупки.";

            var expires = (DateTime)result;
            var now = DateTime.UtcNow;

            if (expires <= now)
                return "❌ Ваша подписка истекла.\nИспользуйте /buy для продления.";

            var remaining = expires - now;

            int daysLeft = (int)Math.Ceiling(remaining.TotalDays);
            int hoursLeft = remaining.Hours;

            return
                "✅ Ваша подписка активна\n" +
                $"📅 До: {expires:dd.MM.yyyy HH:mm} UTC\n" +
                $"⏳ Осталось: {daysLeft} дн. {hoursLeft} ч.";
        }
        // Выдает ссылку для пользователя
        static async Task<string?> GetUserConfig(long tgId)
        {
            var client = await GetClientByTgId(tgId);
            if (client == null)
                return null;
            var server = await GetServerById(client.ServerId);
            if (server == null)
                return null;

            // ШАБЛОН ССЫЛКИ 
            string config = GenerateVlessLink(client.Uuid, server.Ip, client.ShortId, tgId, server.Country, server.IsWhiteList, server.Port, server.SNI);

            return config;
        }
        // Метод для создания новой подписки
        static async Task<(Guid uuid, string shortID)> CreateSubscription(long tgId, int serverId, int durationDays = 30)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var uuid = Guid.NewGuid();
            string shortID = randomShortId();

            var cmd = new NpgsqlCommand(@"
INSERT INTO clients (tg_id, server_id, uuid, short_id, expires_at, status)
VALUES (@tg, @srv, @uuid, @shortID, NOW() + (@days || ' days')::interval, 'active')
ON CONFLICT (tg_id)
DO UPDATE SET
    server_id = EXCLUDED.server_id,
    uuid = EXCLUDED.uuid,
    short_id = EXCLUDED.short_id,
    expires_at =
        CASE
            WHEN clients.expires_at > NOW()
                THEN clients.expires_at + (@days || ' days')::interval
            ELSE NOW() + (@days || ' days')::interval
        END,
    status = 'active'
RETURNING uuid, short_id;
", conn);

            cmd.Parameters.AddWithValue("tg", tgId);
            cmd.Parameters.AddWithValue("srv", serverId);
            cmd.Parameters.AddWithValue("uuid", uuid);
            cmd.Parameters.AddWithValue("shortID", shortID);
            cmd.Parameters.AddWithValue("days", durationDays);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return (reader.GetGuid(0), reader.GetString(1));

            throw new Exception("Не удалось создать подписку");
        }
        // Метод для продления существующей подписки
        static async Task<Guid> UpdateSubscription(long tgId, int durationDays = 30)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        UPDATE clients
        SET
            expires_at = CASE
                WHEN expires_at > NOW()
                    THEN expires_at + (@days || ' days')::interval
                ELSE NOW() + (@days || ' days')::interval
            END,
            status = 'active'
        WHERE tg_id = @tg
        RETURNING uuid;
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);
            cmd.Parameters.AddWithValue("days", durationDays);

            var result = await cmd.ExecuteScalarAsync();
            if (result is Guid g)
                return g;

            throw new Exception("Не удалось продлить подписку");
        }
        // Метод замены UUID
        static async Task<Guid> ReplaceUuidANDserverId(long tgId, int serverId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var newUuid = Guid.NewGuid();

            var cmd = new NpgsqlCommand(@"
        UPDATE clients
        SET
            uuid = @newUuid,
            server_id = @serverId
        WHERE tg_id = @tg
        RETURNING uuid;
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);
            cmd.Parameters.AddWithValue("serverId", serverId);
            cmd.Parameters.AddWithValue("newUuid", newUuid);

            var result = await cmd.ExecuteScalarAsync();

            if (result is Guid g)
                return g;

            throw new Exception("Не удалось заменить UUID и server_id для пользователя");
        }
        // Получить данные о рандомном сервере
        static async Task<Server?> GetActiveServer(int? currentServerId = null, bool isWhiteList = false)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var sql = @"
    SELECT
        s.id,
        s.name,
        s.country,
        s.max_users,
        s.is_active,
        s.panelurl,
        s.username,
        s.password,
        s.ip,
        s.sni
    FROM servers s
    LEFT JOIN clients c
        ON c.server_id = s.id
        AND c.status = 'active'
        AND c.expires_at > NOW()
    WHERE s.is_active = TRUE
        AND s.white_list = @isWhiteList
        AND s.in_random = TRUE
    ";

            if (currentServerId.HasValue)
                sql += " AND s.id <> @current ";

            sql += @"
    GROUP BY s.id
    HAVING COUNT(c.id) < s.max_users
    ORDER BY RANDOM()
    LIMIT 1;
    ";

            await using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("isWhiteList", isWhiteList);

            if (currentServerId.HasValue)
                cmd.Parameters.AddWithValue("current", currentServerId.Value);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return MapServer(reader);

            // fallback — остаёмся на текущем сервере
            if (currentServerId.HasValue)
                return await GetServerByIdForBalancing(currentServerId.Value);

            return null;
        }
        static async Task<Server?> GetServerByIdForBalancing(int serverId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        SELECT
            id,
            name,
            country,
            max_users,
            is_active,
            panelurl,
            username,
            password,
            ip,
            sni
        FROM servers
        WHERE id = @id
        AND is_active = TRUE
        LIMIT 1;
        ", conn);

            cmd.Parameters.AddWithValue("id", serverId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return MapServer(reader);
        }
        static Server MapServer(NpgsqlDataReader reader)
        {
            return new Server
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Country = reader.GetString(2),
                MaxUsers = reader.GetInt32(3),
                IsActive = reader.GetBoolean(4),
                PanelUrl = reader.GetString(5),
                Username = reader.GetString(6),
                Password = reader.GetString(7),
                Ip = reader.GetFieldValue<IPAddress>(8).ToString(),
                SNI = reader.GetString(9)
            };
        }
        // Собирает данные в ссылку
        static string GenerateVlessLink(Guid uuid, string serverIp, string shortID, long tgID, string serverName, bool isWhiteList, int port, string sni = "www.cloudflare.com")
        {
            if (!isWhiteList)
            {
                return $"vless://{uuid}@{serverIp}:{port}" +
                   "?encryption=none" +
                   "&security=reality" +
                   "&type=tcp" +
                   "&flow=xtls-rprx-vision" +
                   $"&sni={sni}" +
                   "&fp=chrome" +
                   $"&pbk={publicKeyVPN}}" +
                   $"&sid={shortID}" +
                   $"#{serverName}_SyabaVPN";
            }
            else
            {
                return $"vless://{uuid}@{serverIp}:{port}" +
                   "?encryption=none" +
                   "&security=reality" +
                   "&type=tcp" +
                   "&flow=xtls-rprx-vision" +
                   $"&sni={sni}" +
                   "&fp=random" +
                   $"&pbk={publicKeyVPN}" +
                   $"&sid={shortID}" +
                   $"#{serverName}_DPI_SyabaVPN";
            }
        }
        static string randomShortId()
        {
            string[] ids = { "6268b651c7", "c179580c", "a12a7af4", "5efe294800b119", "805d00eb45475085", "be6f6f", "46ebf05c2f11", "7a588dedc5", "45f0" };
            return ids[rnd.Next(ids.Length)];
        }
        // Проверка есть ли активная подписка
        static async Task<bool> HasActiveSubscription(long tgId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        SELECT 1
        FROM clients
        WHERE tg_id = @tg
          AND status = 'active'
          AND expires_at > NOW()
        LIMIT 1;
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);

            var result = await cmd.ExecuteScalarAsync();

            return result != null;
        }
        // Генерация оплаты
        static string merchantLogin = "Логин кассы"; // ← сюда
        static string merchantPassword1 = "Пароль от кассы 1"; // ← сюда
        static bool isTestMode = true;
        static async Task<string> GenerateRoboKassaLink(long tgId, int amountRub, int days)
        {
            // 1. Удаляем старые pending-платежи (как было)
            await CleanupExpiredPendingPayments();

            // 2. Проверяем, нет ли уже активной pending-ссылки
            string? existingOrderId = await GetPendingPaymentOrderId(tgId);
            if (!string.IsNullOrEmpty(existingOrderId))
                return "ErrorGen";  // уже есть ссылка

            // 3. Создаём / получаем orderId (можно использовать int, но ваш GUID тоже подойдёт)
            string orderId = await SaveOrUpdatePayment(tgId, amountRub, days);  // возвращает строку-GUID

            // Для Robokassa InvId лучше сделать числовым, но можно и строку (Robokassa примет)
            // Если хотите чисто числовой — создайте автоинкремент в таблице payments

            string outSum = amountRub.ToString("F2", CultureInfo.InvariantCulture); // 100.00
            string invId = orderId;                                                // или ваш числовой ID
            string description = $"Подписка SyabaVPN на {days} дней (tg:{tgId})";

            // Подпись (SignatureValue)
            // Базовый вариант без доп. параметров:
            // MD5(MerchantLogin:OutSum:InvId:Пароль1)
            string signatureStr = $"{merchantLogin}:{outSum}:{invId}:{merchantPassword1}";
            string signature = CreateMD5(signatureStr).ToLower();

            // Если используете кодировку windows-1251 (часто нужно для кириллицы в desc)
            // signatureStr = $"{merchantLogin}:{outSum}:{invId}:{merchantPassword1}:shpuser={tgId}:Encoding=windows-1251"

            var url = "https://auth.robokassa.ru/Merchant/Index.aspx?";

            var parameters = new Dictionary<string, string>
            {
                { "MerchantLogin",    merchantLogin },
                { "OutSum",           outSum },
                { "InvId",            invId },
                { "Desc",             description },
                { "SignatureValue",   signature },
                { "Culture",          "ru" },
                { "Encoding",         "utf-8" },           // или windows-1251
                { "IsTest",        isTestMode ? "1" : "0" },   // раскомментировать при тесте
            };

            string query = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

            return url + query;
        }
        // Сохранение - обновление данных оплаты в бд
        static async Task<string> SaveOrUpdatePayment(long tgId, int amount, int days)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            string newOrderId = Guid.NewGuid().ToString("N");

            var cmd = new NpgsqlCommand(@"
        INSERT INTO payments (tg_id, amount, days, order_id, status)
        VALUES (@tg, @amount, @days, @order, 'pending')
        ON CONFLICT (tg_id)
        WHERE status = 'pending'
        DO UPDATE SET
            amount = EXCLUDED.amount,
            days = EXCLUDED.days,
            order_id = EXCLUDED.order_id,
            created_at = NOW()
        RETURNING order_id;
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);
            cmd.Parameters.AddWithValue("amount", amount);
            cmd.Parameters.AddWithValue("days", days);
            cmd.Parameters.AddWithValue("order", newOrderId);

            var result = await cmd.ExecuteScalarAsync();

            if (result is not string orderId)
                throw new Exception("Не удалось создать или обновить платёж");

            return orderId;
        }
        // Получает у кого ожидание в оплате
        static async Task<string?> GetPendingPaymentOrderId(long tgId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        SELECT order_id FROM payments 
        WHERE tg_id = @tg AND status = 'pending'
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);

            var result = await cmd.ExecuteScalarAsync();
            return result as string;
        }
        // Очистка всех у кого ожидание оплаты
        static async Task CleanupExpiredPendingPayments()
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        DELETE FROM payments 
        WHERE status = 'pending' 
        AND created_at < NOW() - INTERVAL '1 hour'
    ", conn);

            await cmd.ExecuteNonQueryAsync();
        }
        static string CreateMD5(string input)
        {
            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }
        // Процесс после оплаты
        static async Task ProcessPaidPayments(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync(ct);

                    await using var tx = await conn.BeginTransactionAsync(ct);

                    var cmd = new NpgsqlCommand(@"
                SELECT id, tg_id, amount, days
                FROM payments
                WHERE status = 'paid'
                FOR UPDATE SKIP LOCKED
            ", conn, tx);

                    var payments = new List<(long id, long tgId, int amount, int days)>();

                    await using (var reader = await cmd.ExecuteReaderAsync(ct))
                    {
                        while (await reader.ReadAsync(ct))
                        {
                            payments.Add((
                                reader.GetInt64(0),
                                reader.GetInt64(1),
                                reader.GetInt32(2),
                                reader.GetInt32(3)
                            ));
                        }
                    } // reader.DisposeAsync() тут автоматически

                    foreach (var p in payments)
                    {
                        try
                        {
                            var server = await GetActiveServer();
                            if (server == null)
                                continue;

                            bool hasSubscription = await HasActiveSubscription(p.tgId);

                            if (!hasSubscription)
                            {
                                var createdSub = await CreateSubscription(p.tgId, server.Id, durationDays: p.days);

                                await AddUserVia3XUIApi(server.PanelUrl, server.Username, server.Password, createdSub.uuid, p.tgId);

                                var vless = GenerateVlessLink(createdSub.uuid, server.Ip, createdSub.shortID, p.tgId, server.Country, server.IsWhiteList, server.Port, server.SNI);

                                await client.SendMessage(
                                    p.tgId,
                                    $"🎉 Оплата прошла успешно!\n\n" +
                                    $"⏳ Срок: {p.days} дней\n" +
                                    $"/howToActivate - 💡 Как активировать\n" +
                                    $"🌍 Местонахождение - {server.Country}\n" +
                                    $"🔐 Ваш VPN:\n\n<code>{vless}</code>", parseMode: ParseMode.Html
                                );
                            }
                            else
                            {
                                await UpdateSubscription(p.tgId, p.days);
                                await client.SendMessage(
                                    p.tgId,
                                    $"🎉 Оплата прошла успешно!\n" +
                                    $"⏳ Срок действия увеличен на: {p.days} дней\n" +
                                    "Подробнее тут -> /status"
                                );
                            }

                            await RewardReferrerIfNeeded(p.tgId, p.amount);

                            await client.SendMessage(MyTgId,
                                $"✅ Пользователь: `{p.tgId}` приобрел подписку на {p.days} дней за {p.amount} рублей.",
                                parseMode: ParseMode.Markdown
                            );

                            var updateCmd = new NpgsqlCommand(
                                "UPDATE payments SET status = 'processed' WHERE id = @id",
                                conn, tx);

                            updateCmd.Parameters.AddWithValue("id", p.id);
                            await updateCmd.ExecuteNonQueryAsync(ct);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка обработки платежа {p.id}: {ex.Message}");
                        }
                    }

                    await tx.CommitAsync(ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка payment worker: " + ex.Message);
                }

                await Task.Delay(5000, ct);
            }
        }
        // Добавление пользователя 3X-UI
        static async Task<bool> AddUserVia3XUIApi(string panelUrl, string username, string password, Guid uuid, long tgId)
        {
            string inboundRemark = "inbound-reality";
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                AllowAutoRedirect = true
            };

            if (panelUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                Console.WriteLine("⚠️ HTTPS: игнорируем сертификат");
            }

            using var client = new HttpClient(handler);
            client.BaseAddress = new Uri(panelUrl);  // Префикс до /panel/, с слешем на конце!

            try
            {
                // 1. ЛОГИН НА /login
                var loginResponse = await client.PostAsJsonAsync("login/", new { username, password });

                if (!loginResponse.IsSuccessStatusCode)
                {
                    var loginError = await loginResponse.Content.ReadAsStringAsync();
                    await Program.client.SendMessage(tgId, "Ошибка логина, попробуйте снова!");
                    return false;
                }
                Console.WriteLine("✅ Логин успешен");

                // 2. Список инбаундов
                var listResponse = await client.GetAsync("panel/api/inbounds/list");  // <-- С panel/api/inbounds
                if (!listResponse.IsSuccessStatusCode)
                {
                    var listError = await listResponse.Content.ReadAsStringAsync();
                    await Program.client.SendMessage(tgId, "Ошибка, попробуйте снова!");
                    return false;
                }

                var listJson = await listResponse.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(listJson);
                var inbounds = doc.RootElement.GetProperty("obj");

                int? inboundId = null;
                foreach (var inbound in inbounds.EnumerateArray())
                {
                    var remark = inbound.GetProperty("remark").GetString();
                    if (remark == inboundRemark)
                    {
                        inboundId = inbound.GetProperty("id").GetInt32();
                        Console.WriteLine($"✅ Найден инбаунд '{inboundRemark}' с ID: {inboundId}");
                        break;
                    }
                }

                if (inboundId == null)
                {
                    Console.WriteLine($"❌ Инбаунд '{inboundRemark}' не найден. Доступные:");
                    foreach (var inbound in inbounds.EnumerateArray())
                    {
                        Console.WriteLine($"- ID: {inbound.GetProperty("id").GetInt32()}, Remark: {inbound.GetProperty("remark").GetString()}");
                    }
                    return false;
                }

                // 3. Добавление пользователя
                var clientObj = new
                {
                    id = inboundId,
                    settings = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        clients = new[]
                        {
                    new
                    {
                        email = $"#user_{tgId}_SyabaVPN.com",
                        flow = "xtls-rprx-vision",
                        id = uuid.ToString(),
                        enable = true,
                        level = 0,
                        limitIp = 1
                    }
                }
                    })
                };

                var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(clientObj), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("panel/api/inbounds/addClient", content);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"✅ Пользователь {uuid} успешно добавлен!");
                    return true;
                }

                var error = await response.Content.ReadAsStringAsync();
                await Program.client.SendMessage(tgId, $"❌ Ошибка добавления: {response.StatusCode} — {error}");
                return false;
            }
            catch (Exception ex)
            {
                await Program.client.SendMessage(tgId, $"❌ Исключение: {ex.Message}");
                if (ex.InnerException != null) Console.WriteLine($"Inner: {ex.InnerException.Message}");
                return false;
            }
        }
        // Удаление пользователя из 3X-UI
        static async Task<bool> DeleteUserVia3XUIApi(string panelUrl, string username, string password, Guid uuid, long tgID)
        {
            string inboundRemark = "inbound-reality";
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                AllowAutoRedirect = true
            };
            if (panelUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                Console.WriteLine("⚠️ HTTPS: игнорируем сертификат");
            }
            using var client = new HttpClient(handler);
            client.BaseAddress = new Uri(panelUrl); // Префикс до /panel/, с слешем на конце!

            try
            {
                // 1. ЛОГИН НА /login
                var loginResponse = await client.PostAsJsonAsync("login/", new { username, password });
                if (!loginResponse.IsSuccessStatusCode)
                {
                    var loginError = await loginResponse.Content.ReadAsStringAsync();
                    await Program.client.SendMessage(tgID, "Ошибка логина, попробуйте снова!");
                    return false;
                }
                Console.WriteLine("✅ Логин успешен");

                // 2. Список инбаундов
                var listResponse = await client.GetAsync("panel/api/inbounds/list");
                if (!listResponse.IsSuccessStatusCode)
                {
                    var listError = await listResponse.Content.ReadAsStringAsync();
                    await Program.client.SendMessage(tgID, "Ошибка, попробуйте снова!");
                    return false;
                }

                var listJson = await listResponse.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(listJson);
                var inbounds = doc.RootElement.GetProperty("obj");

                int? inboundId = null;
                foreach (var inbound in inbounds.EnumerateArray())
                {
                    var remark = inbound.GetProperty("remark").GetString();
                    if (remark == inboundRemark)
                    {
                        inboundId = inbound.GetProperty("id").GetInt32();
                        Console.WriteLine($"✅ Найден инбаунд '{inboundRemark}' с ID: {inboundId}");
                        break;
                    }
                }

                if (inboundId == null)
                {
                    Console.WriteLine($"❌ Инбаунд '{inboundRemark}' не найден. Доступные:");
                    foreach (var inbound in inbounds.EnumerateArray())
                    {
                        Console.WriteLine($"- ID: {inbound.GetProperty("id").GetInt32()}, Remark: {inbound.GetProperty("remark").GetString()}");
                    }
                    return false;
                }

                // 3. Удаление пользователя
                string deleteUrl = $"panel/api/inbounds/{inboundId}/delClient/{uuid}";

                var response = await client.PostAsync(deleteUrl, null);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"✅ Пользователь {uuid} успешно удалён!");
                    return true;
                }

                var error = await response.Content.ReadAsStringAsync();

                await Program.client.SendMessage(tgID, $"❌ Ошибка удаления: {response.StatusCode} — {error}");
                return false;
            }
            catch (Exception ex)
            {
                await Program.client.SendMessage(tgID, $"❌ Исключение: {ex.Message}");
                if (ex.InnerException != null) Console.WriteLine($"Inner: {ex.InnerException.Message}");
                return false;
            }
        }
        // Замена конфига на рандомный сервер
        static async Task<string> ChangeUserUUID3XUIApi(long tgId, bool isWhiteList = false)
        {
            bool hasSubscription = await HasActiveSubscription(tgId);
            if (!hasSubscription)
                return "У пользователя нет подписки";

            var clientTG = await GetClientByTgId(tgId);
            if (clientTG == null) return "Пользователь не найден";
            var currentServer = await GetServerById(clientTG.ServerId);
            Server? newServer;
            if (isWhiteList)
                newServer = await GetActiveServer(clientTG.ServerId, isWhiteList: true);
            else
                newServer = await GetActiveServer(clientTG.ServerId);

            if (newServer == null || currentServer == null) return "Новый сервер или старый сервер не найдены";
            await client.SendMessage(clientTG.TgId, $"🌍 Нашел место в {newServer.Country}\n🚀 Собираю конфиг...");

            await DeleteUserVia3XUIApi(currentServer.PanelUrl, currentServer.Username, currentServer.Password, clientTG.Uuid, tgId);
            Guid newUuid = await ReplaceUuidANDserverId(tgId, newServer.Id);
            await AddUserVia3XUIApi(newServer.PanelUrl, newServer.Username, newServer.Password, newUuid, tgId);
            string vlessLink = GenerateVlessLink(newUuid, newServer.Ip, clientTG.ShortId, tgId, newServer.Country, newServer.IsWhiteList, newServer.Port, newServer.SNI);
            return vlessLink;
        }
        // Получить пользователя по его Id
        static async Task<Client?> GetClientByTgId(long tgId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        SELECT tg_id, server_id, uuid, short_id, expires_at, status
        FROM clients
        WHERE tg_id = @tg
        LIMIT 1;
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new Client
            {
                TgId = reader.GetInt64(0),
                ServerId = reader.GetInt32(1),
                Uuid = reader.GetGuid(2),
                ShortId = reader.GetString(3),
                ExpiresAt = reader.GetDateTime(4),
                Status = reader.GetString(5)
            };
        }
        // Получить сервер по его Id
        static async Task<Server?> GetServerById(int serverId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        SELECT
            id,
            name,
            country,
            max_users,
            is_active,
            panelurl,
            username,
            password,
            ip,
            sni,
            white_list,
            port
        FROM servers
        WHERE id = @id
        LIMIT 1;
        ", conn);

            cmd.Parameters.AddWithValue("id", serverId);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new Server
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Country = reader.GetString(2),
                MaxUsers = reader.GetInt32(3),
                IsActive = reader.GetBoolean(4),

                PanelUrl = reader.GetString(5),
                Username = reader.GetString(6),
                Password = reader.GetString(7),
                Ip = reader.GetFieldValue<System.Net.IPAddress>(8).ToString(),
                SNI = reader.GetString(9),
                IsWhiteList = reader.GetBoolean(10),
                Port = reader.GetInt32(11)
            };
        }
        // Планировщик запуска проверки на истечение срока подписки
        static async Task CheckExpiredSubscriptions(CancellationToken ct)
        {
            // 1. ПРОВЕРКА СРАЗУ ПРИ СТАРТЕ
            try
            {
                await ProcessExpiredSubscriptions(ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка стартовой проверки подписок: {ex.Message}");
            }

            // 2. ДАЛЬШЕ — ПО РАСПИСАНИЮ
            while (!ct.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var nextRun = now.Date.AddHours(8); // 08:00 UTC
                if (now >= nextRun)
                    nextRun = nextRun.AddDays(1);

                var delay = nextRun - now;

                try
                {
                    await Task.Delay(delay, ct);
                    await ProcessExpiredSubscriptions(ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка плановой проверки подписок: {ex.Message}");
                }
            }
        }
        // Проверка на истечение срока
        static async Task ProcessExpiredSubscriptions(CancellationToken ct)
        {
            List<(long TgId, Guid Uuid, int ServerId)> expiredClients;

            await using (var conn = new NpgsqlConnection(connectionString))
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

                var cmd = new NpgsqlCommand(@"
            UPDATE clients
            SET status = 'expiring'
            WHERE status = 'active'
              AND expires_at <= NOW()
            RETURNING tg_id, uuid, server_id;
        ", conn, tx);

                expiredClients = new();

                await using (var reader = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await reader.ReadAsync(ct))
                    {
                        expiredClients.Add((
                            reader.GetInt64(0),
                            reader.GetGuid(1),
                            reader.GetInt32(2)
                        ));
                    }
                }

                await tx.CommitAsync(ct);
            }
            foreach (var c in expiredClients)
            {
                try
                {
                    var server = await GetServerById(c.ServerId);
                    if (server != null)
                    {
                        await DeleteUserVia3XUIApi(
                            server.PanelUrl,
                            server.Username,
                            server.Password,
                            c.Uuid,
                            c.TgId
                        );
                    }

                    await MarkClientExpired(c.TgId, c.Uuid);

                    await SendWithLimit(async () =>
                    {
                        try
                        {
                            await client.SendMessage(c.TgId, $"⏳ Ваша подписка SyabaVPN истекла.\n\n✨ Хотите продлить? Используйте /buy.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка {ex}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка обработки tgId={c.TgId}: {ex}");
                }
            }
        }
        // Отметка что срок истек
        static async Task MarkClientExpired(long tgId, Guid uuid)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        UPDATE clients
        SET status = 'expired'
        WHERE tg_id = @tg
          AND uuid = @uuid;
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);
            cmd.Parameters.AddWithValue("uuid", uuid);

            await cmd.ExecuteNonQueryAsync();
        }
        static async Task<string> ApplyPromoCode(long tgId, string code)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        SELECT id, bonus_days, max_uses, used_count
        FROM promocodes
        WHERE code = @code
          AND is_active = TRUE
          AND (expires_at IS NULL OR expires_at > NOW())
    ", conn);

            cmd.Parameters.AddWithValue("code", code);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return "❌ Промокод недействителен или истёк.";

            int promoId = reader.GetInt32(0);
            int bonusDays = reader.GetInt32(1);
            int? maxUses = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            int usedCount = reader.GetInt32(3);

            if (maxUses != null && usedCount >= maxUses)
                return "❌ Лимит использования промокода исчерпан.";

            reader.Close();

            // Проверка: использовал ли пользователь
            var checkCmd = new NpgsqlCommand(@"
        SELECT 1 FROM promo_usages
        WHERE promocode_id = @pid AND tg_id = @tg
    ", conn);

            checkCmd.Parameters.AddWithValue("pid", promoId);
            checkCmd.Parameters.AddWithValue("tg", tgId);

            if (await checkCmd.ExecuteScalarAsync() != null)
                return "⚠️ Вы уже использовали этот промокод.";

            await using var tx = await conn.BeginTransactionAsync();

            // применяем дни
            var server = await GetActiveServer();
            if (server == null)
                return "Ошибка - Сервер не найден.";
            bool hasSubscription = await HasActiveSubscription(tgId);
            if (!hasSubscription)
            {
                var createdSub = await CreateSubscription(tgId, server.Id, bonusDays);
                await AddUserVia3XUIApi(server.PanelUrl, server.Username, server.Password, createdSub.uuid, tgId);
            }
            else
            {
                await UpdateSubscription(tgId, bonusDays);
            }

            // фиксируем использование
            var insertUsage = new NpgsqlCommand(@"
        INSERT INTO promo_usages (promocode_id, tg_id)
        VALUES (@pid, @tg)
    ", conn, tx);

            insertUsage.Parameters.AddWithValue("pid", promoId);
            insertUsage.Parameters.AddWithValue("tg", tgId);
            await insertUsage.ExecuteNonQueryAsync();

            var updatePromo = new NpgsqlCommand(@"
        UPDATE promocodes
        SET used_count = used_count + 1
        WHERE id = @pid
    ", conn, tx);

            updatePromo.Parameters.AddWithValue("pid", promoId);
            await updatePromo.ExecuteNonQueryAsync();

            await tx.CommitAsync();

            await client.SendMessage(MyTgId,
                                $"✅ Пользователь: `{tgId}` активировал промокод {code} на {bonusDays} дней.",
                                parseMode: ParseMode.Markdown
                            );

            if (!hasSubscription)
            {
                return $"🎉 Промокод применён!\n⏳ Активирована подписка на {bonusDays} дней.\nИспользуйте /config для получения конфигурации.";
            }
            else
            {
                return $"🎉 Промокод применён!\n⏳ Подписка продлена на {bonusDays} дней.";
            }
        }
        static async Task<string> CreatePromoCode(string code, int bonusDays, int? maxUses = 1, DateTime? expiresAt = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "❌ Код промокода не может быть пустым.";

            if (bonusDays <= 0)
                return "❌ Количество дней должно быть больше 0.";

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // Проверяем, существует ли уже такой код
            var checkCmd = new NpgsqlCommand(@"
        SELECT 1 FROM promocodes WHERE code = @code
    ", conn);

            checkCmd.Parameters.AddWithValue("code", code);

            if (await checkCmd.ExecuteScalarAsync() != null)
                return "⚠️ Такой промокод уже существует.";

            var insertCmd = new NpgsqlCommand(@"
        INSERT INTO promocodes
            (code, bonus_days, max_uses, expires_at, is_active)
        VALUES
            (@code, @days, @maxUses, @expiresAt, TRUE)
    ", conn);

            insertCmd.Parameters.AddWithValue("code", code);
            insertCmd.Parameters.AddWithValue("days", bonusDays);
            insertCmd.Parameters.AddWithValue("maxUses", (object?)maxUses ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);

            await insertCmd.ExecuteNonQueryAsync();

            return $"✅ Промокод **{code}** успешно создан.\n🎁 Бонус: {bonusDays} дней.";
        }
        // Время перезагрузки сервера
        static bool IsMaintenanceWindow()
        {
            var now = DateTime.Now;
            var start = new TimeSpan(3, 50, 0);
            var end = new TimeSpan(4, 05, 0);

            var time = now.TimeOfDay;
            return time >= start && time <= end;
        }
        // Генерация реферального кода
        static string GenerateReferralCode(long tgId)
        {
            return tgId.ToString();
        }
        // Начисление за реферальную ссылку
        static async Task RewardReferrerIfNeeded(long invitedTgId, int money)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // уже награждали?
            var check = new NpgsqlCommand(@"
        SELECT 1 FROM referral_rewards
        WHERE invited_tg_id = @invited
    ", conn);

            check.Parameters.AddWithValue("invited", invitedTgId);
            if (await check.ExecuteScalarAsync() != null)
                return;

            // кто пригласил
            var getRef = new NpgsqlCommand(@"
        SELECT referrer_tg_id FROM users
        WHERE tg_id = @tg
    ", conn);

            getRef.Parameters.AddWithValue("tg", invitedTgId);
            var referrerId = await getRef.ExecuteScalarAsync() as long?;

            if (referrerId == null)
                return;

            int bonusDays = 3;

            await UpdateSubscription(referrerId.Value, bonusDays);

            var insert = new NpgsqlCommand(@"
        INSERT INTO referral_rewards (referrer_tg_id, invited_tg_id, reward_days)
        VALUES (@r, @i, @d)
    ", conn);

            insert.Parameters.AddWithValue("r", referrerId.Value);
            insert.Parameters.AddWithValue("i", invitedTgId);
            insert.Parameters.AddWithValue("d", bonusDays);

            await insert.ExecuteNonQueryAsync();
            await RewardReferrerMoney(invitedTgId, money, bonusDays);
        }
        // Список доступных серверов
        static async Task<List<Server>> GetAvailableServers(bool isWhiteList = false)
        {
            var servers = new List<Server>();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        SELECT
            s.id,
            s.name,
            s.country,
            s.max_users,
            s.is_active,
            s.panelurl,
            s.username,
            s.password,
            s.ip,
            s.sni
        FROM servers s
        LEFT JOIN clients c
            ON c.server_id = s.id
            AND c.status = 'active'
            AND c.expires_at > NOW()
        WHERE s.is_active = TRUE
          AND s.white_list = @wl
        GROUP BY s.id
        HAVING COUNT(c.id) < s.max_users
        ORDER BY s.country;
    ", conn);

            cmd.Parameters.AddWithValue("wl", isWhiteList);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                servers.Add(MapServer(reader));

            return servers;
        }
        // Смена сервера выборочно
        static async Task<string> ChangeUserServerManual(long tgId, int newServerId)
        {
            if (userStates.TryGetValue(tgId, out var state) && state == "CHANGING")
            {
                await client.SendMessage(tgId, "🔄 Уже идет смена, подождите.");
                return "";
            }
            userStates[tgId] = "CHANGING";

            var clientTG = await GetClientByTgId(tgId);
            if (clientTG == null)
                return "❌ Пользователь не найден";

            var oldServer = await GetServerById(clientTG.ServerId);
            var newServer = await GetServerById(newServerId);

            if (oldServer == null || newServer == null)
                return "❌ Сервер не найден";

            await DeleteUserVia3XUIApi(
                oldServer.PanelUrl,
                oldServer.Username,
                oldServer.Password,
                clientTG.Uuid,
                tgId
            );

            Guid newUuid = await ReplaceUuidANDserverId(tgId, newServer.Id);

            await AddUserVia3XUIApi(
                newServer.PanelUrl,
                newServer.Username,
                newServer.Password,
                newUuid,
                tgId
            );

            return GenerateVlessLink(
                newUuid,
                newServer.Ip,
                clientTG.ShortId,
                tgId,
                newServer.Country,
                newServer.IsWhiteList,
                newServer.Port, newServer.SNI
            );
        }
        // Получить кол-во оставшихся дней
        static async Task<int> GetRemainingDays(long tgId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        SELECT expires_at
        FROM clients
        WHERE tg_id = @tg
          AND status = 'active'
          AND expires_at > NOW()
        ORDER BY expires_at DESC
        LIMIT 1;
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);

            var result = await cmd.ExecuteScalarAsync();
            if (result == null)
                return 0;

            var expires = (DateTime)result;
            var remaining = expires - DateTime.UtcNow;

            return (int)Math.Floor(remaining.TotalDays);
        }
        // Уменьшение дней подписки
        static async Task ReduceSubscriptionDays(long tgId, int days)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        UPDATE clients
        SET expires_at = expires_at - (@days || ' days')::interval
        WHERE tg_id = @tg
          AND status = 'active'
          AND expires_at > NOW();
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);
            cmd.Parameters.AddWithValue("days", days);

            await cmd.ExecuteNonQueryAsync();
        }
        // Получени ID всех пользователей
        static async IAsyncEnumerable<long> GetAllUserTgIdsAsync()
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(
                "SELECT tg_id FROM users",
                conn
            );

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                yield return reader.GetInt64(0);
            }
        }
        // Получение ID пользоваталей без подписки и с истекшей подпиской
        static async IAsyncEnumerable<long> GetUsersWithoutOrExpiredSubscriptionAsync()
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
SELECT u.tg_id
FROM users u
WHERE NOT EXISTS (
    SELECT 1
    FROM clients c
    WHERE
        c.tg_id = u.tg_id
        AND c.status = 'active'
        AND c.expires_at > @now
);
", conn);

            cmd.Parameters.AddWithValue("now", DateTime.UtcNow);

            await using var reader = await cmd.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess
            );

            while (await reader.ReadAsync())
            {
                yield return reader.GetInt64(0);
            }
        }
        // Ограничитить рассылку в 15 сообщений
        static readonly SemaphoreSlim _rateLimiter =
    new SemaphoreSlim(15, 15);
        static async Task SendWithLimit(Func<Task> send)
        {
            await _rateLimiter.WaitAsync();

            try
            {
                await send();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка рассылки: {ex.Message}");
            }
            finally
            {
                _ = Task.Delay(1000)
                    .ContinueWith(_ => _rateLimiter.Release());
            }
        }
        // Предупреждение о истечении подписки
        static async Task NotifyExpiringSoonSubscriptions(CancellationToken ct)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var cmd = new NpgsqlCommand(@"
        SELECT tg_id, expires_at
        FROM clients
        WHERE status = 'active'
          AND expires_at > NOW()
          AND expires_at <= NOW() + INTERVAL '3 days';
    ", conn);

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                long tgId = reader.GetInt64(0);
                DateTime expiresAt = reader.GetDateTime(1);

                int daysLeft = (int)Math.Ceiling((expiresAt - DateTime.UtcNow).TotalDays);

                await SendWithLimit(async () =>
                {
                    try
                    {
                        await client.SendMessage(
                            tgId,
                            $"⚠️ Ваша подписка SyabaVPN скоро закончится!\n\n" +
                            $"⏳ Осталось: {daysLeft} дн.\n\n" +
                            $"✨ Чтобы не потерять доступ, продлите подписку заранее: /buy"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Не удалось отправить уведомление {tgId}: {ex.Message}");
                    }
                });
            }
        }
        // Начисление денег за реф.систему
        static async Task RewardReferrerMoney(long invitedTgId, int paymentAmountRub, int bonusDays)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // кто пригласил
            var cmd = new NpgsqlCommand(@"
        SELECT referrer_tg_id
        FROM users
        WHERE tg_id = @tg
    ", conn);

            cmd.Parameters.AddWithValue("tg", invitedTgId);

            var referrerId = await cmd.ExecuteScalarAsync() as long?;
            if (referrerId == null)
                return;

            long reward = paymentAmountRub * 20L * 100 / 100;
            // 20% → в копейках (amount * 20%)

            var update = new NpgsqlCommand(@"
        UPDATE users
        SET balance = balance + @reward
        WHERE tg_id = @ref;
    ", conn);

            update.Parameters.AddWithValue("reward", reward);
            update.Parameters.AddWithValue("ref", referrerId.Value);

            await update.ExecuteNonQueryAsync();

            await client.SendMessage(
                referrerId.Value,
                $"💸 Реферальное начисление!\n\n" +
                $"👤 Ваш друг оплатил подписку\n" +
                $"🎁 Вам начислено {bonusDays} бонусных дней.\n" +
                $"🎁 Вам начислено: {reward / 100.0:F2} ₽\n\n" +
                $"📊 Подробнее командой /ref"
            );
        }
        // Получить баланс пользователя
        static async Task<long> GetUserBalance(long tgId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(
                "SELECT balance FROM users WHERE tg_id = @tg",
                conn
            );
            cmd.Parameters.AddWithValue("tg", tgId);

            var result = await cmd.ExecuteScalarAsync();
            return result == null ? 0 : (long)result;
        }
        // Получить статус активной заявки
        static async Task<string?> GetActiveWithdrawalStatus(long tgId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        SELECT status
        FROM withdrawal_requests
        WHERE tg_id = @tg
          AND status IN ('pending', 'approved')
        ORDER BY created_at DESC
        LIMIT 1;
    ", conn);

            cmd.Parameters.AddWithValue("tg", tgId);

            var result = await cmd.ExecuteScalarAsync();
            return result as string; // null — заявок нет
        }
        // Конвертация баланса в дни
        static async Task<int> ConvertBalanceToDays(long tgId)
        {
            const long PRICE_PER_DAY = 263; // копейки

            long balance = await GetUserBalance(tgId);

            if (balance < PRICE_PER_DAY)
                return 0;

            return (int)(balance / PRICE_PER_DAY);
        }
        static async Task<SystemStats> GetSystemStats()
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var cmd = new NpgsqlCommand(@"
        SELECT
            (SELECT COUNT(*) FROM users)                                          AS total_users,

            (SELECT COUNT(*) FROM clients)                                        AS total_clients,
            (SELECT COUNT(*) FROM clients 
                WHERE status = 'active' AND expires_at > NOW())                  AS active_clients,
            (SELECT COUNT(*) FROM clients 
                WHERE expires_at <= NOW())                                       AS expired_clients,

            (SELECT COUNT(*) FROM servers WHERE is_active = TRUE)                AS active_servers,
            (SELECT COUNT(*) FROM servers)                                       AS total_servers,

            (SELECT COUNT(*) FROM payments WHERE status = 'pending')             AS pending_payments,
            (SELECT COUNT(*) FROM payments WHERE status = 'paid')                AS paid_payments,

            (SELECT COUNT(*) FROM withdrawal_requests)                           AS withdrawal_requests,
            (SELECT COUNT(*) FROM withdrawal_requests 
                WHERE status = 'pending')                                        AS pending_withdrawals;
    ", conn);

            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();

            return new SystemStats
            {
                TotalUsers = reader.GetInt64(0),
                TotalClients = reader.GetInt64(1),
                ActiveClients = reader.GetInt64(2),
                ExpiredClients = reader.GetInt64(3),
                ActiveServers = reader.GetInt64(4),
                TotalServers = reader.GetInt64(5),
                PendingPayments = reader.GetInt64(6),
                PaidPayments = reader.GetInt64(7),
                WithdrawalRequests = reader.GetInt64(8),
                PendingWithdrawals = reader.GetInt64(9)
            };
        }
        // Применяем дни Рамадана
        static async Task ApplyPromoUntilMarch19(long tgId)
        {
            DateTime promoEnd = new DateTime(2026, 3, 19, 23, 59, 59, DateTimeKind.Utc);
            int days = (promoEnd - DateTime.UtcNow).Days;

            if (days <= 0)
                return; // акция закончилась

            bool hasSub = await HasActiveSubscription(tgId);

            if (hasSub)
            {
                // Продлеваем существующую подписку
                await UpdateSubscription(tgId, days);

                await client.SendMessage(
            tgId,
            $"🌙 В честь месяца Рамадан!\n\n" +
            $"❤️ Ваша подписка продлена на {days} дня(ей).\n" +
            $"Спасибо, что вы с нами ❤️"
        );
            }
            else
            {
                var server = await GetActiveServer();
                if (server == null)
                    return;

                var sub = await CreateSubscription(tgId, server.Id, days);
                await AddUserVia3XUIApi(server.PanelUrl, server.Username, server.Password, sub.uuid, tgId);

                await client.SendMessage(
            tgId,
            $"🌙 В честь месяца Рамадан!\n\n" +
            $"❤️ Вам активирована бесплатная подписка на {days} дня(ей).\n" +
            $"Приятного использования 🚀\n" +
            $"/status - чтобы посмотреть"
        );
            }

            await client.SendMessage(MyTgId,
                                $"✅ Пользователь: `{tgId}` забрал бесплатные дни ❤️",
                                parseMode: ParseMode.Markdown
                            );
        }
        // Попытка выдать стартовый бонус
        static async Task<bool> TryGiveStartBonus(long tgId)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // Проверяем, выдавался ли бонус
            var checkCmd = new NpgsqlCommand(@"
        SELECT given_start_bonus
        FROM users
        WHERE tg_id = @tg
    ", conn);

            checkCmd.Parameters.AddWithValue("tg", tgId);

            var result = await checkCmd.ExecuteScalarAsync();

            if (result is bool given && given)
                return false; // уже выдавали

            // Выдаём бонус
            await ApplyPromoUntilMarch19(tgId);

            // Ставим флаг
            var updateCmd = new NpgsqlCommand(@"
        UPDATE users
        SET given_start_bonus = TRUE
        WHERE tg_id = @tg
    ", conn);

            updateCmd.Parameters.AddWithValue("tg", tgId);
            await updateCmd.ExecuteNonQueryAsync();

            return true;
        }
    }
}