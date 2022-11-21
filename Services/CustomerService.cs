using System.Text;
using Hangfire;
using AutoMapper;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ValidationException = FluentValidation.ValidationException;

namespace Syracuse;

public interface ICustomerService
{
    public Task HandleTildaAsync(Dictionary<string, string> data);
    public Task HandleYandexAsync(Dictionary<string, string> data);
}

public class CustomerService : ICustomerService
{
    static Func<string, string> Name = System.IO.Path.GetFileNameWithoutExtension;
    static Func<string> NewName = System.IO.Path.GetRandomFileName;

    private readonly ILogger<CustomerService> _logger;
    private readonly IMapper _mapper;
    private readonly IValidator<Client> _clientValidator;
    private readonly IValidator<Agenda> _agendaValidator;
    private readonly IMailService _mail;
    private readonly IPdfService _pdf;
    private readonly IDbService _db;

    public CustomerService(ILogger<CustomerService> logger, IMapper mapper, IValidator<Client> clientValidator, IValidator<Agenda> agendaValidator, IMailService mailSender, IPdfService pdfCreator, IDbService db)
    {
        _logger = logger;
        _mapper = mapper;
        _clientValidator = clientValidator;
        _agendaValidator = agendaValidator;
        _pdf = pdfCreator;
        _mail = mailSender;
        _db = db;
    }

    static CustomerService() => RecurringJob.AddOrUpdate("health-check", () => HealthCheck(), Cron.Hourly);

    public static async Task HealthCheck()
    {
        await using var scope = ServiceActivator.GetAsyncScope();
        var mail = scope?.ServiceProvider.GetService<IMailService>();
        var db = scope?.ServiceProvider.GetService<IDbService>();
        var logger = scope?.ServiceProvider.GetService<ILogger<CustomerService>>();

        foreach (var nonDoneSale in db.Context.Sales.Include(s => s.Product).Include(s => s.WorkoutProgram).Where(s => !s.IsDone && s.IsErrorHandled == null).ToList())
        {
            try
            {
                if (nonDoneSale.OrderId == default)
                    throw new CustomerExсeption("Некорректная обработка продажи: отсутсвует внешний номер заказа");

                if (nonDoneSale.Product?.Count is 0)
                    throw new CustomerExсeption("Некорректная обработка продажи: отсутствуют связанные продукты");

                if (!nonDoneSale.IsSuccessEmailSent)
                {
                    await ForwardSuccessEmailAsync(nonDoneSale, mail, db, logger);
                    nonDoneSale.IsSuccessEmailSent = true;
                }

                if (nonDoneSale.Type is SaleType.Beginner or SaleType.Profi && nonDoneSale.WorkoutProgram is null &&
                    string.IsNullOrWhiteSpace(nonDoneSale.Key) && nonDoneSale.IsAdminNotified)
                    nonDoneSale.IsAdminNotified = false;

                if (nonDoneSale.Type is SaleType.Standart or SaleType.Pro && string.IsNullOrWhiteSpace(nonDoneSale.Nutrition))
                    AttachNutrition(nonDoneSale, db, scope?.ServiceProvider.GetService<PdfService>(), logger);

                if (string.IsNullOrWhiteSpace(nonDoneSale.Key)
                    && (nonDoneSale.ScheduledDeliverTime is null || (nonDoneSale.ScheduledDeliverTime is DateTime scheduled && scheduled < DateTime.UtcNow.AddMinutes(5d)))
                    && (nonDoneSale.Type is SaleType.Beginner or SaleType.Profi && nonDoneSale.WorkoutProgram is not null ||
                    nonDoneSale.Type is SaleType.Standart or SaleType.Pro && !string.IsNullOrWhiteSpace(nonDoneSale.Nutrition)))
                {
                    var content = nonDoneSale.Type switch
                    {
                        SaleType.Beginner or SaleType.Pro => CreateMailContentForWp(nonDoneSale),
                        SaleType.Standart or SaleType.Pro => CreateMailContentForNutrition(nonDoneSale),
                        _ => throw new ArgumentException(),
                    };
                    var delay = ScheduleHelper.GetSchedule();
                    BackgroundJob.Schedule(() => ForwardMail(content, nonDoneSale), delay);
                    nonDoneSale.ScheduledDeliverTime = DateTime.UtcNow + delay;
                }

                if (!nonDoneSale.IsAdminNotified)
                {
                    await NotifyAdminsAsync(nonDoneSale, mail, db, logger);
                    nonDoneSale.IsAdminNotified = true;
                }
            }
            catch (CustomerExсeption ex)
            {
                logger.LogError(ex, "Health check was not passed");
                await ErrorNotify(nonDoneSale, ex.Message);
            }
            catch (MailExсeption ex)
            {
                logger.LogError(ex, "Health check was not passed");
                await ErrorNotify(nonDoneSale, ex.Message);
            }
            catch (DbExсeption ex)
            {
                logger.LogError(ex, "Health check was not passed");
                await ErrorNotify(nonDoneSale, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Health check was not passed");
                await ErrorNotify(nonDoneSale, "ошибка не указана");
            }
            finally
            {
                nonDoneSale.IsErrorHandled = true;
            }
        }

        async Task ErrorNotify(Sale sale, string exMessage)
        {
            var gettersInfo = MatchHelper.TransformToValues(sale);
            gettersInfo["key"] = KeyHelper.UniversalKey;
            var link = UrlHelper.MakeLink(sale.Type, gettersInfo);
            logger.LogInformation($"Admin (health check of [{sale.Type}]): ReInput link for [{sale.Client.Email}] is [{link}]");

            var message = ($"Внимание! Произошла ошибка: <i>{exMessage}</i><br/><br/>{LogHelper.ClientInfo(sale.Client, sale.Agenda)}<br/><br/>Пожалуйста, перезаполните данные клиента по этой ссылке: {link}");
            foreach (var admin in await db.GetInnerEmailsAsync(admins: true))
                await mail.SendMailAsync(MailType.Failure, admin, "Обратите внимание: некорректная обработка продажи", message);
        }
    }

    public async Task HandleTildaAsync(Dictionary<string, string> data)
    {
        var formname = data.Key("formname");
        var handler = formname switch
        {
            "beginner" => HandleBegginerFormAsync(data),
            "profi" => HandleProfiFormAsync(data),
            "online-coach" => HandleCoachFormAsync(data),
            "standart" => HandleStandartFormAsync(data),
            "pro" => HandleProFormAsync(data),
            "posing" => HandlePosingFormAsync(data),
            "endo" => HandleEndoFormAsync(data),

            "add-worker" => AddWorker(data),
            "delete-worker" => DeleteWorker(data),
            "add-contact" => AddContact(data),
            "delete-contact" => DeleteContact(data),

            "add-or-update-product" => AddOrUpdateProduct(data),
            "add-or-update-content" => AddOrUpdateContent(data),

            "complete-sale" => CompleteSaleAsync(data),

            "load-recepies" => LoadRecepiesAsync(data),
            "load-instructions" => LoadInstructionsAsync(data),
            "add-workout-program" => AddWorkoutProgramAsync(data),

            _ => throw new CustomerExсeption("Пришла форма с некорректным идентификатором"),
        };

        _logger.LogInformation($"Trying to handle: [{formname}] as Tilda");
        await handler;
    }

    public async Task HandleYandexAsync(Dictionary<string, string> data)
    {
        var type = data.Key("type");
        var handler = type switch
        {
            "begginer" => HandleBegginerFormAsync(data),
            "profi" => HandleProfiFormAsync(data),
            "coach" => HandleCoachFormAsync(data),
            "standart" => HandleStandartFormAsync(data),
            "pro" => HandleProFormAsync(data),

            _ => throw new CustomerExсeption("Пришла форма с некорректным идентификатором"),
        };

        _logger.LogInformation($"Trying to handle: [{type}] as Yandex");
        await handler;
    }

    // --------------------------------------------------------------------------------
    #region BaseActions

    private void Map(SaleType type, Dictionary<string, string> data, out Client? client, out Agenda? agenda)
    {
        switch (type)
        {
            case SaleType.Beginner:
            case SaleType.Profi:
            case SaleType.Standart:
            case SaleType.Pro:
            case SaleType.Coach:
                client = _mapper.Map<Dictionary<string, string>, Client>(data);
                agenda = _mapper.Map<Dictionary<string, string>, Agenda>(data);
                _logger.LogInformation($"Client ({type.ToString()}): {client.Name} {client.Phone} {client.Email} is mapped");
                break;
            case SaleType.Posing:
            case SaleType.Endo:
                client = _mapper.Map<Dictionary<string, string>, Client>(data);
                agenda = null;
                _logger.LogInformation($"Client ({type.ToString()}): {client.Name} {client.Phone} {client.Email} is mapped");
                break;
            case SaleType.WorkoutProgram:
            default:
                throw new ArgumentException();
        }
    }
    private async Task<bool> Validate(SaleType type, Dictionary<string, string> data, Client? client, Agenda? agenda)
    {
        try
        {
            if (agenda is not null)
            {
                (_agendaValidator as AgendaValidator).SaleType = type;
                await _agendaValidator.ValidateAndThrowAsync(agenda);
            }

            if (client is not null)
                await _clientValidator.ValidateAndThrowAsync(client);

            _logger.LogInformation($"Client ({type.ToString()}): {client.Name} {client.Phone} {client.Email} is validated");
            return true;
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, $"Client ({type.ToString()}): {client.Name} {client.Phone} {client.Email} – validation fault");
            await HandleValidationErrorAsync(ex, data, type, client, agenda);
            return false;
        }
    }

    // -----------------------

    private static async Task ForwardMail(MailService.MailContent content, Sale sale)
    {
        await using var scope = ServiceActivator.GetAsyncScope();
        var mail = scope?.ServiceProvider.GetService<IMailService>();
        var db = scope?.ServiceProvider.GetService<IDbService>();
        var logger = scope?.ServiceProvider.GetService<ILogger<CustomerService>>();

        try
        {
            await mail.SendMailAsync(content);
            await db.СompleteAsync(sale.Id);
            logger.LogInformation($"Mail({sale.Type.ToString()}): Mail was sent to client [{sale.Client.Email}] with sale ID [{sale.Id}]");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Can't send delayd mail to [{sale.Client.Email}] with sale ID [{sale.Id}]. Hope for the best");
        }
    }

    // -----------------------

    private static async Task AttachAndForwardWpAsync(IDbService db, IMailService mail)
    {
        var awaitingCustomers = db.Context.Sales
            .Include(s => s.Client).Include(s => s.Agenda).Include(s => s.WorkoutProgram)
            .Where(s => (s.Type == SaleType.Beginner || s.Type == SaleType.Profi) && !s.IsDone)
            .ToList();

        foreach (var sale in awaitingCustomers)
            if (sale.WorkoutProgram is null && await db.FindWorkoutProgramAsync(sale.Agenda) is WorkoutProgram wp)
            {
                sale.WorkoutProgram = wp;
                db.Context.Sales.Update(sale);
                var content = CreateMailContentForWp(sale);

                await mail.SendMailAsync(content);
                await db.СompleteAsync(sale.Id);
            }
    }

    private static async Task ForwardSuccessEmailAsync(Sale sale, IMailService mail, IDbService db, ILogger<CustomerService> logger)
    {
        switch (sale.Type)
        {
            case SaleType.Beginner:
                var messageBegginer = "Благодарим за покупку программы тренировок «Begginer»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам персональную программу тренировок.";
                await mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name, "Персональная программа тренировок «Begginer»", messageBegginer);
                logger.LogInformation($"Mail (wp-begginer-successful): sent to {sale.Client.Email}");
                break;
            case SaleType.Profi:
                var messageProfi = "Благодарим за покупку программы тренировок «Profi»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам персональную программу тренировок.";
                await mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name, "Персональная программа тренировок «Profi»", messageProfi);
                logger.LogInformation($"Mail (wp-profi-successful): sent to {sale.Client.Email}");
                break;
            case SaleType.Standart:
                var messageStandart = "Благодарим за покупку программы питания «Standart»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам вашу персональную программу питания.";
                await mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name, "Standart питание: КБЖУ + рацион", messageStandart);
                logger.LogInformation($"Mail (nutrition-standart-successful): sent to {sale.Client.Email}");
                break;
            case SaleType.Pro:
                var messagePro = "Благодарим за покупку программы питания «Pro»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам вашу персональную программу питания и рецепты.";
                await mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name, "PRO питание: КБЖУ + рацион + книга рецептов", messagePro);
                logger.LogInformation($"Mail (nutrition-pro-successful): sent to {sale.Client.Email}");
                break;
            case SaleType.Coach:
                var messageCoach = $"В течение следующего дня (в выходные может потребоваться больше времени) наш тренер свяжется с вами для начала тренировок.<br/><br/><b>Контакты тренера:</b><br/>{await db.GetCoachContactsAsync(sale?.Agenda?.Trainer) ?? "<i>не указаны</i>"}";
                await mail.SendMailAsync(MailType.Success, sale.Client.Email, sale.Client.Name, "Занятия с Online-тренером", messageCoach);
                logger.LogInformation($"Mail (coach): sent to {sale.Client.Email}");
                break;
            case SaleType.Endo:
                var messageEndo = $"Благодарим за покупку! В течение следующего дня (в выходные может потребоваться больше времени) <i>наш эндокринолог</i> свяжется с вами для консультации.";
                await mail.SendMailAsync(MailType.Success, sale.Client.Email, sale.Client.Name, "Консультация эндокринолога", messageEndo);
                logger.LogInformation($"Mail (endo): sent to {sale.Client.Email}");
                break;
            case SaleType.Posing:
                var sb = new StringBuilder();
                foreach (var video in sale.Product ?? Enumerable.Empty<Product>())
                    if (video.Childs is List<Product> children && children.Count != 0)
                    {
                        sb.Append($"<b>{video.Label}</b><br/><br/>");
                        foreach (var child in children) sb.Append(VideoList(child));
                    }
                    else
                        sb.Append(VideoList(video));
                var messagePosing = $"Благодарим за покупку! Ниже Вы можете увидеть ссылки на <i>приватные</i> видео-инструкции:<br/><br/>{sb.ToString()}";
                await mail.SendMailAsync(MailType.Success, sale.Client.Email, sale.Client.Name, "Уроки позинга Fitness Bikini", messagePosing);
                logger.LogInformation($"Mail (posing): sent to {sale.Client.Email}");
                break;
            default:
                throw new ArgumentException();
        }

        string VideoList(Product video)
        {
            var sb = new StringBuilder();
            sb.Append($"<b><i>{video.Label}</i></b><br/>");
            if (video.Content.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is string[] links && links.Length > 0)
                foreach (var link in links)
                    sb.Append(" > " + link + "<br/>");
            sb.Append("<br/>");
            return sb.ToString();
        }
    }

    private static async Task NotifyAdminsAsync(Sale sale, IMailService mail, IDbService db, ILogger<CustomerService> logger)
    {

        switch (sale.Type)
        {
            case SaleType.Beginner:
            case SaleType.Profi:
                if (sale.WorkoutProgram is null)
                {
                    sale.Key = KeyHelper.NewKey();
                    logger.LogInformation($"Client (wp): wp for {sale.Client.Email} doesnt exit");
                    var gettersInfo = MatchHelper.TransformToValues(sale, SaleType.WorkoutProgram);
                    var link = UrlHelper.MakeLink(SaleType.WorkoutProgram.AsReinputLink(), gettersInfo);
                    logger.LogInformation($"Client (wp): New wp link for admins is {link}");

                    var clientInfo = LogHelper.ClientInfo(sale.Client, sale.Agenda);
                    clientInfo = clientInfo.Replace("\n", "<br/>");
                    var emails = await db.GetInnerEmailsAsync(admins: true);
                    var messageWp = $"Необходимо составить новую программу тренировок для:<br/>{clientInfo}<br/>Перейдите для этого по ссылке: <i>{link}</i>";

                    foreach (var admin in emails)
                        await mail.SendMailAsync(MailType.Inner, admin, "Запрос на новую программу тренировок", messageWp);
                }
                else
                {
                    var messageWp = $"Покупка готовой программы тренировок {sale.Type.ToString()}. Клиент:<br/>{LogHelper.ClientInfo(sale.Client).Replace("\n", "<br/>")}";

                    foreach (var admin in await db.GetInnerEmailsAsync(admins: true))
                        await mail.SendMailAsync(MailType.Inner, admin, "Покупка готовой программы тренировок", messageWp);
                    logger.LogInformation($"Mail ({sale.Type.ToString()}): info about {sale.Client.Email} is sent to admins");
                }
                break;
            case SaleType.Standart:
            case SaleType.Pro:
                var messageNut = $"Покупка сгенерированной программы питания {sale.Type.ToString()}. Клиент:<br/>{LogHelper.ClientInfo(sale.Client).Replace("\n", "<br/>")}";

                foreach (var admin in await db.GetInnerEmailsAsync(admins: true))
                    await mail.SendMailAsync(MailType.Inner, admin, "Покупка программы питания", messageNut);
                logger.LogInformation($"Mail ({sale.Type.ToString()}): info about {sale.Client.Email} is sent to admins");
                break;
            case SaleType.Coach:
                var messageCoach = "Новый клиент на онлайн-тренировки:<br/>" +
                            LogHelper.ClientInfo(sale.Client, sale.Agenda).Replace("\n", "<br/>") + "<br/>Пожалуйста, свяжитесь с ним/ней";
                foreach (var worker in await db.GetInnerEmailsAsync(coachNicknames: new[] { sale.Agenda.Trainer }, admins: true))
                    await mail.SendMailAsync(MailType.Inner, worker, "Занятия с Online-тренером: новый клиент", messageCoach);
                logger.LogInformation($"Mail (coach): info about {sale.Client.Email} is sent to admins");
                break;
            case SaleType.Endo:
                var messageEndo = $"Новый клиент на консультацию эндокринолога: <br/>{LogHelper.ClientInfo(sale.Client).Replace("\n", "<br/>")}<br/>Пожалуйста, свяжитесь с ним/ней";
                foreach (var admin in await db.GetInnerEmailsAsync(admins: true))
                    await mail.SendMailAsync(MailType.Inner, admin, "Консультация эндокринолога: новый клиент", messageEndo);
                logger.LogInformation($"Mail (endo): info about {sale.Client.Email} is sent to admins");
                break;
            case SaleType.Posing:
                var messagePosing = $"Покупка видео-уроков: <br/>{LogHelper.ClientInfo(sale.Client).Replace("\n", "<br/>")}";
                foreach (var admin in await db.GetInnerEmailsAsync(admins: true))
                    await mail.SendMailAsync(MailType.Inner, admin, "Покупка видео-уроков", messagePosing);
                logger.LogInformation($"Mail (endo): info about {sale.Client.Email} is sent to admins");
                break;
            default:
                throw new ArgumentException();
        }
    }

    #endregion BaseActions
    // --------------------------------------------------------------------------------
    #region TildaHandlersMain

    private async Task HandleBegginerFormAsync(Dictionary<string, string> data) => await HandleWorkoutProgramAsync(data, SaleType.Beginner);

    private async Task HandleProfiFormAsync(Dictionary<string, string> data) => await HandleWorkoutProgramAsync(data, SaleType.Profi);

    private async Task HandleStandartFormAsync(Dictionary<string, string> data) => await HandleNutritionAsync(data, SaleType.Standart);

    private async Task HandleProFormAsync(Dictionary<string, string> data) => await HandleNutritionAsync(data, SaleType.Pro);

    private async Task HandlePosingFormAsync(Dictionary<string, string> data)
    {
        Map(SaleType.Endo, data, out var client, out _);

        var sale = await _db.AddSaleAsync(client, null, SaleType.Posing, DateTime.UtcNow, false);
        sale.OrderId = int.Parse(data["orderid"]);
        sale.Product = new();
        foreach (var productLabel in data["videos"].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            sale.Product.Add(_db.Context.Products.Where(p => p.Label == productLabel).Select(p => p).Single());

        await ForwardSuccessEmailAsync(sale, _mail, _db, _logger);
        sale.IsSuccessEmailSent = true;

        await NotifyAdminsAsync(sale, _mail, _db, _logger);
        sale.IsAdminNotified = true;

        sale.IsDone = true;
    }

    private async Task HandleEndoFormAsync(Dictionary<string, string> data)
    {
        Map(SaleType.Endo, data, out var client, out _);

        var sale = await _db.AddSaleAsync(client, null, SaleType.Endo, DateTime.UtcNow, false);
        sale.OrderId = int.Parse(data["orderid"]);
        sale.Product = new() { _db.Context.Products.Where(p => p.Code == "endo").Single() };

        await ForwardSuccessEmailAsync(sale, _mail, _db, _logger);
        sale.IsSuccessEmailSent = true;

        await NotifyAdminsAsync(sale, _mail, _db, _logger);
        sale.IsAdminNotified = true;

        sale.IsDone = true;
    }

    private async Task HandleCoachFormAsync(Dictionary<string, string> data)
    {
        Map(SaleType.Coach, data, out var client, out var agenda);
        if (!await Validate(SaleType.Coach, data, client, agenda))
            return;

        string? key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        var sale = await _db.AddSaleAsync(client, agenda, SaleType.Coach,
            dateTime: DateTime.UtcNow,
            isDone: false,
            isNewKey: false, key: key);
        sale.OrderId = int.Parse(data["orderid"]);
        sale.Product = new() { _db.Context.Products.Where(p => p.Code == agenda.Trainer!).Single() };

        await ForwardSuccessEmailAsync(sale, _mail, _db, _logger);
        sale.IsSuccessEmailSent = true;

        await NotifyAdminsAsync(sale, _mail, _db, _logger);
        sale.IsAdminNotified = true;

        sale.IsDone = true;
    }

    #endregion
    // -------------------------------------------------------------------------------

    private async Task HandleWorkoutProgramAsync(Dictionary<string, string> data, SaleType saleType)
    {
        Map(saleType, data, out var client, out var agenda);
        if (!await Validate(saleType, data, client, agenda))
            return;

        string? key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        var sale = await _db.AddSaleAsync(client, agenda, saleType,
            dateTime: DateTime.UtcNow,
            isDone: false,
            isNewKey: false, key: key);
        sale.OrderId = int.Parse(data["orderid"]);
        sale.Product = new() { _db.Context.Products.Where(p => p.Code == saleType.ToString().ToLower()).Single() };

        await ForwardSuccessEmailAsync(sale, _mail, _db, _logger);
        sale.IsSuccessEmailSent = true;

        if (await _db.FindWorkoutProgramAsync(sale.Agenda) is WorkoutProgram wp)
        {
            sale.WorkoutProgram = wp;

            var content = CreateMailContentForWp(sale);
            var delay = ScheduleHelper.GetSchedule();
            BackgroundJob.Schedule(() => ForwardMail(content, sale), delay);
            sale.ScheduledDeliverTime = DateTime.UtcNow + delay;

            _logger.LogInformation($"Client (wp): wp for {sale.Client.Email} is exit. It's {sale.WorkoutProgram.Id} : {sale.WorkoutProgram.ProgramPath}");
        }

        await NotifyAdminsAsync(sale, _mail, _db, _logger);
        sale.IsAdminNotified = true;
    }

    private static MailService.MailContent CreateMailContentForWp(Sale sale)
    {
        var subject = sale.Type is SaleType.Beginner ? "Begginer: готовая программа" : "Profi: готовая программа";
        var message = "Ваша персональная программа тренировок готова! Она прикреплена к данному сообщению.";
        var wpFile = new MailService.FilePath("Персональная программа тренировок.pdf", sale.WorkoutProgram.ProgramPath);
        var content = new MailService.MailContent(MailType.Success, sale.Client.Email, sale.Client.Name, subject, message, new[] { wpFile });
        return content;
    }

    private async Task HandleNutritionAsync(Dictionary<string, string> data, SaleType saleType)
    {
        Map(saleType, data, out var client, out var agenda);
        if (!await Validate(saleType, data, client, agenda))
            return;

        string? key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        var sale = await _db.AddSaleAsync(client, agenda, saleType,
            dateTime: DateTime.UtcNow,
            isDone: false,
            isNewKey: false, key: key);
        sale.OrderId = int.Parse(data["orderid"]);
        sale.Product = new() { _db.Context.Products.Where(p => p.Code == saleType.ToString().ToLower()).Single() };

        if (string.IsNullOrWhiteSpace(sale.Nutrition))
            AttachNutrition(sale, _db, _pdf, _logger);

        await ForwardSuccessEmailAsync(sale, _mail, _db, _logger);
        sale.IsSuccessEmailSent = true;


        var content = CreateMailContentForNutrition(sale);
        var delay = ScheduleHelper.GetSchedule();
        BackgroundJob.Schedule(() => ForwardMail(content, sale), delay);
        sale.ScheduledDeliverTime = DateTime.UtcNow + delay;

        await NotifyAdminsAsync(sale, _mail, _db, _logger);
        sale.IsAdminNotified = true;
    }

    private static void AttachNutrition(Sale sale, IDbService db, IPdfService pdf, ILogger<CustomerService> logger)
    {
        var cpfc = NutritionHelper.CalculateCpfc(sale.Agenda);
        var diet = NutritionHelper.CalculateDiet(cpfc);
        var path = Path.Combine("Resources", "Produced", "Nutritions", NewName());
        pdf.CreateNutrition(path, sale.Agenda, cpfc, diet);

        sale.Nutrition = path;
        db.Context.Sales.Update(sale);
        logger.LogInformation($"Client (nutrition): nutrition {Name(path)} for {sale.Client.Email} calculated");
    }

    private static MailService.MailContent CreateMailContentForNutrition(Sale sale)
    {
        var nutrition = new MailService.FilePath("КБЖУ и рацион.pdf", sale.Nutrition);
        var recepies = new MailService.FilePath("Книга рецептов.pdf", Path.Combine("Resources", "Produced", "Recepies.pdf"));
        var instructions = new MailService.FilePath("Инструкции.pdf", Path.Combine("Resources", "Produced", "Instructions.pdf"));

        switch (sale.Type)
        {
            case SaleType.Standart:
                var msgStandart = "К данному письму приложен PDF документ,в котором находится КБЖУ и примерный рацион питания.";
                var contentStandart = new MailService.MailContent(MailType.Success, sale.Client.Email, sale.Client.Name, "Standart питание: КБЖУ + рацион", msgStandart, new[] { nutrition, instructions });
                return contentStandart;
            case SaleType.Pro:
                var msgPro = "К данному письму приложено два PDF документа. В одном из них находится КБЖУ и примерный рацион питания. В другом – рецепты.";
                var contentPro = new MailService.MailContent(MailType.Success, sale.Client.Email, sale.Client.Name, "PRO питание: КБЖУ + рацион + книга рецептов", msgPro, new[] { nutrition, instructions, recepies });
                return contentPro;
            default:
                throw new ArgumentException();
        }
    }

    // --------------------------------------------------------------------------------

    private async Task HandleValidationErrorAsync(ValidationException ex, Dictionary<string, string> data, SaleType saleType, Client client, Agenda? agenda)
    {
        string? key = data.Key("key");
        if (string.Equals(key, KeyHelper.UniversalKey)) return;
        Sale sale = null;

        if (string.IsNullOrWhiteSpace(key))
        {
            key = KeyHelper.NewKey();
            sale = await _db.AddSaleAsync(client, agenda, saleType,
                dateTime: DateTime.UtcNow,
                isDone: false,
                isNewKey: true, key: key);
            _logger.LogInformation($"Db (validation of [{saleType}]): [{client.Email}] with key [{key}] added data to db. Waiting new data.");
        }
        else
        {
            try
            {
                sale = _db.Context.Sales.Include(s => s.Agenda).Include(s => s.Client).Where(s => s.Key == key).Single();
            }
            catch (Exception e)
            {
                throw new CustomerExсeption($"Отсутсвует запись о продажи с ключем [{key}]", e);
            }
            sale.Client.UpdateWith(client);
            if (agenda is not null) sale.Agenda.UpdateWith(agenda);
            _logger.LogInformation($"Db (validation again of [{saleType}]): [{client.Email}] with key [{key}] found in db. Waiting new data.");
            _logger.LogDebug(sale.Agenda.ToString());
        }

        /// Формирование ссылки для перезаполнения
        var gettersInfo = MatchHelper.TransformToValues(sale);
        var link = UrlHelper.MakeLink(saleType, gettersInfo);
        _logger.LogInformation($"Client (validation of [{saleType}]): ReInput link for [{client.Email}] is [{link}]");

        /// Формирование и отправка письма
        var sb = new StringBuilder()
            .AppendLine("При заполнении анкеты произошла ошибка:<br/>");
        foreach (var error in ex.Errors)
            sb.AppendLine($"{error.ErrorMessage}<br/>");
        sb.AppendLine($"<br/>Пожалуйста, перейдите по ссылке и введите данные повторно!<br/><b>{link}</b>");
        await _mail.SendMailAsync(MailType.Failure, (client.Email, client.Name), saleType.AsErrorTitle(), sb.ToString());
        _logger.LogInformation($"Mail (validation of {saleType}): sent to {client.Email}");
    }

    // --------------------------------------------------------------------------------

    private async Task AddWorkoutProgramAsync(Dictionary<string, string> data)
    {
        var workoutProgram = _mapper.Map<WorkoutProgram>(data);
        workoutProgram.ProgramPath = Path.Combine("Resources", "Produced", "WorkoutPrograms", NewName());
        _logger.LogInformation($"Admin (add wp): wp is mapped. Path is {workoutProgram.ProgramPath}");

        var base64string = data.Key("file");
        await Base64Helper.DecodeToPdf(base64string, workoutProgram.ProgramPath);
        await _db.AddWorkoutProgramAsync(workoutProgram);
        _logger.LogInformation($"Admin (add wp): wp ({Name(workoutProgram.ProgramPath)}) is loaded and added to db");

        await AttachAndForwardWpAsync(_db, _mail);
    }

    // --------------------------------------------------------------------------------

    private async Task LoadRecepiesAsync(Dictionary<string, string> data)
    {
        var path = Path.Combine("Resources", "Produced", "Recepies.pdf");
        var base64string = data.Key("file");
        if (string.IsNullOrEmpty(base64string)) return;
        await Base64Helper.DecodeToPdf(base64string, path);
        _logger.LogInformation($"Admin (load recepies): recepies is loaded.");
    }

    private async Task LoadInstructionsAsync(Dictionary<string, string> data)
    {
        var path = Path.Combine("Resources", "Produced", "Instructions.pdf");
        var base64string = data.Key("file");
        if (string.IsNullOrEmpty(base64string)) return;
        await Base64Helper.DecodeToPdf(base64string, path);
        _logger.LogInformation($"Admin (load Instructions): Instructions is loaded.");
    }

    // --------------------------------------------------------------------------------

    private Task AddOrUpdateProduct(Dictionary<string, string> data)
    {
        if (_db.Context.Products.Where(p => p.Code == data.Key("code")).SingleOrDefault() is Product productToEdit)
        {
            productToEdit.Label = data.Key("label") ?? productToEdit.Label;
            productToEdit.Price = data.Key("price").AsInt() ?? productToEdit.Price;
            _db.Context.Products.Update(productToEdit);
            _logger.LogInformation($"Admin (add or update product): {productToEdit.Code} is updated");
        }
        else if (data.Key("code") is string code)
        {
            var product = new Product() { Code = code, Label = data.Key("label"), Price = data.Key("price").AsInt() ?? 0 };
            _db.Context.Products.Add(product);
        }
        return Task.CompletedTask;
    }

    private Task AddOrUpdateContent(Dictionary<string, string> data)
    {
        if (_db.Context.Products.Include(p => p.Childs).Include(p => p.Parents).Where(p => p.Code == data.Key("code")).SingleOrDefault() is Product productToEdit && productToEdit.Childs?.Count is 0)
        {
            productToEdit.Content = data.Key("content")?.Trim();
            if (data.Key("child-of") is string childOf)
            {
                var parentCode = childOf[1..];
                var parent = _db.Context.Products.Include(p => p.Childs).Where(p => p.Code == parentCode).Single();

                if (childOf.StartsWith('+'))
                {
                    productToEdit.Parents?.Add(parent);
                    parent.Childs?.Add(productToEdit);
                }
                else if (childOf.StartsWith('-'))
                {
                    productToEdit.Parents?.Remove(parent);
                    parent.Childs?.Remove(productToEdit);
                }

                _db.Context.Products.Update(parent);
            }
            _db.Context.Products.Update(productToEdit);
        }
        return Task.CompletedTask;
    }

    private Task AddWorker(Dictionary<string, string> data)
    {
        try
        {
            var worker = _mapper.Map<Worker>(data);
            if (!string.IsNullOrWhiteSpace(worker.Name) && !string.IsNullOrWhiteSpace(worker.Nickname))
            {
                _db.Context.Workers.Add(worker);
                _logger.LogInformation($"Admin (add woker); [{worker.Name}] is added");
            }
            else
                _logger.LogInformation($"Admin (add woker): worker is not added");
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Admin (add woker): error. worker is not added");
        }
        return Task.CompletedTask;
    }

    private Task DeleteWorker(Dictionary<string, string> data)
    {
        if (_db.Context.Workers.Where(w => w.Nickname == data.Key("nickname")).FirstOrDefault() is Worker workerToDelete)
        {
            _db.Context.Workers.Remove(workerToDelete);
            _logger.LogInformation($"Admin (delete worker): [{workerToDelete.Name}] is deleted");
        }
        else
            _logger.LogInformation($"Admin (delete worker): [{data.Key("nickname")}] is not exist");
        return Task.CompletedTask;
    }

    private Task AddContact(Dictionary<string, string> data)
    {
        if (_db.Context.Workers.Where(w => w.Nickname == data.Key("nickname")).FirstOrDefault() is Worker worker)
        {
            var contact = _mapper.Map<Contact>(data);
            if (!string.IsNullOrWhiteSpace(contact.Info))
            {
                contact.Worker = worker;
                _db.Context.Contacts.Add(contact);
                _logger.LogInformation($"Admin (add contact): [{worker.Name}] has new [{contact.Type}] is [{contact.Info}]");
            }
            else
                _logger.LogInformation($"Admin (add contact): contacts for [{worker.Name}] is not added");
        }
        else
            _logger.LogInformation($"Admin (add contact): Worker with nickname [{data.Key("nickname")}] does not exist");
        return Task.CompletedTask;
    }

    private Task DeleteContact(Dictionary<string, string> data)
    {
        if (_db.Context.Workers.Where(w => w.Nickname == data.Key("nickname")).FirstOrDefault() is Worker worker)
        {
            var info = data.Key("info");
            if (_db.Context.Contacts.Where(c => c.Worker == worker && c.Info == info).FirstOrDefault() is Contact contact)
            {
                _db.Context.Contacts.Remove(contact);
                _logger.LogInformation($"Admin (delete contact): [{contact.Info}] from [{worker.Nickname}] is deleted");
            }
            else
                _logger.LogInformation($"Admin (delete contact): [{data.Key("info")}] from [{worker.Nickname}] is not exist");
        }
        else
            _logger.LogInformation($"Admin (delete contact): Worker with nickname [{data.Key("nickname")}] does not exist");
        return Task.CompletedTask;
    }

    private async Task CompleteSaleAsync(Dictionary<string, string> data)
    {
        if (data.Key("sale-id").AsInt() is int saleId)
        {
            await _db.СompleteAsync(saleId);
            _logger.LogInformation($"Admin (complete sale): Sale ID – [{saleId}] is complete");
        }
        else
            _logger.LogInformation($"Admin (complete sale): wrong sale id");
    }
}