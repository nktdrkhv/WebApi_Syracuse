using Syracuse;
using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using AutoMapper;
using FluentValidation;
using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.EntityFrameworkCore;

CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

var mappingConfig = new MapperConfiguration(mc =>
{
    mc.AddProfile(new AgendaMapper());
    mc.AddProfile(new ClientMapper());
    mc.AddProfile(new WorkerMapper());
    mc.AddProfile(new ContactMapper());
    mc.AddProfile(new WorkoutProgramMapper());
});
var autoMapper = mappingConfig.CreateMapper();

// --------------------------------------------------------------------------------

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddLogging();

builder.Services.AddHangfire(configuration => configuration
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage("Resources/Hangfire.db"));
builder.Services.AddHangfireServer();

builder.Services.AddSingleton(autoMapper);
builder.Services.AddScoped<IValidator<Client>, ClientValidator>();
builder.Services.AddScoped<IValidator<Agenda>, AgendaValidator>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IPdfService, PdfService>();
builder.Services.AddScoped<IMailService, MailService>();
builder.Services.AddScoped<IDbService, DbService>();

if (!builder.Environment.IsDevelopment())
    builder.Services.AddLettuceEncrypt();

builder.Services.AddCors(options =>
     options.AddDefaultPolicy(policy => policy.WithOrigins("https://korablev-team.ru").AllowAnyMethod().AllowAnyHeader()));
builder.Services.AddCors();
builder.Host.UseSystemd();

// --------------------------------------------------------------------------------

WebApplication app = builder.Build();
GlobalConfiguration.Configuration.UseActivator(new HangfireActivator(app.Services));

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseExceptionHandler(options =>
    {
        options.Run(
            context =>
            {
                var exceptionObject = context.Features.Get<IExceptionHandlerFeature>();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                app.Logger.LogError(exceptionObject.Error, $"Upper exception handler");
                return Task.CompletedTask;
            });
    });
}

app.UseCors();

// --------------------------------------------------------------------------------

app.MapPost("/tildaForm", (HttpContext context, ICustomerService customerService) =>
{
    if (!string.Equals(context.Request.Form["token"], KeyHelper.ApiToken)) return Results.Unauthorized();
    if (context.Request.Form["test"] == "test") return Results.Ok("test");

    context.Response.OnCompleted(() =>
    {
        try
        {
            var data = context.Request.Form.ToDictionary(form => form.Key.ToLower(), form => form.Value.ToString());
            return customerService.HandleTildaAsync(data);
        }
        catch (Exception e)
        {
            app.Logger.LogError(e, "Error while handling Tilda");
            return Task.CompletedTask;
        }
    });

    return Results.Ok();
});

app.MapPost("/tilda", (HttpContext context, TildaOrder json, ICustomerService customerService, IDbService db) =>
{
    if (!string.Equals(json.Token, KeyHelper.ApiToken)) return Results.Unauthorized();
    if (json.Test == "test") return Results.Ok("test");

    var data = new Dictionary<string, string>();
    data["name"] = json.Name;
    data["email"] = json.Email;
    data["phone"] = json.Phone;
    data["orderid"] = json.Payment.OrderId;
    var formname = json.Payment.Products.First().ExternalId.Split('#').First();
    data["formname"] = formname;

    foreach (var option in json.Payment.Products.First()?.Options ?? Enumerable.Empty<ProductOption>())
        if (option.Option.AsValue().AsCode() is string code)
            data[code] = option.Variant;

    if (formname == "online-coach")
    {
        var productCode = db.Context.Products.Where(p => p.Label == data["trainer"]).Select(p => p.Code).Single();
        var productExpectedPaidAmount = db.Context.Products.Where(p => p.Code == productCode).Select(p => p.Price).Single();
        if (int.Parse(json.Payment.Amount) != productExpectedPaidAmount)
            return Results.Problem("Incorrect paiment");
        data["trainer"] = productCode;
    }
    else if (formname == "posing")
    {
        int finalAmount = 0;
        foreach (var productLabel in data["videos"].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            finalAmount += GetPrice(db.Context, db.Context.Products.Where(p => p.Label == productLabel).Select(p => p.Code).Single());
        if (finalAmount != int.Parse(json.Payment.Amount))
            return Results.Problem("Incorrect paiment");
    }
    else
    {
        var productPaidAmount = int.Parse(json.Payment.Amount);
        var productExpectedPaidAmount = db.Context.Products.Where(p => p.Code == formname).Select(p => p.Price).Single();
        if (productPaidAmount != productExpectedPaidAmount)
            return Results.Problem("Incorrect paiment");
    }

    app.Logger.LogInformation(message: LogHelper.RawData(data));

    context.Response.OnCompleted(() =>
    {
        try
        {
            return customerService.HandleTildaAsync(data);
        }
        catch (Exception e)
        {
            app.Logger.LogError(e, "Error while handling Tilda");
            return Task.CompletedTask;
        }
    });

    return Results.Ok();
});

app.MapPost("/yandex", (HttpContext context, YandexJsonRpc json, ICustomerService customerService) =>
{
    var data = json.Params;
    app.Logger.LogInformation(message: LogHelper.RawData(data));

    if (!string.Equals(data.Key("token"), KeyHelper.ApiToken))
    {
        app.Logger.LogInformation($"Unauthorized: post yandex");
        return Results.Unauthorized(); ;
    }

    context.Response.OnCompleted(() => Task.Run(async () =>
    {
        try
        {
            await customerService.HandleYandexAsync(data);
        }
        catch (Exception e)
        {
            app.Logger.LogError(e, "Error while handling Yandex");
        }
    }));

    return Results.Ok();
});

// --------------------------------------------------------------------------------

app.MapGet("/", (HttpContext context) =>
{
    app.Logger.LogInformation($"Access to root path: {context.Request.Host}");
    return Results.Ok("These Are Not the Droids You Are Looking For");
});

app.MapGet("/admin/{table}", async (string table, string? token, IDbService db) =>
{
    if (token != KeyHelper.ApiToken)
    {
        app.Logger.LogInformation($"Unauthorized: get [{table}]");
        return Results.Unauthorized();
    }

    var handler = table switch
    {
        "nondone" => db.GetNonDoneSalesAsync("<br/>"),
        "team" => db.GetTeamAsync("<br/>"),
        "wp" => db.GetWorkoutProgramsAsync("<br/>"),
        _ => Task.FromResult(new Table()),
    };

    return Results.Json(await handler);
});

app.MapGet("/price/{product}", (string product) =>
{
    using var context = new ApplicationContext();
    var price = GetPrice(context, product);
    app.Logger.LogInformation($"Price for [{product}] is [{price}] rubles");
    return Results.Ok(price);
});

app.MapGet("/label/{product}", (string product) =>
{
    using var context = new ApplicationContext();
    var label = context.Products.Where(p => p.Code == product).Select(p => p.Label).SingleOrDefault();
    app.Logger.LogInformation($"Label for [{product}] is [{label}]");
    return Results.Ok(label);
});

// --------------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.MapGet("/env", () =>
    {
        app.Logger.LogInformation("Env: API_TOKEN = {}", Environment.GetEnvironmentVariable("API_TOKEN"));
        app.Logger.LogInformation("Env: UNIVERSAL_KEY = {}", Environment.GetEnvironmentVariable("UNIVERSAL_KEY"));
        app.Logger.LogInformation("Env: MAIL_USER = {}", Environment.GetEnvironmentVariable("MAIL_USER"));
        app.Logger.LogInformation("Env: MAIL_PASS = {}", Environment.GetEnvironmentVariable("MAIL_PASS"));
        app.Logger.LogInformation("Env: MAIL_FAKE = {}", Environment.GetEnvironmentVariable("MAIL_FAKE"));
        app.Logger.LogInformation("Env: MAIL_FROM_NAME = {}", Environment.GetEnvironmentVariable("MAIL_FROM_NAME"));
        app.Logger.LogInformation("Env: MAIL_FROM_ADDR = {}", Environment.GetEnvironmentVariable("MAIL_FROM_ADDR"));
    });
}
// --------------------------------------------------------------------------------

ServiceActivator.Configure(app.Services);

app.Run();

// --------------------------------------------------------------------------------

int GetPrice(ApplicationContext context, string productCode)
{
    var item = context.Products.Include(p => p.Includes).Where(p => p.Code == productCode).SingleOrDefault();
    if (item.Price == 0 && item.Includes is List<Product> goods)
    {
        int price = 0;
        foreach (var elem in goods)
            price += elem.Price;
        return price;
    }
    else
        return item.Price;
}