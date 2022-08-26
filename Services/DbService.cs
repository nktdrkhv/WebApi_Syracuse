using System.Text;

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
                    .HasIndex(w => w.Name).IsUnique();
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
    Task<string> GetTrainerContactsAsync(string trainerName);
    Task<(string name, string email)[]> GetInnerEmailsAsync(string[] trainerNames = null, bool admins = false);
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

        var oldClient = await Context.Clients.Where(c => c.Email == client.Email && c.Name == client.Name && client.Phone == client.Phone).FirstOrDefaultAsync();

        if (key is null || isNewKey)
        {
            if (oldClient is not null) client = oldClient;
            else Context.Clients.Add(client);

            if (agenda is not null) Context.Agendas.Add(agenda);

            sale = new Sale { Client = client, Agenda = agenda, Type = saleType, Time = dateTime, IsDone = isDone, Key = key };
            Context.Sales.Add(sale);

            _logger.LogInformation($"Db (new sale): sale [{sale.Id} – {sale.Type}] for {client.Name} ({client.Email})");
        }
        else
        {
            try
            {
                sale = await Context.Sales.Where(s => s.Key == key).SingleAsync();
                sale.Key = null;
                Context.Sales.Update(sale);
            }
            catch
            {
                _logger.LogInformation($"Db (update sale): the key [{key}] is not found for {client.Name} ({client.Email})");
                throw new DbExсeption($"У клиента {client.Name} {client.Email} {client.Phone} неверный ключ для обновления его данных. Перейдите по ссылке ");
            }

            client.Id = sale.Client.Id;
            Context.Clients.Update(client);

            if (agenda is not null)
            {
                agenda.Id = sale.Agenda.Id;
                Context.Agendas.Update(agenda);
            }

            _logger.LogInformation($"Db (update sale): sale [{sale.Id} – {sale.Type}] got correct data for {client.Name} ({client.Email})");
        }

        Context.SaveChanges();
        return sale;
    }

    public async Task AddWorkoutProgramAsync(WorkoutProgram workoutProgram)
    {
        var existOne = await FindWorkoutProgramAsync(workoutProgram);
        if (existOne is not null)
            existOne.ProgramPath = workoutProgram.ProgramPath;
        else
            await Context.WorkoutPrograms.AddAsync(workoutProgram);
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

    public async Task<string> GetTrainerContactsAsync(string trainerName)
    {
        try
        {
            var worker = await Context.Workers.Where(w => w.Name == trainerName).SingleAsync(); //// catch error
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
            _logger.LogWarning($"Problem with finding contacts of {trainerName}");
            throw new DbExсeption($"Невозможно найти контакты для тренера: {trainerName}");
        }
    }

    public async Task<(string name, string email)[]> GetInnerEmailsAsync(string[] trainerNames = null, bool admins = false)
    {
        try
        {
            var emails = from worker in Context.Workers
                         join contact in Context.Contacts on worker.Id equals contact.Worker.Id
                         where contact.Type == ContactType.Email && (trainerNames.Contains(worker.Name) || worker.Admin == admins)
                         select ValueTuple.Create(worker.Name, contact.Info);
            return await emails.ToArrayAsync();
        }
        catch
        {
            _logger.LogWarning($"Problem with finding contacts of admins or trainers");
            throw new DbExсeption($"Невозможно найти контакты для хотя бы одного администратора или тренера");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Context.SaveChangesAsync();
        await Context.DisposeAsync();
    }
}

