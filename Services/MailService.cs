using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Net.Imap;
using MailKit;

namespace Syracuse;

public interface IMailService
{
    Task SendMailAsync(MailType type, (string email, string name)[] addressee, string subject, string message, params (string name, string path)[] filePaths);
    Task SendMailAsync(MailType type, (string email, string name) addressee, string subject, string message, params (string name, string path)[] filePaths);

    Task LoadAttachmentAsync(string key, string path);
}

public class MailService : IMailService, IAsyncDisposable
{
    private ILogger<MailService> _logger;

    private static readonly string s_successMailPath = Path.Combine("Resources", "Templates", "success-mail.html");
    private static readonly string s_awaitMailPath = Path.Combine("Resources", "Templates", "await-mail.html");
    private static readonly string s_failureMailPath = Path.Combine("Resources", "Templates", "failure-mail.html");
    private static readonly string s_innerMailPath = Path.Combine("Resources", "Templates", "inner-mail.html");

    private static readonly string s_imapHost = "imap.mail.ru";
    private static readonly string s_smtpHost = "smtp.mail.ru";
    private static readonly int s_smtpPort = 465;
    private static readonly int s_imapPort = 993;

    private static readonly string s_user = Environment.GetEnvironmentVariable("MAIL_USER") ?? "noreply@demo.nktdrkhv.ru";
    private static readonly string s_password = Environment.GetEnvironmentVariable("MAIL_PASS") ?? "YUStitPgTTMFmQSJ4SuT";

    private static readonly MailboxAddress s_from = new MailboxAddress(
        Environment.GetEnvironmentVariable("MAIL_FROM_NAME") ?? "Команда",
        Environment.GetEnvironmentVariable("MAIL_FROM_ADDR") ?? "noreply@demo.nktdrkhv.ru");

    private readonly SmtpClient _smtpClient;

    public MailService(ILogger<MailService> logger)
    {
        _logger = logger;

        try
        {
            _smtpClient = new SmtpClient();
            _smtpClient.ConnectAsync(s_smtpHost, s_smtpPort, true).Wait();
            _smtpClient.AuthenticateAsync(s_user, s_password).Wait();
        }
        catch
        {
            _logger.LogWarning("Problem with SMTP auth");
            throw new MailExсeption("Ошибка при авторизации на SMTP сервере почты");
        }
    }

    public async ValueTask DisposeAsync() => await _smtpClient.DisconnectAsync(true);

    public async Task SendMailAsync(MailType type, (string email, string name) addressee, string subject, string message, params (string name, string path)[] filePaths) => await SendMailAsync(type, new[] { addressee }, subject, message, filePaths);

    public async Task SendMailAsync(MailType type, (string email, string name)[] addressee, string subject, string message, params (string name, string path)[] filePaths)
    {
        var builder = new BodyBuilder();

        try
        {
            var html = type switch
            {
                MailType.Success => await File.ReadAllTextAsync(s_successMailPath),
                MailType.Awaiting => await File.ReadAllTextAsync(s_awaitMailPath),
                MailType.Failure => await File.ReadAllTextAsync(s_failureMailPath),
            };

            var htmlBody = html.Replace("TEXT", message);
            builder.HtmlBody = htmlBody;
        }
        catch
        {
            _logger.LogWarning("Problem with loading mail template");
            throw new MailExсeption("Ошибка при загрузке шаблона письма");
        }

        try
        {
            if (filePaths is not null)
                foreach ((string name, string path) in filePaths)
                    builder.Attachments.Add(name, File.ReadAllBytes(path));
        }
        catch
        {
            _logger.LogWarning("Problem with adding attachments");
            throw new MailExсeption("Ошибка при добавлении вложений в письмо");
        }

        try
        {
            foreach (var addr in addressee)
            {
                var emailMessage = new MimeMessage();
                emailMessage.From.Add(s_from);
                emailMessage.Bcc.Add(s_from);
                emailMessage.To.Add(new MailboxAddress(addr.name, addr.email));
                emailMessage.Subject = subject;
                emailMessage.Body = builder.ToMessageBody();
                await _smtpClient.SendAsync(emailMessage);
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Problem with sending email");
            throw new MailExсeption("Ошибка при отправке сообщений", ex);
        }
    }

    public async Task LoadAttachmentAsync(string subject, string path)
    {
        try
        {
            if (string.IsNullOrEmpty(subject)) throw new Exception("Не указана тема для загрузки");

            using var imapClient = new ImapClient();
            imapClient.Connect(s_imapHost, s_imapPort, true);
            imapClient.Authenticate(s_user, s_password);

            var inbox = imapClient.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite);

            bool isDone = false;
            var attemptCount = 0;
            while (isDone is false)
            {
                if (++attemptCount == 15) throw new Exception("Яндекс.Формы не отправили программу тренировок");
                await Task.Delay(15000);

                var notSeenUid = await inbox.SearchAsync(SearchQuery.NotSeen);
                var fetches = await inbox.FetchAsync(notSeenUid, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope);

                foreach (var fetch in fetches)
                    if (fetch.Envelope.Subject.Equals(subject))
                    {
                        var message = inbox.GetMessage(fetch.UniqueId);
                        var attachment = message.Attachments.FirstOrDefault(); ;
                        using var fs = new FileStream(path, FileMode.OpenOrCreate);
                        if (attachment is null) throw new Exception("Яндекс.Формы не приложили программу тренировок");
                        ((MimePart) attachment)?.Content.DecodeTo(fs);
                        isDone = true;
                        await inbox.StoreAsync(fetch.UniqueId, new StoreFlagsRequest(StoreAction.Add, MessageFlags.Seen));
                        break;
                    }
            }

            await inbox.CloseAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Problem with attachment loading");
            throw new MailExсeption("Ошибка при загрузке вложения.", ex);
        }
    }
}

