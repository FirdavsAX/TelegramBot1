using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

class Program
{
    private static readonly string BotToken = "7771502471:AAHcLGqq8Q6Mwb92A30A1WTRWALYDYVQMxA";
    private static long ChatId = 0; // Global chatId

    // Global list to store meeting topics
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
        ChatId = chatId;
        switch (command)
        {
            case "/topic@FirdavsManageBot":
                var topic = message.Text.Substring(6).Trim();
                if (!string.IsNullOrEmpty(topic))
                {
                    AvailableThemes.Add(topic);
                    await botClient.SendMessage(chatId, $"Theme '{topic}' has been added to the list.");
                }
                else
                {
                    await botClient.SendMessage(chatId, "Please provide a valid topic after /topic.");
                }
                break;

            case "/start@FirdavsManageBot":
                await botClient.SendMessage(chatId, "Welcome! Use /topic <topic> to add a topic for the meeting.");
                break;

            case "/meeting@FirdavsManageBot":
                await botClient.SendMessage(
                    chatId,
                    "The video call for today's meeting starts now! Join via this link: [Meeting Link](http://example.com/meeting)",
                    parseMode: ParseMode.Markdown
                );
                break;

            case "/poll@FirdavsManageBot":
                await CreatePoll(botClient, chatId);
                break;

            case "/result@FirdavsManageBot":
                await AnnounceMeetingTopic(botClient, chatId);
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
        if (AvailableThemes.Count < 2)
        {
            await botClient.SendMessage(chatId, "Not enough topics available for the poll. Please add more topics using /topic <topic>.");
            return;
        }

        // Randomly select 6 topics from the available list
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
            isAnonymous: false,
            allowsMultipleAnswers: true
        );

        await botClient.PinChatMessage(chatId, pollMessage.MessageId);

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
            await botClient.SendMessage(chatId, "No active poll results available.");
            return;
        }

        var pollId = ActivePolls.Keys.First();
        var pollResults = ActivePolls[pollId];
        var mostVoted = pollResults.OrderByDescending(x => x.Value).First();

        var message = $"The most voted topic is: **{mostVoted.Key}**. See you in the meeting at 8 PM!";
        await botClient.SendMessage(chatId, message, parseMode: ParseMode.Markdown);

        AvailableThemes.Remove(mostVoted.Key);

        ActivePolls.Remove(pollId);
    }
}
