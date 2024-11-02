using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static System.Runtime.InteropServices.JavaScript.JSType;

class Program
{
    public class Quest
    {
        public required string Text { get; set; }
        public required string Status { get; set; }
        public required string DateEnd { get; set; }
        public required string Owner { get; set; }
    }

    private static readonly TelegramBotClient botClient = new TelegramBotClient("7753727897:AAFASJzE7A2piFQEz4DTvIi96Q0ghcouB0c");

    private static readonly Dictionary<long, string> userStates = new Dictionary<long, string>();
    private static readonly Dictionary<string, List<long>> groups = new Dictionary<string, List<long>>();
    private static readonly Dictionary<string, string> groupCodes = new Dictionary<string, string>();
    private static readonly Dictionary<string, List<Quest>> questDictionary = new Dictionary<string, List<Quest>>();


    static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        await SetBotCommandsAsync();
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = { } 
        };
        botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken: cts.Token
        );

        Console.WriteLine("Бот запущен. Нажмите Enter для выхода...");
        Console.ReadLine();

        cts.Cancel();
    }

    static async Task SetBotCommandsAsync()
    {
        var commands = new[]
        {
            new BotCommand { Command = "start", Description = "Запустить бота" },
            new BotCommand { Command = "cmenu", Description = "Меню управления командами" },
            new BotCommand { Command = "task", Description = "Задания" },
            new BotCommand { Command = "leave", Description = "Покинуть команду" }
        };

        await botClient.SetMyCommandsAsync(commands);
    }

    static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {

        if (update.Type == UpdateType.Message && update.Message!.Type == MessageType.Text)
        {
            var chatId = update.Message.Chat.Id;
            var messageText = update.Message.Text;


            if (userStates.TryGetValue(chatId, out var state))
            {
                switch (state)
                {
                    case "setname":
                        {
                            foreach (var group in groups)
                            {
                                if (group.Value.Contains(chatId))
                                {
                                    _ = botClient.SendTextMessageAsync(chatId, $"Вы уже состоите в группе");
                                    return;
                                }
                            }
                            CreateGroup(chatId, messageText);
                            userStates.Remove(chatId);
                            return;
                        }
                    case "invite_cmd":
                        {
                            InviteMe(chatId, messageText);
                            userStates.Remove(chatId);
                            return;
                        }
                    case "members":
                        {
                            Members(chatId);
                            userStates.Remove(chatId);
                            return;
                        }
                    case "option1":
                        {
                            AddTask(chatId, messageText);
                            userStates.Remove(chatId);
                            return;
                        }
                }
            }

            string responseText = messageText.ToLower() switch
            {
                "/start" => "Привет! Я ваш бот. Введите /cmenu, чтобы увидеть меню.",
                "/cmenu" => "Выберите опцию:",
                "/create" => "Меню создания команды",
                _ => $"Мне не понятно: {messageText}"
            };

            IReplyMarkup replyMarkup = messageText.ToLower() switch
            {
                "/task" => new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Создать задачу", "option1"),
                        InlineKeyboardButton.WithCallbackData("Список задач", "option2"),
                    }
                }),
                "/cmenu" => new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("\U0001F6A7  Создать команду", "setname"),
                        InlineKeyboardButton.WithCallbackData("\U0001F6D7  Вступить в команду", "invite_cmd"),
                        InlineKeyboardButton.WithCallbackData("\U0001F6D7  Список членов команды", "members")
                    }
                }),
                _ => new ReplyKeyboardRemove()
            };

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: responseText,
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken
            );
        }
        // Обработка нажатий на кнопки Inline Keyboard
        else if (update.Type == UpdateType.CallbackQuery)
        {
            var callbackQuery = update.CallbackQuery;
            var chatId = callbackQuery.Message.Chat.Id;
            var callbackData = callbackQuery.Data;

            Console.WriteLine($"Получен колбэк: {callbackData}");

            if (callbackData.StartsWith("task_"))
            {
                int taskIndex = int.Parse(callbackData.Substring(5));
                string userGroups = groups.FirstOrDefault(g => g.Value.Contains(chatId)).Key;

                if (userGroups != null && questDictionary.ContainsKey(userGroups) && taskIndex < questDictionary[userGroups].Count)
                {
                    var task = questDictionary[userGroups][taskIndex];
                    await botClient.SendTextMessageAsync(chatId, $"Задача: {task.Text}\nСтатус: {task.Status}\nДата создания: {task.DateEnd}\nСоздатель: {task.Owner}");
                    var inlineKeyboard = new InlineKeyboardMarkup(new[]
                   {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("Изменить статус", $"change_status_{taskIndex}"),
                            InlineKeyboardButton.WithCallbackData("Удалить задачу", $"delete_task_{taskIndex}"),
                            InlineKeyboardButton.WithCallbackData("Закрыть", $"close")
                        }
                    });

                    await botClient.SendTextMessageAsync(chatId, "Выберите действие:", replyMarkup: inlineKeyboard);
                }
            }
            else if (callbackData.StartsWith("change_status_"))
            {
                int taskIndex = int.Parse(callbackData.Substring(14));
                userStates[chatId] = $"change_status_{taskIndex}";
                ChangeTaskStatus(chatId);

            }
            else if (callbackData.StartsWith("delete_task_"))
            {
                int taskIndex = int.Parse(callbackData.Substring(12));
                string userGroups = groups.FirstOrDefault(g => g.Value.Contains(chatId)).Key;

                if (userGroups != null && questDictionary.ContainsKey(userGroups) && taskIndex < questDictionary[userGroups].Count)
                {
                    questDictionary[userGroups].RemoveAt(taskIndex);
                    await botClient.SendTextMessageAsync(chatId, "Задача удалена.");
                }
            }

            string responseText = callbackData switch
            {
                "setname" => "Введите название команды: ",
                "invite_cmd" => "Введите код приглашения в команду: ",
                "members" => "Список членов команды",
                "option1" => "Текст задачи:",
                "option2" => "Выбери из списка",
                "close" => "Действие отменено",
                _ => "!"
            };

            switch(callbackData)
            {
                case "setname": { userStates[chatId] = "setname"; break; }
                case "invite_cmd": { userStates[chatId] = "invite_cmd"; break; }
                case "members": { Members(chatId); break; }
                case "option1": { userStates[chatId] = "option1"; break; }
                case "option2": { _ = ShowTasks(chatId); break; }
                    
            }


            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: responseText,
                cancellationToken: cancellationToken
            );
            

            await botClient.AnswerCallbackQueryAsync(
                callbackQueryId: callbackQuery.Id,
                cancellationToken: cancellationToken
            );
        }
    }
// ==================================
    static void ChangeTaskStatus(long chatId)
    {
        string state = userStates[chatId];
        int taskIndex = int.Parse(state.Substring(14));
        string userGroups = groups.FirstOrDefault(g => g.Value.Contains(chatId)).Key;

        if (userGroups != null && questDictionary.ContainsKey(userGroups) && taskIndex < questDictionary[userGroups].Count)
        {
            if (questDictionary[userGroups][taskIndex].Status == "Открыто") { questDictionary[userGroups][taskIndex].Status = "Закрыто"; }
            else { questDictionary[userGroups][taskIndex].Status = "Открыто"; }
            botClient.SendTextMessageAsync(chatId, $"Статус задачи изменен!");
        }
        return;
    }
    static void AddTask(long chatId, string TextTask)
    {
        string userGroups = groups.FirstOrDefault(g => g.Value.Contains(chatId)).Key;
        if (userGroups == null)
        {
            botClient.SendTextMessageAsync(chatId, "Вы не состоите ни в одной группе.");
        }
        else
        {
            if (groups.ContainsKey(userGroups))
            {
                Quest newQuest = new Quest
                {
                    Text = TextTask,
                    Status = "Открыто", 
                    DateEnd = DateTime.Now.AddDays(7).ToString("dd-MM-yyyy"),
                    Owner = chatId.ToString()
                };
                if (!questDictionary.ContainsKey(userGroups))
                {
                    questDictionary[userGroups] = new List<Quest>();
                }
                questDictionary[userGroups].Add(newQuest);
                botClient.SendTextMessageAsync(chatId, $"Задача '{TextTask}' успешно создана.");
                return;
            }
        }
    }
    static async Task ShowTasks(long chatId)
    {
        string userGroups = groups.FirstOrDefault(g => g.Value.Contains(chatId)).Key;
        if (userGroups == null)
        {
            await botClient.SendTextMessageAsync(chatId, "Вы не состоите ни в одной из групп!");
            return;
        }

        if (!questDictionary.ContainsKey(userGroups) || questDictionary[userGroups].Count == 0)
        {
            await botClient.SendTextMessageAsync(chatId, "У вас нет задач.");
            return;
        }

        var inlineKeyboard = new InlineKeyboardMarkup(questDictionary[userGroups].Select((quest, index) =>
        {
            var button = InlineKeyboardButton.WithCallbackData($"Задача {index + 1}: {quest.Text}", $"task_{index}");
            return new[] { button };
        }));

        await botClient.SendTextMessageAsync(chatId, "Ваши задачи:", replyMarkup: inlineKeyboard);
    }
    static void Members(long chatId)
    {
        var userGroups = groups.Where(g => g.Value.Contains(chatId)).Select(g => g.Key).ToList();

        if (userGroups.Count == 0)
        {
            botClient.SendTextMessageAsync(chatId, "Вы не состоите ни в одной группе.");
            return;
        }

        foreach (var groupName in userGroups)
        {
            var members = groups[groupName];
            var memberList = string.Join("\n", members.Select(id => $"Участник: {id}"));
            botClient.SendTextMessageAsync(chatId, $"Участники группы {groupName}:\n{memberList}");
        }
    }
    static void InviteMe(long chatId, string groupCode)
    {
        var group = groupCodes.FirstOrDefault(x => x.Value == groupCode).Key;
        if (group == null)
        {
            botClient.SendTextMessageAsync(chatId, $"Группа с кодом '{groupCode}' не найдена.");
            return;
        }

        if (groups[group].Contains(chatId))        
        {
            botClient.SendTextMessageAsync(chatId, $"Вы уже состоите в группе '{group}'");
            return;
        }

        groups[group].Add(chatId);
        {
            botClient.SendTextMessageAsync(chatId, $"Вы присоединились к группе '{group}'.");
            return;
        }
    }
    static void CreateGroup(long chatId, string groupName)
    {
        if (groups.ContainsKey(groupName))
        {
            botClient.SendTextMessageAsync(chatId, $"Группа с названием '{groupName}' уже существует.");
            return;
        }
        var groupCode = GenerateGroupCode();
        groups[groupName] = new List<long> { chatId };
        groupCodes[groupName] = groupCode;
        botClient.SendTextMessageAsync(chatId, $"Группа '{groupName}' создана. Код для присоединения: {groupCode}");
    }

    static string GenerateGroupCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 4)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    //===================================================================
    static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var ErrorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(ErrorMessage);
        return Task.CompletedTask;
    }
}