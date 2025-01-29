using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

class Program
{
    private static readonly string BotToken = "7771502471:AAHcLGqq8Q6Mwb92A30A1WTRWALYDYVQMxA";
    private static long ChatId = 0; // Global chatId

    // Global list to store meeting themes
    private static readonly List<string> AvailableThemes = new List<string>();

    // Dictionary to store active polls and their results
    private static readonly Dictionary<string, Dictionary<string, int>> ActivePolls = new();

    static async Task Main(string[] args)
    {
        var botClient = new TelegramBotClient(BotToken);
        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.PollAnswer, UpdateType.MyChatMember }
        };

        botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken: cts.Token
        );

        ScheduleDailyTasks(botClient, cts.Token);

        Console.ReadLine();
        cts.Cancel();
    }

    static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.MyChatMember)
        {
            var myChatMember = update.MyChatMember;
            if (myChatMember.NewChatMember.User.Id == botClient.BotId && myChatMember.NewChatMember.Status == ChatMemberStatus.Administrator)
            {
                ChatId = myChatMember.Chat.Id;
            }
            return;
        }

        if (update.Type != UpdateType.Message || update.Message?.Text == null)
        {
            return;
        }

        var message = update.Message;
        long chatId = message.Chat.Id; // Use local chatId variable
        var command = message.Text.Split(' ')[0];

        switch (command)
        {
            case "/topic":
                var theme = message.Text.Substring(6).Trim();
                if (!string.IsNullOrEmpty(theme))
                {
                    AvailableThemes.Add(theme);
                    await botClient.SendTextMessageAsync(chatId, $"Theme '{theme}' has been added to the list.");
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "Please provide a valid theme after /topic.");
                }
                break;

            case "/start":
                await botClient.SendTextMessageAsync(chatId, "Welcome! Use /topic <theme> to add a theme for the meeting.");
                break;

            case "/meeting":
                await botClient.SendTextMessageAsync(
                    chatId,
                    "The video call for today's meeting starts now! Join via this link: [Meeting Link](http://example.com/meeting)",
                    parseMode: ParseMode.Markdown
                );
                break;

            case "/poll":
                await CreatePoll(botClient, chatId);
                break;

            case "/result":
                await AnnounceMeetingTopic(botClient, chatId);
                break;

            default:
                await botClient.SendTextMessageAsync(chatId, "Unknown command. Please use /topic, /start, /meeting, /poll, or /result.");
                break;
        }

        if (update.Type == UpdateType.PollAnswer)
        {
            await HandlePollAnswer(update.PollAnswer!);
        }
    }

    static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine(exception.ToString());
        return Task.CompletedTask;
    }

    static async Task CreatePoll(ITelegramBotClient botClient, long chatId)
    {
        if (AvailableThemes.Count < 1)
        {
            await botClient.SendTextMessageAsync(chatId, "Not enough themes available for the poll. Please add more themes using /topic <theme>.");
            return;
        }

        // Randomly select 6 themes from the available list
        var random = new Random();
        var selectedThemes = AvailableThemes.OrderBy(x => random.Next()).Take(6).ToList();

        var question = "What topic should we discuss in today's meeting?";
        var options = new List<InputPollOption>();
        foreach (var option in selectedThemes)
        {
            var pollAnswer = new InputPollOption(option);
            options.Add(option);
        }

        var pollMessage = await botClient.SendPoll(
            chatId: chatId,
            question: question,
            options: options,
            isAnonymous: false
        );

        await botClient.PinChatMessageAsync(chatId, pollMessage.MessageId);

        // Track the poll
        ActivePolls[pollMessage.Poll.Id] = selectedThemes.ToDictionary(option => option, _ => 0);
    }

    static Task HandlePollAnswer(PollAnswer pollAnswer)
    {
        if (ActivePolls.TryGetValue(pollAnswer.PollId, out var pollResults))
        {
            foreach (var optionId in pollAnswer.OptionIds)
            {
                var option = pollResults.Keys.ElementAt(optionId);
                pollResults[option]++;
            }
        }

        return Task.CompletedTask;
    }

    static async Task AnnounceMeetingTopic(ITelegramBotClient botClient, long chatId)
    {
        if (!ActivePolls.Any())
        {
            await botClient.SendTextMessageAsync(chatId, "No active poll results available.");
            return;
        }

        var pollId = ActivePolls.Keys.First();
        var pollResults = ActivePolls[pollId];
        var mostVoted = pollResults.OrderByDescending(x => x.Value).First();

        var message = $"The most voted topic is: **{mostVoted.Key}**. See you in the meeting at 8 PM!";
        await botClient.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Markdown);

        AvailableThemes.Remove(mostVoted.Key);

        ActivePolls.Remove(pollId);
    }

    static async Task SendVideoCallReminder(ITelegramBotClient botClient, long chatId)
    {
        var message = "Reminder: The English-speaking meeting starts now! Join the video chat!";
        await botClient.SendTextMessageAsync(chatId, message);
    }

    static void ScheduleDailyTasks(ITelegramBotClient botClient, CancellationToken token)
    {
        // Create poll at 9 AM
        ScheduleTask(10, 22, async () =>
        {
            if (ChatId != 0)
            {
                await CreatePoll(botClient, ChatId);
            }
        }, token);

        // Announce the meeting topic at 5 PM
        ScheduleTask(17, 0, async () =>
        {
            if (ChatId != 0)
            {
                await AnnounceMeetingTopic(botClient, ChatId);
            }
        }, token);

        // Send a video call reminder at 8 PM
    }

    static void ScheduleTask(int hour, int minute, Func<Task> task, CancellationToken token)
    {
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var scheduledTime = DateTime.Today.AddHours(hour).AddMinutes(minute);

                if (now > scheduledTime)
                    scheduledTime = scheduledTime.AddDays(1);

                var delay = scheduledTime - now;
                await Task.Delay(delay, token);

                await task();
            }
        }, token);
    }
}
