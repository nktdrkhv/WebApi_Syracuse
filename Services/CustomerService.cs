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

    public async Task HandleTildaAsync(Dictionary<string, string> data)
    {
        var formname = data.Key("formname");
        var handler = formname switch
        {
            "begginer" => HandleBegginerFormAsync(data),
            "profi" => HandleProfiFormAsync(data),
            "coach" => HandleCoachFormAsync(data),
            "standart" => HandleStandartFormAsync(data),
            "pro" => HandleProFormAsync(data),
            "posing" => HandlePosingFormAsync(data),
            "endo" => HandleEndoFormAsync(data),

            "add_worker" => Task.Run(() => AddWorker(data)),
            "delete_worker" => Task.Run(() => DeleteWorker(data)),

            "add_contact" => Task.Run(() => AddContact(data)),
            "delete_contact" => Task.Run(() => DeleteContact(data)),

            "complete_sale" => CompleteSaleAsync(data),

            "load_recepies" => LoadRecepiesAsync(data),
            "load_instructions" => LoadInstructionsAsync(data),
            "add_workout_program" => AddWorkoutProgramAsync(data),

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
            case SaleType.Begginer:
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

    //static CustomerService() => RecurringJob.AddOrUpdate<IServiceProvider>("send-out-products-to-customers", sp => AttachNewWpAndSendAsync(sp), Cron.Daily);

    private static async Task ForwardMail(IServiceProvider provider, MailService.MailContent content, Sale sale)
    {
        using var scope = provider.CreateScope();
        await using var mail = scope.ServiceProvider.GetService<IMailService>();
        await using var db = scope.ServiceProvider.GetService<IDbService>();
        var logger = provider.GetService<ILogger<CustomerService>>();

        await mail.SendMailAsync(content);
        await db.СompleteAsync(sale.Id);
        logger.LogInformation($"Mail({sale.Type.ToString()}): Mail was sent to client [{sale.Client.Email}] with sale ID [{sale.Id}]");
    }

    // -----------------------

    private async Task AttachWpAndForwardAsync()
    {
        var awaitingCustomers = _db.Context.Sales
            .Include(s => s.Client).Include(s => s.Agenda).Include(s => s.WorkoutProgram)
            .Where(s => (s.Type == SaleType.Begginer || s.Type == SaleType.Profi) && !s.IsDone)
            .ToList();

        foreach (var sale in awaitingCustomers)
            if (sale.WorkoutProgram is null && await _db.FindWorkoutProgramAsync(sale.Agenda) is WorkoutProgram wp)
            {
                sale.WorkoutProgram = wp;
                _db.Context.Sales.Update(sale);
                var content = CreateContentForWp(sale);

                await _mail.SendMailAsync(content);
                await _db.СompleteAsync(sale.Id);
            }
    }

    private async Task ForwardSuccessEmailAsync(Sale sale)
    {
        switch (sale.Type)
        {
            case SaleType.Begginer:
                var messageBegginer = "Благодарим за покупку программы тренировок «Begginer»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам персональную программу тренировок.";
                await _mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name, "Персональная программа тренировок «Begginer»", messageBegginer);
                _logger.LogInformation($"Mail (wp-begginer-successful): sent to {sale.Client.Email}");
                break;
            case SaleType.Profi:
                var messageProfi = "Благодарим за покупку программы тренировок «Profi»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам персональную программу тренировок.";
                await _mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name, "Персональная программа тренировок «Profi»", messageProfi);
                _logger.LogInformation($"Mail (wp-profi-successful): sent to {sale.Client.Email}");
                break;
            case SaleType.Standart:
                var messageStandart = "Благодарим за покупку программы питания «Standart»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам вашу персональную программу питания.";
                await _mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name, "Standart питание: КБЖУ + рацион", messageStandart);
                break;
            case SaleType.Pro:
                var messagePro = "Благодарим за покупку программы питания «Pro»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам вашу персональную программу питания и рецепты.";
                await _mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name, "PRO питание: КБЖУ + рацион + книга рецептов", messagePro);
                break;
            case SaleType.Coach:
                var messageCoach = $"В течение следующего дня (в выходные может потребоваться больше времени) наш тренер свяжется с вами для начала тренировок.<br/><br/><b>Контакты тренера:</b><br/>{await _db.GetCoachContactsAsync(sale?.Agenda?.Trainer) ?? "<i>не указаны</i>"}";
                await _mail.SendMailAsync(MailType.Success, sale.Client.Email, sale.Client.Name, "Занятия с Online-тренером", messageCoach);
                _logger.LogInformation($"Mail (coach): sent to {sale.Client.Email}");
                break;
            case SaleType.Endo:
                var messageEndo = $"В течение следующего дня (в выходные может потребоваться больше времени) <i>наш эндокринолог</i> свяжется с вами для консультации";
                await _mail.SendMailAsync(MailType.Success, sale.Client.Email, sale.Client.Name, "Консультация эндокринолога", messageEndo);
                _logger.LogInformation($"Mail (endo): sent to {sale.Client.Email}");
                break;
            case SaleType.Posing:
                break;
            default:
                throw new ArgumentException();
        }
    }

    private async Task NotifyAdminsAsync(Sale sale)
    {

        switch (sale.Type)
        {
            case SaleType.Begginer:
            case SaleType.Profi:
                if (sale.WorkoutProgram is null)
                {
                    _logger.LogInformation($"Client (wp): wp for {sale.Client.Email} doesnt exit");
                    var gettersInfo = MatchHelper.TransformToValues(sale, SaleType.WorkoutProgram);
                    var link = UrlHelper.MakeLink(SaleType.WorkoutProgram.AsReinputLink(), gettersInfo);
                    _logger.LogInformation($"Client (wp): New wp link for admins is {link}");

                    var clientInfo = LogHelper.ClientInfo(sale.Client, sale.Agenda);
                    clientInfo = clientInfo.Replace("\n", "<br/>");
                    var emails = await _db.GetInnerEmailsAsync(admins: true);
                    var messageWp = $"Необходимо составить новую программу тренировок для:<br/>{clientInfo}<br/>Перейдите для этого по ссылке: <i>{link}</i>";

                    foreach (var admin in await _db.GetInnerEmailsAsync(admins: true))
                        await _mail.SendMailAsync(MailType.Inner, admin, "Запрос на новую программу тренировок", messageWp);
                }
                else
                {
                    var messageWp = $"Покупка готовой программы тренировок {sale.Type.ToString()}. Клиент:<br/>{LogHelper.ClientInfo(sale.Client).Replace("\n", "<br/>")}";

                    foreach (var admin in await _db.GetInnerEmailsAsync(admins: true))
                        await _mail.SendMailAsync(MailType.Inner, admin, "Покупка готовой программы тренировок", messageWp);
                    _logger.LogInformation($"Mail ({sale.Type.ToString()}): info about {sale.Client.Email} is sent to admins");
                }
                break;
            case SaleType.Standart:
            case SaleType.Pro:
                var messageNut = $"Покупка сгенерированной программы питания {sale.Type.ToString()}. Клиент:<br/>{LogHelper.ClientInfo(sale.Client).Replace("\n", "<br/>")}";

                foreach (var admin in await _db.GetInnerEmailsAsync(admins: true))
                    await _mail.SendMailAsync(MailType.Inner, admin, "Покупка программы питания", messageNut);
                _logger.LogInformation($"Mail ({sale.Type.ToString()}): info about {sale.Client.Email} is sent to admins");
                break;
            case SaleType.Coach:
                var messageCoach = "Новый клиент на онлайн-тренировки:<br/>" +
                            LogHelper.ClientInfo(sale.Client, sale.Agenda).Replace("\n", "<br/>") + "<br/>Пожалуйста, свяжитесь с ним/ней";
                foreach (var worker in await _db.GetInnerEmailsAsync(coachNicknames: new[] { sale.Agenda.Trainer }, admins: true))
                    await _mail.SendMailAsync(MailType.Inner, worker, "Занятия с Online-тренером: новый клиент", messageCoach);
                _logger.LogInformation($"Mail (coach): info about {sale.Client.Email} is sent to admins");
                break;
            case SaleType.Endo:
                var messageEndo = $"Новый клиент на консультацию эндокринолога: <br/>{LogHelper.ClientInfo(sale.Client).Replace("\n", "<br/>")}<br/>Пожалуйста, свяжитесь с ним/ней";
                foreach (var admin in await _db.GetInnerEmailsAsync(admins: true))
                    await _mail.SendMailAsync(MailType.Inner, admin, "Консультация эндокринолога: новый клиент", messageEndo);
                _logger.LogInformation($"Mail (endo): info about {sale.Client.Email} is sent to admins");
                break;
            case SaleType.Posing:
                break;
            default:
                throw new ArgumentException();
        }
    }

    #endregion BaseActions
    // --------------------------------------------------------------------------------
    #region TildaHandlersMain

    private async Task HandleBegginerFormAsync(Dictionary<string, string> data) => await HandleWorkoutProgramAsync(data, SaleType.Begginer);

    private async Task HandleProfiFormAsync(Dictionary<string, string> data) => await HandleWorkoutProgramAsync(data, SaleType.Profi);

    private async Task HandleStandartFormAsync(Dictionary<string, string> data) => await HandleNutritionAsync(data, SaleType.Standart);

    private async Task HandleProFormAsync(Dictionary<string, string> data) => await HandleNutritionAsync(data, SaleType.Pro);

    private async Task HandlePosingFormAsync(Dictionary<string, string> data)
    {
        await Task.Yield();
    }

    private async Task HandleEndoFormAsync(Dictionary<string, string> data)
    {
        Map(SaleType.Endo, data, out var client, out _);

        var sale = await _db.AddSaleAsync(client, null, SaleType.Endo, DateTime.UtcNow, true);

        await ForwardSuccessEmailAsync(sale);
        sale.IsSuccessEmailSent = true;
        _db.Context.Sales.Update(sale);

        await NotifyAdminsAsync(sale);
        sale.IsAdminNotified = true;
        _db.Context.Sales.Update(sale);
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
            isDone: true,
            isNewKey: false, key: key);

        await ForwardSuccessEmailAsync(sale);
        sale.IsSuccessEmailSent = true;
        _db.Context.Sales.Update(sale);

        await NotifyAdminsAsync(sale);
        sale.IsAdminNotified = true;
        _db.Context.Sales.Update(sale);
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

        await ForwardSuccessEmailAsync(sale);
        sale.IsSuccessEmailSent = true;
        _db.Context.Sales.Update(sale);

        if (await _db.FindWorkoutProgramAsync(sale.Agenda) is WorkoutProgram wp)
        {
            sale.WorkoutProgram = wp;
            _db.Context.Sales.Update(sale);
            var content = CreateContentForWp(sale);
            BackgroundJob.Schedule<IServiceProvider>(sp => ForwardMail(sp, content, sale), ScheduleHelper.GetSchedule());
            _logger.LogInformation($"Client (wp): wp for {sale.Client.Email} is exit. It's {sale.WorkoutProgram.Id} : {sale.WorkoutProgram.ProgramPath}");
        }

        await NotifyAdminsAsync(sale);
        sale.IsAdminNotified = true;
        _db.Context.Sales.Update(sale);
    }

    private MailService.MailContent CreateContentForWp(Sale sale)
    {
        var subject = sale.Type is SaleType.Begginer ? "Begginer: готовая программа" : "Profi: готовая программа";
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

        var cpfc = NutritionHelper.CalculateCpfc(agenda);
        var diet = NutritionHelper.CalculateDiet(cpfc);
        var path = Path.Combine("Resources", "Produced", "Nutritions", NewName());
        _pdf.CreateNutrition(path, agenda, cpfc, diet);
        _logger.LogInformation($"Client (nutrition): nutrition {Name(path)} for {client.Email} calculated");

        var nutrition = new MailService.FilePath("КБЖУ и рацион.pdf", path);
        var recepies = new MailService.FilePath("Книга рецептов.pdf", Path.Combine("Resources", "Produced", "Recepies.pdf"));
        var instructions = new MailService.FilePath("Инструкции.pdf", Path.Combine("Resources", "Produced", "Instructions.pdf"));

        await ForwardSuccessEmailAsync(sale);
        sale.IsSuccessEmailSent = true;

        switch (saleType)
        {
            case SaleType.Standart:
                var msgStandart = "К данному письму приложен PDF документ,в котором находится КБЖУ и примерный рацион питания.";
                var contentStandart = new MailService.MailContent(MailType.Success, client.Email, client.Name, "Standart питание: КБЖУ + рацион", msgStandart, new[] { nutrition, instructions });
                BackgroundJob.Schedule<IServiceProvider>(sp => ForwardMail(sp, contentStandart, sale), ScheduleHelper.GetSchedule());
                break;
            case SaleType.Pro:
                var msgPro = "К данному письму приложено два PDF документа. В одном из них находится КБЖУ и примерный рацион питания. В другом – рецепты.";
                var contentPro = new MailService.MailContent(MailType.Success, client.Email, client.Name, "PRO питание: КБЖУ + рацион + книга рецептов", msgPro, new[] { nutrition, instructions, recepies });
                BackgroundJob.Schedule<IServiceProvider>(sp => ForwardMail(sp, contentPro, sale), ScheduleHelper.GetSchedule());
                break;
        }
    }

    // --------------------------------------------------------------------------------

    private async Task HandleValidationErrorAsync(ValidationException ex, Dictionary<string, string> data, SaleType saleType, Client client, Agenda? agenda)
    {
        string? existKey = data.Key("key");
        if (string.Equals(existKey, KeyHelper.UniversalKey)) return;
        string key; Sale sale;

        if (string.IsNullOrWhiteSpace(existKey))
        {
            key = KeyHelper.NewKey();
            sale = await _db.AddSaleAsync(client, agenda, saleType,
                dateTime: DateTime.UtcNow,
                isDone: false,
                isNewKey: true,
                key: key);
            _logger.LogInformation($"Db (validation of [{saleType}]): [{client.Email}] with key [{key}] added data to db. Waiting new data.");
        }
        else
        {
            key = existKey;
            sale = (from s in _db.Context.Sales.Include(s => s.Agenda).Include(s => s.Client)
                    where s.Key == key
                    select s).Single();
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
            .AppendLine("При заполнении анкеты произошла ошибка: <br />");
        foreach (var error in ex.Errors)
            sb.AppendLine($"{error.ErrorMessage} <br/>");
        sb.AppendLine($"<br/> Пожалуйста, перейдите по ссылке и введите данные повторно! <br /><b>{link}</b>");
        await _mail.SendMailAsync(MailType.Failure, (client.Email, client.Name), saleType.AsErrorTitle(), sb.ToString());
        _logger.LogInformation($"Mail (validation of {saleType}): sent to {client.Email}");
    }

    private async Task AddWorkoutProgramAsync(Dictionary<string, string> data)
    {
        var workoutProgram = _mapper.Map<WorkoutProgram>(data);
        workoutProgram.ProgramPath = Path.Combine("Resources", "Produced", "WorkoutPrograms", NewName());
        _logger.LogInformation($"Admin (add wp): wp is mapped. Path is {workoutProgram.ProgramPath}");

        var base64string = data.Key("file");
        await Base64Helper.DecodeToPdf(base64string, workoutProgram.ProgramPath);
        await _db.AddWorkoutProgramAsync(workoutProgram);
        _logger.LogInformation($"Admin (add wp): wp ({Name(workoutProgram.ProgramPath)}) is loaded and added to db");

        await AttachWpAndForwardAsync();
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

    private void AddWorker(Dictionary<string, string> data)
    {
        try
        {
            var worker = _mapper.Map<Worker>(data);
            if (string.IsNullOrWhiteSpace(worker.Name) && string.IsNullOrWhiteSpace(worker.Nickname))
            {
                _db.Context.Workers.Add(worker);
                _logger.LogInformation($"Admin (add woker); [{worker.Name}] is added");
            }
            else
                _logger.LogInformation($"Admin (add woker); worker is not added");
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Admin (add woker): error. worker is not added");
        }
    }

    private void DeleteWorker(Dictionary<string, string> data)
    {
        var workerToDelete = _db.Context.Workers.Where(w => w.Nickname == data.Key("nickname")).FirstOrDefault();
        if (workerToDelete is not null)
        {
            _db.Context.Workers.Remove(workerToDelete);
            _logger.LogInformation($"Admin (delete worker): [{workerToDelete.Name}] is deleted");
        }
        else
            _logger.LogInformation($"Admin (delete worker): [{data.Key("nickname")}] is not exist");
    }

    private void AddContact(Dictionary<string, string> data)
    {
        var worker = _db.Context.Workers.Where(w => w.Nickname == data.Key("nickname")).FirstOrDefault();
        if (worker is null)
        {
            _logger.LogInformation($"Admin (add contact): Worker with nickname [{data.Key("nickname")}] does not exist");
            return;
        }

        var contact = _mapper.Map<Contact>(data);
        if (string.IsNullOrWhiteSpace(contact.Info))
        {
            contact.Worker = worker;
            _db.Context.Contacts.Add(contact);
            _logger.LogInformation($"Admin (add contact): [{worker.Name}] has new [{contact.Type}] is [{contact.Info}]");
        }
        else
            _logger.LogInformation($"Admin (add contact): contacts for [{worker.Name}] is not added");
    }

    private void DeleteContact(Dictionary<string, string> data)
    {
        var worker = _db.Context.Workers.Where(w => w.Nickname == data.Key("nickname")).FirstOrDefault();
        if (worker is null)
        {
            _logger.LogInformation($"Admin (delete contact): Worker with nickname [{data.Key("nickname")}] does not exist");
            return;
        }

        var info = data.Key("info");
        var contact = _db.Context.Contacts.Where(c => c.Worker == worker && c.Info == info).FirstOrDefault();
        if (contact is not null)
        {
            _db.Context.Contacts.Remove(contact);
            _logger.LogInformation($"Admin (delete contact): [{contact.Info}] from [{worker.Nickname}] is deleted");
        }
        else
            _logger.LogInformation($"Admin (delete contact): [{data.Key("info")}] from [{worker.Nickname}] is not exist");
    }

    private async Task CompleteSaleAsync(Dictionary<string, string> data)
    {
        var saleId = data.Key("sale_id").AsInt(); /// почему проходит ок? при несуществ
        if (saleId is not null)
        {
            await _db.СompleteAsync(saleId.Value);
            _logger.LogInformation($"Admin (complete sale): Sale ID – [{saleId.Value}] is complete");
        }
        else
            _logger.LogInformation($"Admin (complete sale): wrong sale id");
    }
}