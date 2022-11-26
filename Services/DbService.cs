using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Syracuse;

public sealed class ApplicationContext : DbContext
{
    public ApplicationContext()
    {
        Database.EnsureCreated();
    }

    public DbSet<Agenda> Agendas => Set<Agenda>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<Worker> Workers => Set<Worker>();
    public DbSet<WorkoutProgram> WorkoutPrograms => Set<WorkoutProgram>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=Resources/Syracuse.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>()
            .HasAlternateKey(c => c.Email);
        modelBuilder.Entity<Worker>()
            .HasAlternateKey(w => w.Nickname);
        modelBuilder.Entity<Sale>()
            .HasIndex(s => s.Key).IsUnique();
        modelBuilder.Entity<Sale>()
            .HasMany(s => s.Product).WithMany(p => p.PartOf);
        modelBuilder.Entity<Product>()
            .HasIndex(s => s.Code).IsUnique();
        modelBuilder.Entity<Product>()
            .HasMany(p => p.Parents).WithMany(p => p.Childs);
    }
}

public interface IDbService : IAsyncDisposable
{
    ApplicationContext Context { get; }

    Task СompleteAsync(int saleId);

    Task<Sale> AddSaleAsync(Client client, Agenda? agenda, SaleType saleType, DateTime dateTime, bool isDone,
        bool isNewKey = false, string? key = null);

    Task AddWorkoutProgramAsync(WorkoutProgram workoutProgram);

    Task<WorkoutProgram?> FindWorkoutProgramAsync(WorkoutProgram workoutProgram);
    Task<WorkoutProgram?> FindWorkoutProgramAsync(Agenda agenda);

    Task<Table> GetNonDoneSalesAsync(string separator, double additionalHours = 7.0);
    Task<Table> GetWorkoutProgramsAsync(string separator);
    Task<Table> GetTeamAsync(string separator);
    Task<Table> GetProductsAsync(string separator);

    Task<string?> GetCoachContactsAsync(string coachNickname, string separator = "<br/>");
    Task<(string email, string name)[]> GetInnerEmailsAsync(string[] coachNicknames = null!, bool admins = false);
}

public class DbService : IDbService
{
    private readonly ILogger<DbService> _logger;

    public DbService(ILogger<DbService> logger)
    {
        _logger = logger;
        Context = new ApplicationContext();
    }

    public ApplicationContext Context { get; }

    public async Task СompleteAsync(int saleId)
    {
        var sale = await Context.FindAsync<Sale>(saleId);
        if (sale is not null)
        {
            sale.IsDone = true;
            sale.Key = null;
            sale.IsErrorHandled = null;
        }
    }

    public async Task<Sale> AddSaleAsync(Client client, Agenda? agenda, SaleType saleType, DateTime dateTime,
        bool isDone, bool isNewKey = false, string? key = null)
    {
        if (string.IsNullOrWhiteSpace(key) || isNewKey)
        {
            // ReSharper disable once AccessToModifiedClosure
            if (await Context.Clients.Where(c => c.Email == client.Email).FirstOrDefaultAsync() is { } oldClient)
            {
                client = oldClient;
                _logger.LogInformation($"Db (new sale): [{client.Name}] ({client.Email}) is already exist");
            }
            else
            {
                Context.Clients.Add(client);
                _logger.LogInformation($"Db (new sale): [{client.Name}] ({client.Email}) is new");
            }

            var sale = new Sale
            {
                Client = client, Agenda = agenda, Type = saleType, PurchaseTime = dateTime, IsDone = isDone, Key = key
            };
            Context.Sales.Add(sale);
            await Context.SaveChangesAsync();
            _logger.LogInformation($"Db (new sale): sale [{sale.Id} – {sale.Type}] for {client.Name} ({client.Email})");
            return sale;
        }

        try
        {
            Sale? sale = await Context.Sales.Include(s => s.Client).Include(s => s.Agenda).Where(s => s.Key == key)
                .SingleAsync();

            if (client.Email != sale.Client.Email &&
                Context.Clients.SingleOrDefault(c => c.Email == client.Email) is { } existingClient)
            {
                existingClient.UpdateWith(client);
                sale.Client = existingClient;
            }
            else
            {
                sale.Client.UpdateWith(client);
            }

            sale.Agenda?.UpdateWith(agenda);

            sale.IsErrorHandled = null;
            sale.Key = null;
            Context.Sales.Update(sale);
            _logger.LogInformation(
                $"Db (update sale): sale [{sale.Id} – {sale.Type}] got correct data for {client.Name} ({client.Email})");
            return sale;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e,
                $"Db (update sale): the key [{key}] is not found for [{client.Name}] ({client.Email})");
            throw new DbExсeption(
                $"У клиента {client.Name} {client.Email} {client.Phone} неверный ключ для обновления его данных.");
        }
    }

    public async Task AddWorkoutProgramAsync(WorkoutProgram workoutProgram)
    {
        if (await FindWorkoutProgramAsync(workoutProgram) is { } existOne)
        {
            existOne.ProgramPath = workoutProgram.ProgramPath;
            Context.WorkoutPrograms.Update(existOne);
            _logger.LogInformation($"Db (add wp): wp is exits {workoutProgram.ProgramPath}");
        }
        else
        {
            await Context.WorkoutPrograms.AddAsync(workoutProgram);
            _logger.LogInformation($"Db (add wp): wp is newby {workoutProgram.ProgramPath}");
        }
    }


    public async Task<WorkoutProgram?> FindWorkoutProgramAsync(WorkoutProgram workoutProgram)
    {
        return await (from wp in Context.WorkoutPrograms
            where wp.Gender == workoutProgram.Gender &&
                  wp.Purpouse == workoutProgram.Purpouse &&
                  wp.Focus == workoutProgram.Focus &&
                  wp.ActivityLevel == workoutProgram.ActivityLevel &&
                  wp.Diseases == workoutProgram.Diseases &&
                  wp.IgnoreDiseases == workoutProgram.IgnoreDiseases
            select wp).FirstOrDefaultAsync();
    }

    public Task<WorkoutProgram?> FindWorkoutProgramAsync(Agenda agenda)
    {
        List<WorkoutProgram>? wps = (from prog in Context.WorkoutPrograms
            where agenda.Gender == prog.Gender &&
                  agenda.ActivityLevel == prog.ActivityLevel &&
                  agenda.Focus == prog.Focus &&
                  agenda.Purpouse == prog.Purpouse &&
                  (agenda.Diseases == prog.Diseases || prog.IgnoreDiseases)
            select prog).ToList();

        _logger.LogInformation(
            $"Db (find wp): For [{agenda.Gender} {agenda.ActivityLevel.AsString()} {agenda.Purpouse.AsString()} {agenda.Focus.AsString() ?? "no focus"} {agenda.Diseases ?? "no diseases"}] found total [{wps.Count}]");

        if (agenda.Diseases is { } diseases)
            if (wps.FirstOrDefault(wp => wp.Diseases == diseases) is { } wpDpos)
                return Task.FromResult(wpDpos);
            else if (wps.FirstOrDefault(wp => wp.IgnoreDiseases) is { } wpDneg)
                return Task.FromResult(wpDneg);
            else
                return Task.FromResult<WorkoutProgram?>(null);
        return Task.FromResult(wps.FirstOrDefault());
    }

    public async Task<Table> GetWorkoutProgramsAsync(string separator)
    {
        IQueryable<List<string>>? wps = from wp in Context.WorkoutPrograms
            select new List<string>
            {
                wp.Gender ?? "––",
                wp.ActivityLevel == null ? "––" : wp.ActivityLevel.AsString(),
                wp.Purpouse == null ? "––" : wp.Purpouse.AsString(),
                wp.Focus == null ? "––" : wp.Focus.AsString(),
                wp.Diseases ?? "––",
                wp.IgnoreDiseases == true ? "Да" : "Нет"
            };
        var table = new Table
        {
            Titles = new List<string> { "Пол", "Кол-во тренировок", "Цель", "Фокус", "Заболевания", "Игнорировать?" },
            Data = await wps.ToListAsync()
        };
        return table;
    }

    public async Task<Table> GetNonDoneSalesAsync(string separator, double additionalHours = 7.0)
    {
        IQueryable<List<string>>? sales = from c in Context.Sales
            where !c.IsDone
            let purchaseTime = c.PurchaseTime.AddHours(additionalHours)
            let scheduleTime = c.ScheduledDeliverTime.GetValueOrDefault().AddHours(additionalHours)
            select new List<string>
            {
                c.Id.ToString(),
                $"{c.Client.Name}{separator}{c.Client.Email}{separator}{c.Client.Phone}",
                c.Type.ToString(),
                $"{purchaseTime.ToShortDateString()}{separator}{purchaseTime.ToShortTimeString()}",
                $"{scheduleTime.ToShortDateString()}{separator}{scheduleTime.ToShortTimeString()}"
            };
        var table = new Table
        {
            Titles = new List<string> { "ID", "Клиент", "Тип", "Время покупки", "Время отправки" },
            Data = await sales.ToListAsync()
        };
        return table;
    }

    public async Task<Table> GetTeamAsync(string separator)
    {
        IQueryable<List<string>>? team = from w in Context.Workers
            select new List<string> { w.Nickname, w.Name, w.Contacts.AsInfoString(separator), w.Admin ? "Да" : "Нет" };
        var table = new Table
        {
            Titles = new List<string> { "Никнейм", "Имя", "Контакты", "Администратор?" },
            Data = await team.ToListAsync()
        };
        return table;
    }

    public async Task<Table> GetProductsAsync(string separator)
    {
        IQueryable<List<string>>? products = from p in Context.Products.Include(p => p.Parents)
            select new List<string>
            {
                p.Code, p.Label, p.Price.ToString(), p.Content.ToColumn(separator), p.Parents.AsString(separator, false)
            };
        var table = new Table
        {
            Titles = new List<string> { "Код", "Название", "Цена, ₽", "Контент", "Включен в" },
            Data = await products.ToListAsync()
        };
        return table;
    }

    public async Task<string?> GetCoachContactsAsync(string coachNickname, string separator = "<br/>")
    {
        try
        {
            Worker? worker = await (from w in Context.Workers.Include(w => w.Contacts)
                where w.Nickname == coachNickname
                select w).SingleAsync();
            var contacts = new StringBuilder();

            if (worker.Contacts == null) return null;
            foreach (Contact? item in worker.Contacts.Where(c => c.Type == ContactType.Email))
                contacts.AppendLine($"Почта: {item.Info}" + separator);

            foreach (Contact? item in worker.Contacts.Where(c => c.Type == ContactType.Phone))
                contacts.AppendLine($"Телефон: {item.Info}" + separator);

            foreach (Contact? item in worker.Contacts.Where(c => c.Type == ContactType.Address))
                contacts.AppendLine($"Адрес: {item.Info}" + separator);

            return contacts.ToString();
        }
        catch
        {
            _logger.LogWarning($"Db (trainer contacts): Cant find contacts of [{coachNickname}]");
            throw new DbExсeption($"Невозможно найти контакты для тренера: [{coachNickname}]");
        }
    }

    public async Task<(string email, string name)[]> GetInnerEmailsAsync(string[] coachNicknames = null!,
        bool admins = false)
    {
        try
        {
            IQueryable<(string, string)>? emails = from worker in Context.Workers
                join contact in Context.Contacts on worker equals contact.Worker
                where contact.Type == ContactType.Email &&
                      ((coachNicknames != null && coachNicknames.Contains(worker.Nickname)) ||
                       (worker.Admin && admins))
                select ValueTuple.Create(contact.Info, worker.Name);
            return await emails.ToArrayAsync();
        }
        catch
        {
            _logger.LogWarning("Db (inner emails): Cant find contacts of ");
            throw new DbExсeption("Невозможно найти контакты для хотя бы одного администратора или тренера");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Context.SaveChangesAsync();
        await Context.DisposeAsync();
        _logger.LogTrace("Db (dispose): disposed");
    }
}