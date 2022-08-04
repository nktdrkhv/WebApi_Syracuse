using MailKit.Net.Smtp;
using MimeKit;

namespace Syracuse;
public class MailService
{
    public async Task SendMailAsync(string email, string reciever, string subject, string message, params (string name, string path)[] filePaths)
    {
        var emailMessage = new MimeMessage();
        var builder = new BodyBuilder();
        builder.TextBody = message;

        if (filePaths is not null)
            foreach ((string name, string path) in filePaths)
                _ = builder.Attachments.Add(name, File.ReadAllBytes(path));

        emailMessage.From.Add(new MailboxAddress("Служба поддержки", "noreply@demo.nktdrkhv.ru"));
        emailMessage.To.Add(new MailboxAddress(reciever, email));
        emailMessage.Subject = subject;
        emailMessage.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(Environment.GetEnvironmentVariable("MailSmtpServer") ?? "smtp.mail.ru", int.Parse(Environment.GetEnvironmentVariable("MailSmtpPort") ?? "465"), true);
        await client.AuthenticateAsync(Environment.GetEnvironmentVariable("MailSmtpUser") ?? "noreply@demo.nktdrkhv.ru", Environment.GetEnvironmentVariable("MailSmtpPassword") ?? "qB8X7RuKs9KuH0YZJBkn");
        _ = await client.SendAsync(emailMessage);
        await client.DisconnectAsync(true);
    }
}

