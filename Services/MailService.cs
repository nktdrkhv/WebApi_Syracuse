using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Net.Imap;
using MailKit;

namespace Syracuse;

public interface IMailService : IAsyncDisposable
{
    Task SendMailAsync(MailService.MailContent content);
    Task SendMailAsync(MailType type, string addresseeEmail, string addresseeName, string subject, string message, params MailService.FilePath[] filePaths);
    Task SendMailAsync(MailType type, (string email, string name)[] addressee, string subject, string message, params (string name, string path)[] filePaths);
    Task SendMailAsync(MailType type, (string email, string name) addressee, string subject, string message, params (string name, string path)[] filePaths);
    //Task LoadAttachmentAsync(string key, string path);
}

public class MailService : IMailService
{
    public record FilePath(string name, string path);
    public record MailContent(MailType type, string email, string name, string subject, string message, FilePath[] files);

    private ILogger<MailService> _logger;
    private bool _isInit = false;

    private static readonly string s_successMailPath = Path.Combine("Resources", "Templates", "success-mail.html");
    private static readonly string s_awaitMailPath = Path.Combine("Resources", "Templates", "await-mail.html");
    private static readonly string s_failureMailPath = Path.Combine("Resources", "Templates", "failure-mail.html");
    private static readonly string s_innerMailPath = Path.Combine("Resources", "Templates", "inner-mail.html");
    private static readonly string s_smtpHost = "smtp.mail.ru";
    private static readonly int s_smtpPort = 465;
    private static string s_user => Environment.GetEnvironmentVariable("MAIL_USER");
    private static string s_password => Environment.GetEnvironmentVariable("MAIL_PASS");
    private static bool s_fake => Environment.GetEnvironmentVariable("MAIL_FAKE").Equals("yes");
    private static MailboxAddress s_from => new MailboxAddress(
        Environment.GetEnvironmentVariable("MAIL_FROM_NAME"),
        Environment.GetEnvironmentVariable("MAIL_FROM_ADDR"));

    private SmtpClient _smtpClient;

    public MailService(ILogger<MailService> logger) => _logger = logger;

    public async ValueTask DisposeAsync()
    {
        if (_smtpClient is not null)
        {
            await _smtpClient.DisconnectAsync(true);
            _smtpClient.Dispose();
            _logger.LogInformation("Mail (dispose): disposed");
        }
    }

    private async Task Init()
    {
        if (s_fake || _isInit) return;

        try
        {

            _smtpClient = new SmtpClient();
            await _smtpClient.ConnectAsync(s_smtpHost, s_smtpPort, true);
            await _smtpClient.AuthenticateAsync(s_user, s_password);
            _isInit = true;
        }
        catch (Exception e)
        {
            _logger.LogWarning("Mail (Init): problem with SMTP auth");
            throw new MailExсeption("Ошибка при авторизации на SMTP сервере почты", e);
        }
    }

    public async Task SendMailAsync(MailService.MailContent content) => await SendMailAsync(content.type, content.email, content.name, content.subject, content.message, content.files);

    public async Task SendMailAsync(MailType type, string addresseeEmail, string addresseeName, string subject, string message, params MailService.FilePath[] filePaths) => await SendMailAsync(type, new[] { (addresseeEmail, addresseeName) }, subject, message, filePaths.Select(fp => (fp.name, fp.path)).ToArray());

    public async Task SendMailAsync(MailType type, (string email, string name) addressee, string subject, string message, params (string name, string path)[] filePaths) => await SendMailAsync(type, new[] { addressee }, subject, message, filePaths);

    public async Task SendMailAsync(MailType type, (string email, string name)[] addressee, string subject, string message, params (string name, string path)[] filePaths)
    {
        await Init();
        var builder = new BodyBuilder();

        try
        {
            var html = type switch
            {
                MailType.Success => await File.ReadAllTextAsync(s_successMailPath),
                MailType.Awaiting => await File.ReadAllTextAsync(s_awaitMailPath),
                MailType.Failure => await File.ReadAllTextAsync(s_failureMailPath),
                MailType.Inner => await File.ReadAllTextAsync(s_innerMailPath),
                _ => throw new NotImplementedException(),
            };

            var htmlBody = html.Replace("TEXT", message);
            builder.HtmlBody = htmlBody;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Mail (html coad): problem with loading mail template");
            throw new MailExсeption("Ошибка при загрузке шаблона письма");
        }

        try
        {
            if (filePaths is not null)
                foreach ((string name, string path) in filePaths)
                    builder.Attachments.Add(name, File.ReadAllBytes(path));
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, $"Mail (add attchmnts): cant add attachment {filePaths.GetHashCode()}");
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

                if (s_fake)
                {
                    _logger.LogWarning($"Mail (sending): fake mail is sent to {addr.email} - {addr.name} with message {message}");
                    continue;
                }
                else
                    await _smtpClient.SendAsync(emailMessage);
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Mail (set properties and sending): problem with sending email. EX: {ex.Message}");
            throw new MailExсeption("Ошибка при отправке сообщений", ex);
        }
    }

    // public async Task LoadAttachmentAsync(string subject, string path)
    // {
    //     try
    //     {
    //         if (string.IsNullOrEmpty(subject)) throw new Exception("Не указана тема для загрузки");

    //         using var imapClient = new ImapClient();
    //         imapClient.Connect(s_imapHost, s_imapPort, true);
    //         imapClient.Authenticate(s_user, s_password);

    //         var inbox = imapClient.GetFolder("YandexForms");
    //         await inbox.OpenAsync(FolderAccess.ReadWrite);

    //         bool isDone = false;
    //         var attemptCount = 0;
    //         while (isDone is false)
    //         {
    //             if (++attemptCount == 5) throw new MailExсeption("Яндекс.Формы не отправили программу тренировок");
    //             await Task.Delay(5000);
    //             _logger.LogInformation($"Mail (load attch): #{attemptCount} try");

    //             var notSeenUid = await inbox.SearchAsync(SearchQuery.NotSeen);
    //             var fetches = await inbox.FetchAsync(notSeenUid, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope);

    //             foreach (var fetch in fetches)
    //                 if (fetch.Envelope.Subject.Equals(subject))
    //                 {
    //                     var message = inbox.GetMessage(fetch.UniqueId);
    //                     var attachment = message.Attachments.FirstOrDefault(); ;
    //                     using var fs = new FileStream(path, FileMode.OpenOrCreate); //
    //                     if (attachment is null) throw new MailExсeption("Яндекс.Формы не приложили программу тренировок");
    //                     ((MimePart)attachment).Content.DecodeTo(fs);
    //                     isDone = true;
    //                     await inbox.StoreAsync(fetch.UniqueId, new StoreFlagsRequest(StoreAction.Add, MessageFlags.Seen));
    //                     break;
    //                 }
    //         }

    //         _logger.LogInformation($"Mail (load attch): done");
    //         await inbox.CloseAsync(true);
    //     }
    //     catch (MailExсeption ex)
    //     {
    //         _logger.LogWarning("Mail (load attch): known issue");
    //         throw new MailExсeption($"Ошибка при загрузке вложения. {ex.Message}");
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogWarning("Mail (load attch): uknown issue");
    //         throw new MailExсeption($"Неизвестная ошибка при отправке вложения.", ex);
    //     }
    // }
}

