using System.Text;
using AutoMapper;
using FluentValidation;
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
            "begginer" => HandleBegginerForm(data),
            "profi" => HandleProfiForm(data),
            "coach" => HandleCoachForm(data),
            "standart" => HandleStandartForm(data),
            "pro" => HandleProForm(data),
            "posing" => HandlePosingForm(data),
            "endo" => HandleEndoForm(data),

            "add_worker" => AddWorker(data),
            "delete_worker" => DeleteWorker(data),

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
            "begginer" => HandleBegginerForm(data),
            "profi" => HandleProfiForm(data),
            "coach" => HandleCoachForm(data),
            "standart" => HandleStandartForm(data),
            "pro" => HandleProForm(data),

            "add_workout_program" => AddWorkoutProgram(data),
            "load_recepies" => LoadRecepies(data),

            _ => throw new CustomerExсeption("Пришла форма с некорректным идентификатором"),
        };

        _logger.LogInformation($"Trying to handle: [{type}] as Yandex");
        await handler;
    }

    // --------------------------------------------------------------------------------
    #region TildaHandlersMain

    private async Task HandleBegginerForm(Dictionary<string, string> data) => await HandleWorkoutProgram(data, SaleType.Begginer);

    private async Task HandleProfiForm(Dictionary<string, string> data) => await HandleWorkoutProgram(data, SaleType.Profi);

    private async Task HandleStandartForm(Dictionary<string, string> data) => await HandleNutrition(data, SaleType.Standart);

    private async Task HandleProForm(Dictionary<string, string> data) => await HandleNutrition(data, SaleType.Pro);

    private async Task HandlePosingForm(Dictionary<string, string> data)
    {
        await Task.Yield();
    }

    private async Task HandleEndoForm(Dictionary<string, string> data)
    {
        var client = _mapper.Map<Dictionary<string, string>, Client>(data);
        _logger.LogInformation($"Client (endo): {client.Name} {client.Phone} {client.Email} is mapped");

        await _db.AddSaleAsync(client, null, SaleType.Endo, DateTime.UtcNow, true);

        var message1 = $"В течение следующего дня (в выходные может потребоваться больше времени) наш эндокринолог свяжется с вами для консультации";
        await _mail.SendMailAsync(MailType.Awaiting, (client.Email, client.Name), "Консультация эндокринолога", message1);
        _logger.LogInformation($"Mail (endo): sent to {client.Email}");

        var message2 = $"Новый клиент на консультацию эндокринолога: <br /> {LogHelper.ClientInfo(client).Replace("\n", "<br />")} <br /> Пожалуйста, свяжитесь с ним/ней";
        foreach (var worker in await _db.GetInnerEmailsAsync(admins: true))
            await _mail.SendMailAsync(MailType.Inner, worker, "Консультация эндокринолога: новый клиент", message2);
        _logger.LogInformation($"Mail (endo): info about {client.Email} is sent to admins");
    }

    private async Task HandleCoachForm(Dictionary<string, string> data)
    {
        /// Маппинг в унифицированный DTO
        var agenda = _mapper.Map<Dictionary<string, string>, Agenda>(data);
        var client = _mapper.Map<Dictionary<string, string>, Client>(data);
        _logger.LogInformation($"Client (coach): {client.Name} {client.Phone} {client.Email} is mapped");

        try
        {
            /// Валидация
            (_agendaValidator as AgendaValidator).SaleType = SaleType.Coach;
            await _agendaValidator.ValidateAndThrowAsync(agenda);
            await _clientValidator.ValidateAndThrowAsync(client);
            _logger.LogInformation($"Client (coach): {client.Name} {client.Phone} {client.Email} is validated");
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning($"Client (coach): {client.Name} {client.Phone} {client.Email} – validation fault");
            await HandleValidationError(ex, data, SaleType.Coach, client, agenda);
        }

        /// Добавление данных в БД: новых записей или обновление старых при наличие ключа
        string? key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        await _db.AddSaleAsync(client, agenda, SaleType.Coach,
            dateTime: DateTime.UtcNow,
            isDone: true,
            isNewKey: false,
            key: key);
        _logger.LogInformation($"Db (coach): {client.Email} with key [{key}] added data to db");

        /// Отправка сообщения клиенту
        var message1 = $"В течение следующего дня (в выходные может потребоваться больше времени) наш тренер свяжется с вами для начала тренировок. <br /> Контакты тренера:\n {await _db.GetTrainerContactsAsync(data.Key("trainer"))}";
        await _mail.SendMailAsync(MailType.Awaiting, (client.Email, client.Name), "Занятия с Online-тренером", message1);
        _logger.LogInformation($"Mail (coach): sent to {client.Email}");

        /// Отправка сообщений тренеру и администраторам
        var message2 = "Новый клиент на онлайн-тренировки: <br />" +
                        LogHelper.ClientInfo(client, agenda).Replace("\n", "<br />") +
                        "<br /> Пожалуйста, свяжитесь с ним/ней";
        foreach (var worker in await _db.GetInnerEmailsAsync(trainerNames: new[] { agenda.Trainer }, admins: true))
            await _mail.SendMailAsync(MailType.Inner, (worker.email, worker.name), "Занятия с Online-тренером: новый клиент", message2);
        _logger.LogInformation($"Mail (coach): info about {client.Email} is sent to admins");
    }

    #endregion
    // --------------------------------------------------------------------------------

    private async Task HandleWorkoutProgram(Dictionary<string, string> data, SaleType saleType)
    {
        var agenda = _mapper.Map<Dictionary<string, string>, Agenda>(data);
        var client = _mapper.Map<Dictionary<string, string>, Client>(data);
        _logger.LogInformation($"Client (workout): {client.Name} {client.Phone} {client.Email} is mapped");

        try
        {
            (_agendaValidator as AgendaValidator).SaleType = saleType;
            await _agendaValidator.ValidateAndThrowAsync(agenda);
            await _clientValidator.ValidateAndThrowAsync(client);
            _logger.LogInformation($"Client (workout): {client.Name} {client.Phone} {client.Email} is validated");
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning($"Client (workout): {client.Name} {client.Phone} {client.Email} – validation fault");
            await HandleValidationError(ex, data, saleType, client, agenda);
        }

        var key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        var sale = await _db.AddSaleAsync(client, agenda, saleType,
            dateTime: DateTime.UtcNow,
            isDone: false,
            isNewKey: false,
            key: key);
        _logger.LogInformation($"Db (workout): {client.Email} with key [{key}] added data to db");

        switch (saleType)
        {
            case SaleType.Begginer:
                var message1 = "Благодарим за покупку программы тренировок «Begginer»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам персональную программу тренировок.";
                await _mail.SendMailAsync(MailType.Awaiting, (client.Email, client.Name), "Персональная программа тренировок «Begginer»", message1);
                break;
            case SaleType.Profi:
                var message2 = "Благодарим за покупку программы тренировок «Profi»! В течение следующего дня (в выходные может потребоваться больше времени) наш тренер отправит вам персональную программу тренировок.";
                await _mail.SendMailAsync(MailType.Awaiting, (client.Email, client.Name), "Персональная программа тренировок «Profi»", message2);
                break;
        }
        _logger.LogInformation($"Mail (workout-prepare): sent to {client.Email}");

        await TransmitWorkoutProgram(sale);
    }

    private async Task HandleNutrition(Dictionary<string, string> data, SaleType saleType)
    {
        var agenda = _mapper.Map<Dictionary<string, string>, Agenda>(data);
        var client = _mapper.Map<Dictionary<string, string>, Client>(data);
        _logger.LogInformation($"Client (nutrition): {client.Name} {client.Phone} {client.Email} is mapped");

        try
        {
            (_agendaValidator as AgendaValidator).SaleType = saleType;
            await _agendaValidator.ValidateAndThrowAsync(agenda);
            await _clientValidator.ValidateAndThrowAsync(client);
            _logger.LogInformation($"Client (nutrition): {client.Name} {client.Phone} {client.Email} is validated");
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning($"Client (nutrition): {client.Name} {client.Phone} {client.Email} – validation fault");
            await HandleValidationError(ex, data, saleType, client, agenda);
        }

        string? key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        await _db.AddSaleAsync(client, agenda, saleType,
            dateTime: DateTime.UtcNow,
            isDone: true,
            isNewKey: false,
            key: key);
        _logger.LogInformation($"Db (nutrition): {client.Email} with key [{key}] added data to db");

        var cpfc = NutritionHelper.CalculateCpfc(agenda);
        var diet = NutritionHelper.CalculateDiet(cpfc);
        var path = Path.Combine("Resources", "Produced", "Nutritions", NewName());
        _pdf.CreateNutrition(path, agenda, cpfc, diet);
        _logger.LogInformation($"Client (nutrition): nutrition {Name(path)} for {client.Email} calculated");

        switch (saleType)
        {
            case SaleType.Standart:
                var message1 = "К данному письму приложен PDF документ,в котором находится КБЖУ и примерный рацион питания.";
                await _mail.SendMailAsync(MailType.Success, (client.Email, client.Name), "Standart питание. КБЖУ + рацион", message1,
                    ("КБЖУ и рацион.pdf", path));
                break;
            case SaleType.Pro:
                var recepiesPath = Path.Combine("Resources", "Produced", "Recepies.pdf");
                var message2 = "К данному письму приложено два PDF документа. В одном из них находится КБЖУ и примерный рацион питания. В другом – рецепты.";
                await _mail.SendMailAsync(MailType.Success, (client.Email, client.Name), "PRO питание + книга рецептов", message2,
                    ("КБЖУ и рацион.pdf", path), ("Книга рецептов.pdf", recepiesPath));
                break;
        }
        _logger.LogInformation($"Mail (nutrition): sent to {client.Email}");
    }

    // --------------------------------------------------------------------------------

    private async Task HandleValidationError(ValidationException ex, Dictionary<string, string> data, SaleType saleType, Client client, Agenda? agenda)
    {
        var key = data.Key("key") ?? KeyHelper.NewKey();
        var sale = await _db.AddSaleAsync(client, agenda, saleType,
            dateTime: DateTime.UtcNow,
            isDone: false,
            isNewKey: true,
            key: key);
        _logger.LogInformation($"Db (validation of {saleType}): {client.Email} with key [{key}] added data to db. Waiting new data.");

        var gettersInfo = MatchHelper.TransformToValues(sale);
        var link = UrlHelper.MakeLink(saleType, gettersInfo);
        _logger.LogInformation($"Client (validation of {saleType}): Reinput link for {client.Email} is {link}");

        var sb = new StringBuilder()
            .AppendLine("При заполнении анкеты произошла ошибка: <br />");
        foreach (var error in ex.Errors)
            sb.AppendLine($"{error.ErrorMessage} <br />");
        sb.AppendLine($"<br /> Пожалуйста, перейдите по ссылке и введите данные повторно! <br /><b>{link}</b>");
        await _mail.SendMailAsync(MailType.Failure, (client.Email, client.Name), saleType.AsErrorTitle(), sb.ToString());
        _logger.LogInformation($"Mail (validation of {saleType}): sent to {client.Email}");
    }

    private async Task TransmitWorkoutProgram(Sale sale)
    {
        var wp = await _db.FindWorkoutProgramAsync(sale.Id);

        if (wp is null)
        {
            _logger.LogInformation($"Client (wp): wp for {sale.Client.Email} doesnt exit");

            sale.Key = KeyHelper.NewKey();
            _db.Context.Sales.Update(sale);

            var gettersInfo = MatchHelper.TransformToValues(sale);
            var link = UrlHelper.MakeLink(SaleType.WorkoutProgram.AsReinputLink(), gettersInfo);
            _logger.LogInformation($"Client (wp): New wp link for admins is {link}");

            var clientInfo = LogHelper.ClientInfo(sale.Client, sale.Agenda);
            clientInfo = clientInfo.Replace("\n", "<br />");
            var emails = await _db.GetInnerEmailsAsync(admins: true);
            var title = "Запрос на новую программу тренировок";
            var message = $"Необходимо составить новую программу тренировок для:<br />{clientInfo}<br />Перейдите для этого по ссылке: {link}";

            foreach (var email in emails)
                await _mail.SendMailAsync(MailType.Inner, email, title, message);
            _logger.LogInformation($"Mail (wp): Wp link for admins is sent");
        }
        else
        {
            sale.WorkoutProgram = wp;
            _db.Context.Sales.Update(sale);
            _logger.LogInformation($"Client (wp): wp for {sale.Client.Email} is exit. It's {sale.WorkoutProgram.Id} : {sale.WorkoutProgram.ProgramPath}");

            ///await Task.Delay(10000); // Таймер для отправки
            await Task.Delay(1000);

            string subject = sale.Type is SaleType.Begginer ? "Begginer: готовая программа" : "Profi: готовая программа";
            string message = "Ваша персональная программа тренировок готова! Она прикреплена к данному сообщению.";
            await _mail.SendMailAsync(MailType.Success, (sale.Client.Name, sale.Client.Email), subject, message, ("Персональная программа тренировок.pdf", wp.ProgramPath));
            _logger.LogInformation($"Mail (wp): Wp sent to {sale.Client.Email} ({sale.Client.Phone})");

            await _db.СompleteAsync(sale.Id);
        }
    }

    private async Task AddWorkoutProgram(Dictionary<string, string> data)
    {
        var unique = data.Key("unique");
        var workoutProgram = _mapper.Map<WorkoutProgram>(data);
        _logger.LogInformation($"Admin (add wp): wp is mapped. Unique key is {unique}");


        var path = (await _db.FindWorkoutProgramAsync(workoutProgram)).ProgramPath ?? Path.Combine("Resources", "Produced", "WorkoutPrograms", NewName());
        await _mail.LoadAttachmentAsync(unique, path);
        workoutProgram.ProgramPath = path;
        await _db.AddWorkoutProgramAsync(workoutProgram);
        _logger.LogInformation($"Admin (add wp): wp ({Name(workoutProgram.ProgramPath)}) is loaded and added to db");

        Sale sale;
        var key = data.Key("key");
        if (!string.IsNullOrEmpty(key))
        {
            try
            {
                sale = (from s in _db.Context.Sales
                        where s.Key == key
                        select s).Single();
            }
            catch
            {
                _logger.LogWarning($"Admin (add wp): key {key} doesnt exits");
                throw new CustomerExсeption("Указан некорректный ключ при добавлении программы. Воспользуйтесь отправленной в письме ссылкой.");
            }

            sale.WorkoutProgram = workoutProgram;
            _db.Context.Sales.Update(sale);
            _logger.LogInformation($"Admin (add wp): sale ({sale.Id}) is updated");

            string subject = sale.Type is SaleType.Begginer ? "Begginer: готовая программа" : "Profi: готовая программа";
            string message = "Ваша персональная программа тренировок готова! Она прикреплена к данному сообщению.";
            await _mail.SendMailAsync(MailType.Success, (sale.Client.Name, sale.Client.Email), subject, message, ("Персональная программа тренировок.pdf", sale.WorkoutProgram.ProgramPath));
            _logger.LogInformation($"Mail client (add wp): wp is sent to {sale.Client.Name}");

            await _db.СompleteAsync(sale.Id);
        }
    }

    private async Task LoadRecepies(Dictionary<string, string> data)
    {
        var path = Path.Combine("Resources", "Produced", "Recepies.pdf");
        var unique = data.Key("unique");
        await _mail.LoadAttachmentAsync(unique, path);
        _logger.LogInformation($"Admin (load recepies): recepies is loaded. Unique key is {unique}");
    }

    private async Task AddWorker(Dictionary<string, string> data)
    {
        await Task.Run(() =>
        {
            var worker = _mapper.Map<Worker>(data);
            worker.Contacts = new();
            worker.Contacts.Add(new Contact() { Type = ContactType.Email, Info = data.Key("email"), Worker = worker });
            worker.Contacts.Add(new Contact() { Type = ContactType.Phone, Info = data.Key("phone"), Worker = worker });
            _db.Context.Workers.Add(worker);
            _logger.LogInformation($"Admin (add woker); {worker.Name} is added");
        });
    }

    private async Task DeleteWorker(Dictionary<string, string> data)
    {
        await Task.Run(() =>
        {
            var workerToDelete = (from worker in _db.Context.Workers
                                  where worker.Name == data.Key("name")
                                  select worker).FirstOrDefault();
            if (workerToDelete is not null)
            {
                _db.Context.Workers.Remove(workerToDelete);
                _logger.LogInformation($"Admin (delete worker): {workerToDelete.Name} is deleted");
            }
        });
    }
}

