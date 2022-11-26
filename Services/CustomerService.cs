using System.Text;
using AutoMapper;
using FluentValidation;
using FluentValidation.Results;
using Hangfire;
using Microsoft.EntityFrameworkCore;
// ReSharper disable MemberCanBePrivate.Global

namespace Syracuse;

public interface ICustomerService
{
    public Task HandleTildaAsync(Dictionary<string, string> data);
    public Task HandleYandexAsync(Dictionary<string, string> data);
}

public class CustomerService : ICustomerService
{
    private static readonly Func<string, string> s_name = Path.GetFileNameWithoutExtension;
    private static readonly Func<string> s_newName = Path.GetRandomFileName;
    private readonly IValidator<Agenda> _agendaValidator;
    private readonly IValidator<Client> _clientValidator;
    private readonly IDbService _db;

    private readonly ILogger<CustomerService> _logger;
    private readonly IMailService _mail;
    private readonly IMapper _mapper;
    private readonly IPdfService _pdf;

    static CustomerService() => RecurringJob.AddOrUpdate("health-check", () => HealthCheck(), Cron.Hourly);

    public CustomerService(ILogger<CustomerService> logger, IMapper mapper, IValidator<Client> clientValidator,
        IValidator<Agenda> agendaValidator, IMailService mailSender, IPdfService pdfCreator, IDbService db)
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
        Task? handler = formname switch
        {
            "beginner" => HandleBegginerFormAsync(data),
            "profi" => HandleProfiFormAsync(data),
            "online-coach" => HandleCoachFormAsync(data),
            "standart" => HandleStandartFormAsync(data),
            "pro" => HandleProFormAsync(data),
            "posing" => HandlePosingFormAsync(data),
            "endo" => HandleEndoFormAsync(data),

            "add-worker" => AddWorker(data),
            "edit-worker" => EditWorker(data),
            "delete-worker" => DeleteWorker(data),
            "add-contact" => AddContact(data),
            "delete-contact" => DeleteContact(data),

            "add-or-update-product" => AddOrUpdateProduct(data),
            "add-or-update-content" => AddOrUpdateContent(data),

            "complete-sale" => CompleteSaleAsync(data),

            "load-recepies" => LoadRecepiesAsync(data),
            "load-instructions" => LoadInstructionsAsync(data),
            "add-workout-program" => AddWorkoutProgramAsync(data),

            _ => throw new CustomerExсeption("Пришла форма с некорректным идентификатором")
        };

        _logger.LogInformation($"Trying to handle: [{formname}] as Tilda");
        await handler;
    }

    public async Task HandleYandexAsync(Dictionary<string, string> data)
    {
        var type = data.Key("type");
        Task? handler = type switch
        {
            "begginer" => HandleBegginerFormAsync(data),
            "profi" => HandleProfiFormAsync(data),
            "online-coach" => HandleCoachFormAsync(data),
            "standart" => HandleStandartFormAsync(data),
            "pro" => HandleProFormAsync(data),

            _ => throw new CustomerExсeption("Пришла форма с некорректным идентификатором")
        };

        _logger.LogInformation($"Trying to handle: [{type}] as Yandex");
        await handler;
    }

    public static async Task HealthCheck()
    {
        await using AsyncServiceScope? scope = ServiceActivator.GetAsyncScope();
        var mail = scope?.ServiceProvider.GetService<IMailService>();
        var db = scope?.ServiceProvider.GetService<IDbService>();
        var logger = scope?.ServiceProvider.GetService<ILogger<CustomerService>>();

        foreach (Sale? nonDoneSale in db!.Context.Sales.Include(s => s.Client).Include(s => s.Agenda)
                     .Include(s => s.Product).Include(s => s.WorkoutProgram)
                     .Where(s => !s.IsDone && s.IsErrorHandled == null).ToList())
            try
            {
                if (nonDoneSale.OrderId == default)
                {
                    nonDoneSale.OrderId = -1;
                    throw new CustomerExсeption(
                        "Некорректная обработка продажи: отсутсвует внешний номер заказа, проверьте факт оплаты в банке");
                }

                if (nonDoneSale.Product?.Count is 0)
                    throw new CustomerExсeption("Некорректная обработка продажи: отсутствуют связанные продукты");

                if (!nonDoneSale.IsSuccessEmailSent)
                {
                    await ForwardSuccessEmailAsync(nonDoneSale, mail, db, logger);
                    nonDoneSale.IsSuccessEmailSent = true;
                }

                switch (nonDoneSale.Type)
                {
                    case SaleType.Beginner or SaleType.Profi when nonDoneSale.WorkoutProgram is null && string.IsNullOrWhiteSpace(nonDoneSale.Key) && nonDoneSale.IsAdminNotified:
                        nonDoneSale.IsAdminNotified = false;
                        break;
                    case SaleType.Standart or SaleType.Pro when string.IsNullOrWhiteSpace(nonDoneSale.Nutrition):
                        AttachNutrition(nonDoneSale, db, scope?.ServiceProvider.GetService<PdfService>(), logger);
                        break;
                }

                if (string.IsNullOrWhiteSpace(nonDoneSale.Key)
                    && (nonDoneSale.ScheduledDeliverTime is null ||
                        (nonDoneSale.ScheduledDeliverTime is { } scheduled &&
                         scheduled < DateTime.UtcNow.AddMinutes(5d)))
                    && ((nonDoneSale.Type is SaleType.Beginner or SaleType.Profi &&
                         nonDoneSale.WorkoutProgram is not null)
                        || (nonDoneSale.Type is SaleType.Standart or SaleType.Pro &&
                            !string.IsNullOrWhiteSpace(nonDoneSale.Nutrition))))
                {
                    MailService.MailContent? content = nonDoneSale.Type switch
                    {
                        SaleType.Beginner or SaleType.Pro => CreateMailContentForWp(nonDoneSale),
                        SaleType.Standart or SaleType.Pro => CreateMailContentForNutrition(nonDoneSale),
                        _ => throw new ArgumentException()
                    };
                    TimeSpan delay = ScheduleHelper.GetSchedule();
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
                logger.LogError(ex, "Health check was not passed because of Customer Ex");
                await ErrorNotify(nonDoneSale, ex.Message);
            }
            catch (MailExсeption ex)
            {
                logger.LogError(ex, "Health check was not passed because of Mail Ex");
                await ErrorNotify(nonDoneSale, ex.Message);
            }
            catch (DbExсeption ex)
            {
                logger.LogError(ex, "Health check was not passed because of Db Ex");
                await ErrorNotify(nonDoneSale, ex.Message);
            }
            catch (BackgroundJobClientException ex)
            {
                logger.LogError(ex, "Health check was not passed because of Schedule Ex");
                await ErrorNotify(nonDoneSale,
                    "Ошибка при попытке запланировать отложенную отправку сообщения клиенту");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Health check was not passed");
                await ErrorNotify(nonDoneSale, "Ошибка не указана");
            }
            finally
            {
                nonDoneSale.IsErrorHandled = true;
                logger.LogInformation(
                    $"Health checked for [{nonDoneSale.Id} {nonDoneSale.Type.ToString()} by {nonDoneSale.Client.Email}]");
            }

        async Task ErrorNotify(Sale sale, string exMessage)
        {
            Dictionary<string?, string?>? gettersInfo = MatchHelper.TransformToValues(sale);
            gettersInfo["key"] = KeyHelper.UniversalKey;
            var link = UrlHelper.MakeLink(sale.Type, gettersInfo);
            logger!.LogInformation(
                $"Admin (health check of [{sale.Type}]): ReInput link for [{sale.Client.Email}] is [{link}]");

            var message =
                $"Внимание! Произошла ошибка:<br/><i>{exMessage}</i><br/><br/>{LogHelper.ClientInfo(sale.Client, sale.Agenda).Replace("\n", "<br/>")}<br/>Пожалуйста, перезаполните данные клиента по этой ссылке: {link}";
            await mail!.SendMailAsync(MailType.Failure, await db!.GetInnerEmailsAsync(admins: true),
                "Обратите внимание: некорректная обработка продажи", message);
        }
    }
    // -------------------------------------------------------------------------------

    private async Task HandleWorkoutProgramAsync(Dictionary<string, string> data, SaleType saleType)
    {
        Map(saleType, data, out Client? client, out Agenda? agenda);
        if (!await Validate(saleType, data, client, agenda))
            return;

        var key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        Sale? sale = await _db.AddSaleAsync(client!, agenda, saleType,
            DateTime.UtcNow,
            false,
            false, key);
        sale.OrderId = sale.OrderId == -1 ? sale.OrderId : int.Parse(data["orderid"]);
        sale.Product = new List<Product>
            { _db.Context.Products.Single(p => p.Code == saleType.ToString().ToLower()) };

        await ForwardSuccessEmailAsync(sale, _mail, _db, _logger);
        sale.IsSuccessEmailSent = true;

        if (await _db.FindWorkoutProgramAsync(sale.Agenda!) is { } wp)
        {
            sale.WorkoutProgram = wp;

            MailService.MailContent? content = CreateMailContentForWp(sale);
            TimeSpan delay = ScheduleHelper.GetSchedule();
            BackgroundJob.Schedule(() => ForwardMail(content, sale), delay);
            sale.ScheduledDeliverTime = DateTime.UtcNow + delay;

            _logger.LogInformation(
                $"Client (wp): wp for {sale.Client.Email} is exit. It's {sale.WorkoutProgram.Id} : {sale.WorkoutProgram.ProgramPath}");
        }

        await NotifyAdminsAsync(sale, _mail, _db, _logger);
        sale.IsAdminNotified = true;
    }

    public static MailService.MailContent CreateMailContentForWp(Sale sale)
    {
        var subject = sale.Type is SaleType.Beginner ? "Begginer: готовая программа" : "Profi: готовая программа";
        var message = "Ваша персональная программа тренировок готова! Она прикреплена к данному сообщению.";
        var wpFile =
            new MailService.FilePath("Персональная программа тренировок.pdf", sale.WorkoutProgram!.ProgramPath);
        var content = new MailService.MailContent(MailType.Success, sale.Client.Email, sale.Client.Name, subject,
            message, new[] { wpFile });
        return content;
    }

    public async Task HandleNutritionAsync(Dictionary<string, string> data, SaleType saleType)
    {
        Map(saleType, data, out Client? client, out Agenda? agenda);
        if (!await Validate(saleType, data, client, agenda))
            return;

        var key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        Sale? sale = await _db.AddSaleAsync(client!, agenda, saleType,
            DateTime.UtcNow,
            false,
            false, key);
        sale.OrderId = sale.OrderId == -1 ? sale.OrderId : int.Parse(data["orderid"]);
        sale.Product = new List<Product>
            { _db.Context.Products.Single(p => p.Code == saleType.ToString().ToLower()) };

        if (string.IsNullOrWhiteSpace(sale.Nutrition))
            AttachNutrition(sale, _db, _pdf, _logger);

        await ForwardSuccessEmailAsync(sale, _mail, _db, _logger);
        sale.IsSuccessEmailSent = true;

        MailService.MailContent? content = CreateMailContentForNutrition(sale);
        TimeSpan delay = ScheduleHelper.GetSchedule();
        BackgroundJob.Schedule(() => ForwardMail(content, sale), delay);
        sale.ScheduledDeliverTime = DateTime.UtcNow + delay;

        await NotifyAdminsAsync(sale, _mail, _db, _logger);
        sale.IsAdminNotified = true;
    }

    public static void AttachNutrition(Sale sale, IDbService db, IPdfService pdf, ILogger<CustomerService> logger)
    {
        Cpfc? cpfc = NutritionHelper.CalculateCpfc(sale.Agenda!);
        Diet? diet = NutritionHelper.CalculateDiet(cpfc, sale.Agenda.Gender!);
        var path = Path.Combine("Resources", "Produced", "Nutritions", s_newName());
        pdf.CreateNutrition(path, sale.Type, sale.Agenda, cpfc, diet);

        sale.Nutrition = path;
        db.Context.Sales.Update(sale);
        logger.LogInformation($"Client (nutrition): nutrition {s_name(path)} for {sale.Client.Email} calculated");
    }

    public static MailService.MailContent CreateMailContentForNutrition(Sale sale)
    {
        var nutrition = new MailService.FilePath("КБЖУ и рацион.pdf", sale.Nutrition!);
        var recepies =
            new MailService.FilePath("Книга рецептов.pdf", Path.Combine("Resources", "Produced", "Recepies.pdf"));
        var instructions =
            new MailService.FilePath("Инструкции.pdf", Path.Combine("Resources", "Produced", "Instructions.pdf"));

        switch (sale.Type)
        {
            case SaleType.Standart:
                var msgStandart =
                    "К данному письму приложен PDF документ,в котором находится КБЖУ и примерный рацион питания.";
                var contentStandart = new MailService.MailContent(MailType.Success, sale.Client.Email, sale.Client.Name,
                    "Standart питание: КБЖУ + рацион", msgStandart, new[] { nutrition, instructions });
                return contentStandart;
            case SaleType.Pro:
                var msgPro =
                    "К данному письму приложено два PDF документа. В одном из них находится КБЖУ и примерный рацион питания. В другом – рецепты.";
                var contentPro = new MailService.MailContent(MailType.Success, sale.Client.Email, sale.Client.Name,
                    "PRO питание: КБЖУ + рацион + книга рецептов", msgPro, new[] { nutrition, instructions, recepies });
                return contentPro;
            default:
                throw new ArgumentException();
        }
    }

    // --------------------------------------------------------------------------------

    private async Task HandleValidationErrorAsync(ValidationException ex, Dictionary<string, string> data,
        SaleType saleType, Client client, Agenda? agenda)
    {
        var key = data.Key("key");
        if (string.Equals(key, KeyHelper.UniversalKey)) return;
        Sale sale;

        if (string.IsNullOrWhiteSpace(key))
        {
            key = KeyHelper.NewKey();
            sale = await _db.AddSaleAsync(client, agenda, saleType,
                DateTime.UtcNow,
                false,
                true, key);
            sale.OrderId = sale.OrderId == -1 ? sale.OrderId : int.Parse(data["orderid"]);
            sale.IsErrorHandled = false;
            _logger.LogInformation(
                $"Db (validation of [{saleType}]): [{client.Email}] with key [{key}] added data to db. Waiting new data.");
        }
        else
        {
            try
            {
                sale = _db.Context.Sales.Include(s => s.Agenda).Include(s => s.Client)
                    .Single(s => s.Key == key);
            }
            catch (Exception e)
            {
                throw new CustomerExсeption($"Отсутсвует запись о продажи с ключем [{key}]", e);
            }

            sale.Client.UpdateWith(client);
            if (agenda is not null) sale.Agenda!.UpdateWith(agenda);
            _logger.LogInformation(
                $"Db (validation again of [{saleType}]): [{client.Email}] with key [{key}] found in db. Waiting new data.");
        }

        // Формирование ссылки для перезаполнения
        Dictionary<string?, string?>? gettersInfo = MatchHelper.TransformToValues(sale);
        var link = UrlHelper.MakeLink(saleType, gettersInfo);
        _logger.LogInformation($"Client (validation of [{saleType}]): ReInput link for [{client.Email}] is [{link}]");

        // Формирование и отправка письма
        StringBuilder? sb = new StringBuilder()
            .AppendLine("При заполнении анкеты произошла ошибка:<br/>");
        foreach (ValidationFailure? error in ex.Errors)
            sb.AppendLine($"{error.ErrorMessage}<br/>");
        sb.AppendLine($"<br/>Пожалуйста, перейдите по ссылке и введите данные повторно!<br/><b>{link}</b>");
        await _mail.SendMailAsync(MailType.Failure, client.Email, client.Name, saleType.AsErrorTitle()!, sb.ToString());
        _logger.LogInformation($"Mail (validation of {saleType}): sent to {client.Email}");
    }

    // --------------------------------------------------------------------------------

    private async Task AddWorkoutProgramAsync(Dictionary<string, string> data)
    {
        var workoutProgram = _mapper.Map<WorkoutProgram>(data);
        workoutProgram.ProgramPath = Path.Combine("Resources", "Produced", "WorkoutPrograms", s_newName());
        _logger.LogInformation($"Admin (add wp): wp is mapped. Path is {workoutProgram.ProgramPath}");

        var base64String = data.Key("file");
        await Base64Helper.DecodeToPdf(base64String ?? string.Empty, workoutProgram.ProgramPath);
        await _db.AddWorkoutProgramAsync(workoutProgram);
        _logger.LogInformation($"Admin (add wp): wp ({s_name(workoutProgram.ProgramPath)}) is loaded and added to db");

        await AttachAndForwardWpAsync(_db, _mail, _logger);
    }

    // --------------------------------------------------------------------------------

    private async Task LoadRecepiesAsync(Dictionary<string, string> data)
    {
        var path = Path.Combine("Resources", "Produced", "Recepies.pdf");
        var base64String = data.Key("file");
        if (string.IsNullOrEmpty(base64String)) return;
        await Base64Helper.DecodeToPdf(base64String, path);
        _logger.LogInformation("Admin (load recepies): recepies is loaded.");
    }

    private async Task LoadInstructionsAsync(Dictionary<string, string> data)
    {
        var path = Path.Combine("Resources", "Produced", "Instructions.pdf");
        var base64String = data.Key("file");
        if (string.IsNullOrEmpty(base64String)) return;
        await Base64Helper.DecodeToPdf(base64String, path);
        _logger.LogInformation("Admin (load Instructions): Instructions is loaded.");
    }

    // --------------------------------------------------------------------------------

    private Task AddOrUpdateProduct(Dictionary<string, string> data)
    {
        if (_db.Context.Products.SingleOrDefault(p => p.Code == data.Key("code")) is { } productToEdit)
        {
            productToEdit.Label = data.Key("label") ?? productToEdit.Label;
            productToEdit.Price = data.Key("price").AsInt() ?? productToEdit.Price;
            _db.Context.Products.Update(productToEdit);
            _logger.LogInformation($"Admin (add or update product): {productToEdit.Code} is updated");
        }
        else if (data.Key("code") is { } code)
        {
            var product = new Product
                { Code = code, Label = data.Key("label") ?? string.Empty, Price = data.Key("price").AsInt() ?? 0 };
            _db.Context.Products.Add(product);
        }

        return Task.CompletedTask;
    }

    private Task AddOrUpdateContent(Dictionary<string, string> data)
    {
        if (_db.Context.Products.Include(p => p.Childs).Include(p => p.Parents)
                .SingleOrDefault(p => p.Code == data.Key("code")) is { } productToEdit)
        {
            productToEdit.Content = data.Key("content")?.Trim();
            if (productToEdit.Childs?.Count is 0 && data.Key("child-of") is { } childOf)
            {
                var parentCode = childOf[1..];
                Product? parent = _db.Context.Products.Include(p => p.Childs).Single(p => p.Code == parentCode);

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
            {
                _logger.LogInformation("Admin (add woker): worker is not added");
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Admin (add woker): error. worker is not added");
        }

        return Task.CompletedTask;
    }

    private Task EditWorker(Dictionary<string, string> data)
    {
        if (_db.Context.Workers.FirstOrDefault(w => w.Nickname == data.Key("nickname")) is { } workerToEdit)
        {
            var worker = _mapper.Map<Worker>(data);
            workerToEdit.Admin = worker.Admin;
            workerToEdit.Name = worker.Name;
            _db.Context.Workers.Update(workerToEdit);
            _logger.LogInformation($"Admin (edit worker): [{workerToEdit.Name}] is edited");
        }
        else
        {
            _logger.LogInformation($"Admin (edit worker): [{data.Key("nickname")}] is not exist");
        }

        return Task.CompletedTask;
    }

    private Task DeleteWorker(Dictionary<string, string> data)
    {
        if (_db.Context.Workers
                .FirstOrDefault(w => w.Nickname == data.Key("nickname")) is { } workerToDelete)
        {
            _db.Context.Workers.Remove(workerToDelete);
            _logger.LogInformation($"Admin (delete worker): [{workerToDelete.Name}] is deleted");
        }
        else
        {
            _logger.LogInformation($"Admin (delete worker): [{data.Key("nickname")}] is not exist");
        }

        return Task.CompletedTask;
    }

    private Task AddContact(Dictionary<string, string> data)
    {
        if (_db.Context.Workers.FirstOrDefault(w => w.Nickname == data.Key("nickname")) is { } worker)
        {
            var contact = _mapper.Map<Contact>(data);
            if (!string.IsNullOrWhiteSpace(contact.Info))
            {
                contact.Worker = worker;
                _db.Context.Contacts.Add(contact);
                _logger.LogInformation(
                    $"Admin (add contact): [{worker.Name}] has new [{contact.Type}] is [{contact.Info}]");
            }
            else
            {
                _logger.LogInformation($"Admin (add contact): contacts for [{worker.Name}] is not added");
            }
        }
        else
        {
            _logger.LogInformation(
                $"Admin (add contact): Worker with nickname [{data.Key("nickname")}] does not exist");
        }

        return Task.CompletedTask;
    }

    private Task DeleteContact(Dictionary<string, string> data)
    {
        if (_db.Context.Workers.FirstOrDefault(w => w.Nickname == data.Key("nickname")) is { } worker)
        {
            var info = data.Key("info");
            if (_db.Context.Contacts.FirstOrDefault(c => c.Worker == worker && c.Info == info) is { } contact)
            {
                _db.Context.Contacts.Remove(contact);
                _logger.LogInformation($"Admin (delete contact): [{contact.Info}] from [{worker.Nickname}] is deleted");
            }
            else
            {
                _logger.LogInformation(
                    $"Admin (delete contact): [{data.Key("info")}] from [{worker.Nickname}] is not exist");
            }
        }
        else
        {
            _logger.LogInformation(
                $"Admin (delete contact): Worker with nickname [{data.Key("nickname")}] does not exist");
        }

        return Task.CompletedTask;
    }

    private async Task CompleteSaleAsync(Dictionary<string, string> data)
    {
        if (data.Key("sale-id").AsInt() is { } saleId)
        {
            await _db.СompleteAsync(saleId);
            _logger.LogInformation($"Admin (complete sale): Sale ID – [{saleId}] is complete");
        }
        else
        {
            _logger.LogInformation("Admin (complete sale): wrong sale id");
        }
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
                _logger.LogInformation(
                    $"Client ({type.ToString()}): {client.Name} {client.Phone} {client.Email} is mapped");
                break;
            case SaleType.Posing:
            case SaleType.Endo:
                client = _mapper.Map<Dictionary<string, string>, Client>(data);
                agenda = null;
                _logger.LogInformation(
                    $"Client ({type.ToString()}): {client.Name} {client.Phone} {client.Email} is mapped");
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
                ((AgendaValidator)_agendaValidator).SaleType = type;
                await _agendaValidator.ValidateAndThrowAsync(agenda);
            }

            if (client is not null)
                await _clientValidator.ValidateAndThrowAsync(client);

            _logger.LogInformation(
                $"Client ({type.ToString()}): {client!.Name} {client.Phone} {client.Email} is validated");
            return true;
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex,
                $"Client ({type.ToString()}): {client!.Name} {client.Phone} {client.Email} – validation fault");
            await HandleValidationErrorAsync(ex, data, type, client, agenda);
            return false;
        }
    }

    // -----------------------

    public static async Task ForwardMail(MailService.MailContent content, Sale sale)
    {
        await using AsyncServiceScope? scope = ServiceActivator.GetAsyncScope();
        var mail = scope?.ServiceProvider.GetService<IMailService>();
        var db = scope?.ServiceProvider.GetService<IDbService>();
        var logger = scope?.ServiceProvider.GetService<ILogger<CustomerService>>();

        try
        {
            await mail!.SendMailAsync(content);
            await db!.СompleteAsync(sale.Id);
            logger!.LogInformation(
                $"Mail({sale.Type.ToString()}): Mail was sent to client [{sale.Client.Email}] with sale ID [{sale.Id}]");
        }
        catch (Exception ex)
        {
            logger!.LogError(ex,
                $"Can't send delayd mail to [{sale.Client.Email}] with sale ID [{sale.Id}]. Hope for the best");
        }
    }

    // -----------------------

    public static async Task AttachAndForwardWpAsync(IDbService db, IMailService mail, ILogger<CustomerService> logger)
    {
        List<Sale>? awaitingCustomers = db.Context.Sales
            .Include(s => s.Client).Include(s => s.Agenda).Include(s => s.WorkoutProgram)
            .Where(s => (s.Type == SaleType.Beginner || s.Type == SaleType.Profi) && !s.IsDone)
            .ToList();

        foreach (Sale? sale in awaitingCustomers)
            if (sale.WorkoutProgram is null && await db.FindWorkoutProgramAsync(sale.Agenda!) is { } wp)
            {
                sale.WorkoutProgram = wp;
                db.Context.Sales.Update(sale);
                logger.LogInformation($"AttachAndForwardWp: wp [{wp.Id}] for {sale.Client.Email} found and attached");

                MailService.MailContent? content = CreateMailContentForWp(sale);
                await mail.SendMailAsync(content);
                await db.СompleteAsync(sale.Id);
            }
    }

    public static async Task ForwardSuccessEmailAsync(Sale sale, IMailService mail, IDbService db,
        ILogger<CustomerService> logger)
    {
        switch (sale.Type)
        {
            case SaleType.Beginner:
                var messageBegginer =
                    "Благодарим за покупку программы тренировок «Begginer»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам персональную программу тренировок.";
                await mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name,
                    "Персональная программа тренировок «Begginer»", messageBegginer);
                logger.LogInformation($"Mail (wp-begginer-successful): sent to {sale.Client.Email}");
                break;
            case SaleType.Profi:
                var messageProfi =
                    "Благодарим за покупку программы тренировок «Profi»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам персональную программу тренировок.";
                await mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name,
                    "Персональная программа тренировок «Profi»", messageProfi);
                logger.LogInformation($"Mail (wp-profi-successful): sent to {sale.Client.Email}");
                break;
            case SaleType.Standart:
                var messageStandart =
                    "Благодарим за покупку программы питания «Standart»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам вашу персональную программу питания.";
                await mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name,
                    "Standart питание: КБЖУ + рацион", messageStandart);
                logger.LogInformation($"Mail (nutrition-standart-successful): sent to {sale.Client.Email}");
                break;
            case SaleType.Pro:
                var messagePro =
                    "Благодарим за покупку программы питания «Pro»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам вашу персональную программу питания и рецепты.";
                await mail.SendMailAsync(MailType.Awaiting, sale.Client.Email, sale.Client.Name,
                    "PRO питание: КБЖУ + рацион + книга рецептов", messagePro);
                logger.LogInformation($"Mail (nutrition-pro-successful): sent to {sale.Client.Email}");
                break;
            case SaleType.Coach:
                var messageCoach =
                    $"В течение следующего дня (в выходные может потребоваться больше времени) наш тренер свяжется с вами для начала тренировок.<br/><br/><b>Контакты тренера:</b><br/>{await db.GetCoachContactsAsync(sale.Agenda?.Trainer ?? string.Empty) ?? "<i>не указаны</i>"}";
                await mail.SendMailAsync(MailType.Success, sale.Client.Email, sale.Client.Name,
                    "Занятия с Online-тренером", messageCoach);
                logger.LogInformation($"Mail (coach): sent to {sale.Client.Email}");
                break;
            case SaleType.Endo:
                var messageEndo =
                    "Благодарим за покупку! В течение следующего дня (в выходные может потребоваться больше времени) <i>наш эндокринолог</i> свяжется с вами для консультации.";
                await mail.SendMailAsync(MailType.Success, sale.Client.Email, sale.Client.Name,
                    "Консультация эндокринолога", messageEndo);
                logger.LogInformation($"Mail (endo): sent to {sale.Client.Email}");
                break;
            case SaleType.Posing:
                var sb = new StringBuilder();
                foreach (Product? video in sale.Product ?? Enumerable.Empty<Product>())
                    if (video.Childs is { Count: > 0 } children)
                    {
                        sb.Append($"<b>{video.Label}</b><br/><br/>");
                        foreach (Product? child in children) sb.Append(VideoList(child));
                    }
                    else
                    {
                        sb.Append(VideoList(video));
                    }

                var messagePosing =
                    $"Благодарим за покупку! Ниже Вы можете увидеть ссылки на <i>приватные</i> видео-инструкции:<br/><br/>{sb}";
                await mail.SendMailAsync(MailType.Success, sale.Client.Email, sale.Client.Name,
                    "Уроки позинга Fitness Bikini", messagePosing);
                logger.LogInformation($"Mail (posing): sent to {sale.Client.Email}");
                break;
            default:
                throw new ArgumentException();
        }

        string VideoList(Product video)
        {
            var sb = new StringBuilder();
            sb.Append($"<b><i>{video.Label}</i></b><br/>");
            if (video.Content!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is
                { Length: > 0 } links)
                foreach (var link in links)
                    sb.Append(" > " + link + "<br/>");
            sb.Append("<br/>");
            return sb.ToString();
        }
    }

    public static async Task NotifyAdminsAsync(Sale sale, IMailService mail, IDbService db,
        ILogger<CustomerService> logger)
    {
        switch (sale.Type)
        {
            case SaleType.Beginner:
            case SaleType.Profi:
                if (sale.WorkoutProgram is null)
                {
                    sale.Key = KeyHelper.NewKey();
                    logger.LogInformation($"Client (wp): wp for {sale.Client.Email} doesnt exit");
                    Dictionary<string?, string?>? gettersInfo =
                        MatchHelper.TransformToValues(sale, SaleType.WorkoutProgram);
                    var link = UrlHelper.MakeLink(SaleType.WorkoutProgram.AsReinputLink()!, gettersInfo);
                    logger.LogInformation($"Client (wp): New wp link for admins is {link}");

                    var clientInfo = LogHelper.ClientInfo(sale.Client, sale.Agenda);
                    clientInfo = clientInfo.Replace("\n", "<br/>");
                    (string email, string name)[]? emails = await db.GetInnerEmailsAsync(admins: true);
                    var messageWp =
                        $"Необходимо составить новую программу тренировок для:<br/><br/>{clientInfo}<br/>Перейдите для этого по ссылке: <i>{link}</i>";
                    await mail.SendMailAsync(MailType.Inner, emails, "Запрос на новую программу тренировок", messageWp);
                }
                else
                {
                    var messageWp =
                        $"Покупка готовой программы тренировок {sale.Type.ToString()}.<br/><br/>{LogHelper.ClientInfo(sale.Client).Replace("\n", "<br/>")}";
                    await mail.SendMailAsync(MailType.Inner, await db.GetInnerEmailsAsync(admins: true),
                        "Покупка готовой программы тренировок", messageWp);
                    logger.LogInformation(
                        $"Mail ({sale.Type.ToString()}): info about {sale.Client.Email} is sent to admins");
                }

                break;
            case SaleType.Standart:
            case SaleType.Pro:
                var messageNut =
                    $"Покупка сгенерированной программы питания {sale.Type.ToString()}.<br/><br/>{LogHelper.ClientInfo(sale.Client).Replace("\n", "<br/>")}";
                await mail.SendMailAsync(MailType.Inner, await db.GetInnerEmailsAsync(admins: true),
                    "Покупка программы питания", messageNut);
                logger.LogInformation(
                    $"Mail ({sale.Type.ToString()}): info about {sale.Client.Email} is sent to admins");
                break;
            case SaleType.Coach:
                var messageCoach = "Новый клиент на онлайн-тренировки:<br/><br/>" +
                                   LogHelper.ClientInfo(sale.Client, sale.Agenda).Replace("\n", "<br/>") +
                                   "<br/>Пожалуйста, свяжитесь с ним/ней";
                await mail.SendMailAsync(MailType.Inner,
                    await db.GetInnerEmailsAsync(new[] { sale.Agenda!.Trainer }, true),
                    "Занятия с Online-тренером: новый клиент", messageCoach);
                logger.LogInformation($"Mail (coach): info about {sale.Client.Email} is sent to admins");
                break;
            case SaleType.Endo:
                var messageEndo =
                    $"Новый клиент на консультацию эндокринолога:<br/><br/>{LogHelper.ClientInfo(sale.Client).Replace("\n", "<br/>")}<br/>Пожалуйста, свяжитесь с ним/ней";
                await mail.SendMailAsync(MailType.Inner, await db.GetInnerEmailsAsync(admins: true),
                    "Консультация эндокринолога: новый клиент", messageEndo);
                logger.LogInformation($"Mail (endo): info about {sale.Client.Email} is sent to admins");
                break;
            case SaleType.Posing:
                var messagePosing =
                    $"Покупка видео-уроков:<br/><br/>{LogHelper.ClientInfo(sale.Client).Replace("\n", "<br/>")}";
                await mail.SendMailAsync(MailType.Inner, await db.GetInnerEmailsAsync(admins: true),
                    "Покупка видео-уроков", messagePosing);
                logger.LogInformation($"Mail (endo): info about {sale.Client.Email} is sent to admins");
                break;
            default:
                throw new ArgumentException();
        }
    }

    #endregion BaseActions

    // --------------------------------------------------------------------------------

    #region TildaHandlersMain

    private async Task HandleBegginerFormAsync(Dictionary<string, string> data)
    {
        await HandleWorkoutProgramAsync(data, SaleType.Beginner);
    }

    private async Task HandleProfiFormAsync(Dictionary<string, string> data)
    {
        await HandleWorkoutProgramAsync(data, SaleType.Profi);
    }

    private async Task HandleStandartFormAsync(Dictionary<string, string> data)
    {
        await HandleNutritionAsync(data, SaleType.Standart);
    }

    private async Task HandleProFormAsync(Dictionary<string, string> data)
    {
        await HandleNutritionAsync(data, SaleType.Pro);
    }

    private async Task HandlePosingFormAsync(Dictionary<string, string> data)
    {
        Map(SaleType.Endo, data, out Client? client, out _);

        Sale? sale = await _db.AddSaleAsync(client ?? throw new InvalidOperationException(), null, SaleType.Posing, DateTime.UtcNow, false);
        sale.OrderId = sale.OrderId == -1 ? sale.OrderId : int.Parse(data["orderid"]);
        sale.Product = new List<Product>();
        foreach (var productLabel in data["videos"]
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            sale.Product.Add(_db.Context.Products.Where(p => p.Label == productLabel).Select(p => p).Single());

        await ForwardSuccessEmailAsync(sale, _mail, _db, _logger);
        sale.IsSuccessEmailSent = true;

        await NotifyAdminsAsync(sale, _mail, _db, _logger);
        sale.IsAdminNotified = true;

        sale.IsDone = true;
    }

    private async Task HandleEndoFormAsync(Dictionary<string, string> data)
    {
        Map(SaleType.Endo, data, out Client? client, out _);

        Sale? sale = await _db.AddSaleAsync(client ?? throw new InvalidOperationException(), null, SaleType.Endo, DateTime.UtcNow, false);
        sale.OrderId = sale.OrderId == -1 ? sale.OrderId : int.Parse(data["orderid"]);
        sale.Product = new List<Product> { _db.Context.Products.Single(p => p.Code == "endo") };

        await ForwardSuccessEmailAsync(sale, _mail, _db, _logger);
        sale.IsSuccessEmailSent = true;

        await NotifyAdminsAsync(sale, _mail, _db, _logger);
        sale.IsAdminNotified = true;

        sale.IsDone = true;
    }

    private async Task HandleCoachFormAsync(Dictionary<string, string> data)
    {
        Map(SaleType.Coach, data, out Client? client, out Agenda? agenda);
        if (!await Validate(SaleType.Coach, data, client, agenda))
            return;

        var key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        Sale? sale = await _db.AddSaleAsync(client!, agenda, SaleType.Coach,
            DateTime.UtcNow,
            false,
            false, key);
        sale.OrderId = sale.OrderId == -1 ? sale.OrderId : int.Parse(data["orderid"]);
        sale.Product = new List<Product> { _db.Context.Products.Single(p => p.Code == agenda.Trainer!) };

        await ForwardSuccessEmailAsync(sale, _mail, _db, _logger);
        sale.IsSuccessEmailSent = true;

        await NotifyAdminsAsync(sale, _mail, _db, _logger);
        sale.IsAdminNotified = true;

        sale.IsDone = true;
    }

    #endregion
}