using System.Text;

using AutoMapper;

using FluentValidation;

using Microsoft.EntityFrameworkCore.Metadata.Internal;

using ValidationException = FluentValidation.ValidationException;

namespace Syracuse;

public interface ICustomerService
{
    public Task HandleTildaAsync(Dictionary<string, string> data);
    public Task HandleYandexAsync(Dictionary<string, string> data);
}

public class CustomerService : ICustomerService
{
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
        var handler = data.Key("formname") switch
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

        await handler;
    }
    public async Task HandleYandexAsync(Dictionary<string, string> data)
    {
        var handler = data.Key("type") switch
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

        await _db.AddSale(client, null, SaleType.Endo, DateTime.UtcNow, true);

        var message1 = $"В течение следующего дня (в выходные может потребоваться больше времени) наш эндокринолог свяжется с вами для консультации";
        await _mail.SendMailAsync(MailType.Awaiting, (client.Email, client.Name), "Консультация эндокринолога", message1);

        var message2 = $"Новый клиент на консультацию эндокринолога: \n\n {LogHelper.ClientInfo(client)} \n\n Пожалуйста, свяжитесь с ним/ней";
        foreach (var worker in await _db.GetInnerEmails(admins: true))
            await _mail.SendMailAsync(MailType.Inner, worker, "Консультация эндокринолога: новый клиент", message2);
    }

    private async Task HandleCoachForm(Dictionary<string, string> data)
    {
        /// Маппинг в унифицированный DTO
        var agenda = _mapper.Map<Dictionary<string, string>, Agenda>(data);
        var client = _mapper.Map<Dictionary<string, string>, Client>(data);

        try
        {
            /// Валидация
            (_agendaValidator as AgendaValidator).SaleType = SaleType.Coach;
            await _agendaValidator.ValidateAndThrowAsync(agenda);
            await _clientValidator.ValidateAndThrowAsync(client);
        }
        catch (ValidationException ex)
        {
            await HandleValidationError(ex, data, SaleType.Coach, client, agenda);
        }

        /// Добавление данных в БД: новых записей или обновление старых при наличие ключа
        string? key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        await _db.AddSale(client, agenda, SaleType.Coach,
            dateTime: DateTime.UtcNow,
            isDone: true,
            isNewKey: false,
            key: key);

        /// Отправка сообщения клиенту
        var message1 = $"В течение следующего дня (в выходные может потребоваться больше времени) наш тренер свяжется с вами для начала тренировок. <br /> Контакты тренера:\n {await _db.GetTrainerContacts(data.Key("trainer"))}";
        await _mail.SendMailAsync(MailType.Awaiting, (client.Email, client.Name), "Занятия с Online-тренером", message1);

        /// Отправка сообщений тренеру и администраторам
        var message2 = $"Новый клиент на онлайн-тренировки: \n\n {LogHelper.ClientInfo(client, agenda)} \n\n Пожалуйста, свяжитесь с ним/ней";
        foreach (var worker in await _db.GetInnerEmails(trainerNames: new[] { agenda.Trainer }, admins: true))
            await _mail.SendMailAsync(MailType.Inner, (worker.email, worker.name), "Занятия с Online-тренером: новый клиент", message2);

        _logger.LogInformation($"Successful purchare [{SaleType.Coach}] by {client.Email} ({client.Phone})");
    }

    #endregion
    // --------------------------------------------------------------------------------

    private async Task HandleWorkoutProgram(Dictionary<string, string> data, SaleType saleType)
    {
        var agenda = _mapper.Map<Dictionary<string, string>, Agenda>(data);
        var client = _mapper.Map<Dictionary<string, string>, Client>(data);

        try
        {
            (_agendaValidator as AgendaValidator).SaleType = saleType;
            await _agendaValidator.ValidateAndThrowAsync(agenda);
            await _clientValidator.ValidateAndThrowAsync(client);
        }
        catch (ValidationException ex)
        {
            await HandleValidationError(ex, data, saleType, client, agenda);
        }

        var key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        var sale = await _db.AddSale(client, agenda, saleType,
            dateTime: DateTime.UtcNow,
            isDone: false,
            isNewKey: false,
            key: key);

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

        await TransmitWorkoutProgram(sale);

        _logger.LogInformation($"Successful purchare [{saleType}] by {client.Email} ({client.Phone})");
    }

    private async Task HandleNutrition(Dictionary<string, string> data, SaleType saleType)
    {
        var agenda = _mapper.Map<Dictionary<string, string>, Agenda>(data);
        var client = _mapper.Map<Dictionary<string, string>, Client>(data);

        try
        {
            (_agendaValidator as AgendaValidator).SaleType = saleType;
            await _agendaValidator.ValidateAndThrowAsync(agenda);
            await _clientValidator.ValidateAndThrowAsync(client);
        }
        catch (ValidationException ex)
        {
            await HandleValidationError(ex, data, saleType, client, agenda);
        }

        string? key = data.Key("key");
        key = key?.Equals(KeyHelper.UniversalKey) is true ? null : key;
        await _db.AddSale(client, agenda, saleType,
            dateTime: DateTime.UtcNow,
            isDone: true,
            isNewKey: false,
            key: key);

        var cpfc = NutritionHelper.CalculateCpfc(agenda);
        var diet = NutritionHelper.CalculateDiet(cpfc);
        var nutritionPath = _pdf.CreateNutrition(agenda, cpfc, diet);

        switch (saleType)
        {
            case SaleType.Standart:
                var message1 = "К данному письму приложен PDF документ,в котором находится КБЖУ и примерный рацион питания.";
                await _mail.SendMailAsync(MailType.Success, (client.Email, client.Name), "Standart питание. КБЖУ + рацион", message1,
                    ("КБЖУ и рацион.pdf", nutritionPath));
                break;
            case SaleType.Pro:
                var recepiesPath = Path.Combine("Resources", "Produced", "Recepies.pdf");
                var message2 = "К данному письму приложено два PDF документа. В одном из них находится КБЖУ и примерный рацион питания. В другом – рецепты.";
                await _mail.SendMailAsync(MailType.Success, (client.Email, client.Name), "PRO питание + книга рецептов", message2,
                    ("КБЖУ и рацион.pdf", nutritionPath), ("Книга рецептов.pdf", recepiesPath));
                break;
        }

        _logger.LogInformation($"Successful purchare [{saleType}] by {client.Email} ({client.Phone})");
    }

    // --------------------------------------------------------------------------------

    private async Task HandleValidationError(ValidationException ex, Dictionary<string, string> data, SaleType saleType, Client client, Agenda? agenda)
    {
        var key = data.Key("key") ?? KeyHelper.NewKey();
        await _db.AddSale(client, agenda, saleType,
            dateTime: DateTime.UtcNow,
            isDone: false,
            isNewKey: true,
            key: key);

        var link = UrlHelper.MakeLink(saleType, data); /// нормальная ссылка, подготовить data    ///!!!!!!!!!!!!!!!!!!
        var sb = new StringBuilder()
            .AppendLine("При заполнении анкеты произошла ошибка:\n");
        foreach (var error in ex.Errors)
            sb.AppendLine($"\t{error.ErrorMessage}");
        sb.AppendLine($"\nПожалуйста, перейдите по ссылке и введите данные повторно!\n\t{link}");

        var message = sb.ToString();
        var title = saleType.AsErrorTitle();
        await _mail.SendMailAsync(MailType.Failure, (client.Email, client.Name), title, message);

        _logger.LogWarning($"Incorrect data from client {client.Email} ({client.Phone}) by purcharing [{saleType}]. New form link was sent to email {link}");
    }

    private async Task TransmitWorkoutProgram(Sale sale)
    {
        var wp = await _db.GetWorkoutProgram(sale.Id);

        if (wp is null)
        {
            sale.Key = KeyHelper.NewKey();
            _db.Context.Sales.Update(sale);

            var dic = new Dictionary<string, string>() /// вынести отдельно?   ///!!!!!!!!!!!!!!!!!!
            {
                ["key"] = sale.Key,
                ["gender"] = sale.Agenda.Gender.AsValue().ToString(),
                ["activity_level"] = sale.Agenda.ActivityLevel.ToString(),
                ["focus"] = sale.Agenda.Focus.ToString(),
                ["purpouse"] = sale.Agenda.Purpouse.ToString(),
                ["diseases"] = sale.Agenda.Diseases,
            };

            var link = UrlHelper.MakeLink("https://forms.yandex.ru/cloud/62fff1f4d2a4c7ac2baeaa93", dic);
            var clientInfo = LogHelper.ClientInfo(sale.Client, sale.Agenda);
            var emails = await _db.GetInnerEmails(admins: true);
            var title = "Запрос на новую программу тренировок";
            var message = $"Необходимо составить новую программу тренировок для:\n{clientInfo}\nПерейдите для этого по ссылке: {link}";

            foreach (var email in emails)
                await _mail.SendMailAsync(MailType.Inner, email, title, message);

            _logger.LogInformation($"Link for creating program for {sale.Client.Email} is sent to admins");
        }
        else
        {
            await Task.Delay(10000);
            // var f = new Timer(2000); таймер
            string subject = sale.Type is SaleType.Begginer ? "Begginer: готовая программа" : "Profi: готовая программа";
            string message = "Ваша персональная программа тренировок готова! Она прикреплена к данному сообщению.";
            await _mail.SendMailAsync(MailType.Success, (sale.Client.Name, sale.Client.Email), subject, message, ("Персональная программа тренировок.pdf", wp.ProgramPath));
            sale.WorkoutProgram = wp;
            _db.Context.Sales.Update(sale);
            await _db.СompleteAsync(sale.Id);
            _logger.LogInformation($"Workout program sent to {sale.Client.Email} ({sale.Client.Phone})");
        }
    }

    private async Task AddWorkoutProgram(Dictionary<string, string> data)
    {
        var path = Path.Combine("Resources", "Produced", "WorkoutPrograms", Guid.NewGuid().ToString());
        var unique = data.Key("unique");
        var workoutProgram = _mapper.Map<WorkoutProgram>(data);

        await _mail.LoadAttachmentAsync(unique, path);
        workoutProgram.ProgramPath = path;
        await _db.AddWorkoutProgram(workoutProgram);

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
                _logger.LogWarning($"Problem with putting correct key while new workout program adding");
                throw new CustomerExсeption("Указан некорректный ключ при добавлении программы. Воспользуйтесь специальной ссылкой.");
            }
            
            sale.WorkoutProgram = workoutProgram;
            _db.Context.Sales.Update(sale);

            string subject = sale.Type is SaleType.Begginer ? "Begginer: готовая программа" : "Profi: готовая программа";
            string message = "Ваша персональная программа тренировок готова! Она прикреплена к данному сообщению.";
            await _mail.SendMailAsync(MailType.Success, (sale.Client.Name, sale.Client.Email), subject, message, ("Персональная программа тренировок.pdf", sale.WorkoutProgram.ProgramPath));

            await _db.СompleteAsync(sale.Id);
            _logger.LogInformation($"Workout program finally sent to {sale.Client.Email} ({sale.Client.Phone})");
        }            
    }

    private async Task LoadRecepies(Dictionary<string, string> data)
    {
        var path = Path.Combine("Resources", "Produced", "Recepies.pdf");
        var unique = data.Key("unique");
        await _mail.LoadAttachmentAsync(unique, path);
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
            _logger.LogInformation($"Woker {worker.Name} is added");
        });
    }

    private async Task DeleteWorker(Dictionary<string, string> data)
    {
        await Task.Run(() =>
        {
            var workerToDelete = (from worker in _db.Context.Workers
                                  where worker.Name == data.Key("name")
                                  select worker).FirstOrDefault();
            _db.Context.Workers.Remove(workerToDelete);
            _logger.LogInformation($"Woker {workerToDelete.Name} is deleted");
        });
    }
}

