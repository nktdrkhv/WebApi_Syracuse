﻿using System.Text;

using Microsoft.EntityFrameworkCore;

namespace Syracuse;

public class ApplicationContext : DbContext
{
    public DbSet<Agenda> Agendas => Set<Agenda>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<Worker> Workers => Set<Worker>();
    public DbSet<WorkoutProgram> WorkoutPrograms => Set<WorkoutProgram>();
    public DbSet<Product> Products => Set<Product>();

    public ApplicationContext() => Database.EnsureCreated();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlite("Data Source=Resources/Syracuse.db");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>()
                    .HasAlternateKey(c => new { c.Email, c.Phone, c.Name });
        modelBuilder.Entity<Worker>()
                    .HasIndex(w => w.Nickname).IsUnique();
        modelBuilder.Entity<Sale>()
                    .HasIndex(s => s.Key).IsUnique();
        modelBuilder.Entity<Product>()
                    .HasIndex(s => s.Code).IsUnique();
    }
}

public interface IDbService : IAsyncDisposable
{
    ApplicationContext Context { get; }

    Task СompleteAsync(int saleId);
    Task<Sale> AddSaleAsync(Client client, Agenda? agenda, SaleType saleType, DateTime dateTime, bool isDone, bool isNewKey = false, string? key = null);
    Task AddWorkoutProgramAsync(WorkoutProgram workoutProgram);
    Task<WorkoutProgram?> FindWorkoutProgramAsync(WorkoutProgram workoutProgram);
    Task<WorkoutProgram?> FindWorkoutProgramAsync(int saleId);
    Task<Table> FindNonDoneSalesAsync(string separator);
    Task<Table> FindWorkoutProgramsAsync(string separator);
    Task<Table> FindTeamAsync(string separator);
    Task<string> GetCoachContactsAsync(string coachNickname);
    Task<(string name, string email)[]> GetInnerEmailsAsync(string[] coachNicknames = null, bool admins = false);
}

public class DbService : IDbService
{
    public ApplicationContext Context { get; }

    private ILogger<DbService> _logger;

    public DbService(ILogger<DbService> logger)
    {
        _logger = logger;
        Context = new ApplicationContext();
    }

    public async Task СompleteAsync(int saleId)
    {
        var sale = await Context.FindAsync<Sale>(saleId);
        if (sale is not null)
        {
            sale.IsDone = true;
            sale.Key = null;
        }
    }

    public async Task<Sale> AddSaleAsync(Client client, Agenda? agenda, SaleType saleType, DateTime dateTime, bool isDone, bool isNewKey = false, string? key = null)
    {
        Sale sale = null;

        var oldClient = await Context.Clients.Where(c => c.Email == client.Email && c.Name == client.Name && c.Phone == client.Phone).FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(key) || isNewKey)
        {
            if (oldClient is not null)
            {
                client = oldClient;
                _logger.LogInformation($"Db (new sale): [{client.Name}] ({client.Email}) is already exist");
            }
            else
            {
                Context.Clients.Add(client);
                _logger.LogInformation($"Db (new sale): [{client.Name}] ({client.Email}) is new");
            }

            if (agenda is not null)
                Context.Agendas.Add(agenda);

            sale = new Sale { Client = client, Agenda = agenda, Type = saleType, Time = dateTime, IsDone = isDone, Key = key };
            Context.Sales.Add(sale);

            _logger.LogInformation($"Db (new sale): sale [{sale.Id} – {sale.Type}] for {client.Name} ({client.Email})");
        }
        else
        {
            try
            {
                sale = await Context.Sales.Where(s =>
                    string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)).SingleAsync();
                sale.Key = null;
                Context.Sales.Update(sale);

                client.Id = sale.Client.Id;
                Context.Clients.Update(client);

                if (agenda is not null)
                {
                    agenda.Id = sale.Agenda.Id;
                    Context.Agendas.Update(agenda);
                }
            }
            catch
            {
                _logger.LogInformation($"Db (update sale): the key [{key}] is not found for {client.Name} ({client.Email})");
                throw new DbExсeption($"У клиента {client.Name} {client.Email} {client.Phone} неверный ключ для обновления его данных.");
                // а был ли клиент, или это старый пишет. есть ли такой в бд?
            }

            _logger.LogInformation($"Db (update sale): sale [{sale.Id} – {sale.Type}] got correct data for {client.Name} ({client.Email})");
        }

        Context.SaveChanges(); //!
        return sale;
    }

    public async Task AddWorkoutProgramAsync(WorkoutProgram workoutProgram)
    {
        var existOne = await FindWorkoutProgramAsync(workoutProgram);
        if (existOne is not null)
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
        //todo join
        return await (from wp in Context.WorkoutPrograms
                      where wp.Gender == workoutProgram.Gender &&
                            wp.Purpouse == workoutProgram.Purpouse &&
                            wp.Focus == workoutProgram.Focus &&
                            wp.ActivityLevel == workoutProgram.ActivityLevel &&
                            wp.Diseases == workoutProgram.Diseases &&
                            wp.IgnoreDiseases == workoutProgram.IgnoreDiseases
                      select wp).FirstOrDefaultAsync();
    }

    public async Task<WorkoutProgram?> FindWorkoutProgramAsync(int saleId)
    {
        var sale = await Context.FindAsync<Sale>(saleId);
        var agenda = sale.Agenda;

        var wp = (from prog in Context.WorkoutPrograms
                  where agenda.Gender == prog.Gender &&
                        agenda.ActivityLevel == prog.ActivityLevel &&
                        agenda.Focus == prog.Focus &&
                        agenda.Purpouse == prog.Purpouse
                  select prog).FirstOrDefault();
        if (wp != null && (wp.Diseases == agenda.Diseases || wp.IgnoreDiseases))
            return wp;
        else
            return null;
    }

    public async Task<Table> FindNonDoneSalesAsync(string separator)
    {
        var sales = from c in Context.Sales
                    where c.IsDone == false
                    select new List<string>() { c.Id.ToString(), $"{c.Client.Name}{separator}{c.Client.Email}{separator}{c.Client.Phone}", c.Type.ToString(), c.Time.ToString(), c.Key };
        var table = new Table();
        table.Titles = new() { "ID", "Клиент", "Тип", "Дата", "Ключ" };
        table.Data = await sales.ToListAsync();
        return table;
    }

    public async Task<Table> FindWorkoutProgramsAsync(string separator)
    {
        var wps = from wp in Context.WorkoutPrograms
                  select new List<string>() {
                    wp.Gender == null ? "––" : wp.Gender,
                    wp.ActivityLevel == null ? "––" : wp.ActivityLevel.AsString(),
                    wp.Purpouse == null ? "––" : wp.Purpouse.AsString() ,
                    wp.Focus == null ? "––" : wp.Focus.AsString(),
                    wp.Diseases == null ? "––" : wp.Diseases,
                    wp.IgnoreDiseases == true ? "Да" : "Нет" };
        var table = new Table();
        table.Titles = new() { "Пол", "Кол-во тренировок", "Цель", "Фокус", "Заболевания", "Игнорировать?" };
        table.Data = await wps.ToListAsync();
        return table;
    }

    public async Task<Table> FindTeamAsync(string separator)
    {
        var team = from w in Context.Workers
                   select new List<string>() { w.Nickname, w.Name, w.Contacts.AsInfoString(separator), w.Admin ? "Да" : "Нет" };
        var table = new Table();
        table.Titles = new() { "Никнейм", "Имя", "Контакты", "Администратор?" };
        table.Data = await team.ToListAsync();
        return table;
    }

    public async Task<string> GetCoachContactsAsync(string coachNickname)
    {
        try
        {
            var worker = await Context.Workers.Where(
                w => String.Equals(w.Nickname, coachNickname, StringComparison.OrdinalIgnoreCase))
                .SingleAsync();
            var contacts = new StringBuilder();

            foreach (var item in worker.Contacts.Where(c => c.Type == ContactType.Email))
                contacts.AppendLine($"Почта: {item.Info}");

            foreach (var item in worker.Contacts.Where(c => c.Type == ContactType.Phone))
                contacts.AppendLine($"Телефон: {item.Info}");

            foreach (var item in worker.Contacts.Where(c => c.Type == ContactType.Address))
                contacts.AppendLine($"Адрес: {item.Info}");

            return contacts.ToString();
        }
        catch
        {
            _logger.LogWarning($"Db (trainer contacts): Cant find contacts of [{coachNickname}]");
            throw new DbExсeption($"Невозможно найти контакты для тренера: [{coachNickname}]");
        }
    }

    public async Task<(string name, string email)[]> GetInnerEmailsAsync(string[] coachNicknames = null, bool admins = false)
    {
        try
        {
            var emails = from worker in Context.Workers
                         join contact in Context.Contacts on worker equals contact.Worker
                         where contact.Type == ContactType.Email &&
                                (coachNicknames != null && coachNicknames.Contains(worker.Nickname) ||
                                worker.Admin && admins)
                         select ValueTuple.Create(worker.Name, contact.Info);
            return await emails.ToArrayAsync();
        }
        catch
        {
            _logger.LogWarning($"Db (inner emails): Cant find contacts of ");
            throw new DbExсeption($"Невозможно найти контакты для хотя бы одного администратора или тренера");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Context.SaveChangesAsync();
        await Context.DisposeAsync();
        _logger.LogInformation("Db (dispose): disposed");
    }
}