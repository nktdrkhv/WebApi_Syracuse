using Syracuse;
using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using AutoMapper;
using FluentValidation;
using Hangfire;
using Hangfire.Storage.SQLite;

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
                app.Logger.LogWarning($"From Exception handler: {exceptionObject.Error.Message} \n {exceptionObject.Error.StackTrace}");
                return Task.CompletedTask;
            });
    });
}

app.UseCors();

// --------------------------------------------------------------------------------

app.MapPost("/tilda", (HttpContext context, TildaOrder json, ICustomerService customerService) =>
{
    if (!string.Equals(json.Token, KeyHelper.ApiToken)) return Results.Unauthorized();
    if (json.Test == "test") return Results.Ok("test");

    var data = new Dictionary<string, string>();
    data["name"] = json.Name;
    data["email"] = json.Email;
    data["phone"] = json.Email;
    data["orderid"] = json.Payment.OrderId;
    var product = json.Payment.Products.First();
    data["price"] = product.Price;
    foreach (var option in product.Options)
    {
        var optionName = option.Option;
        var variantName = option.Variant;
        data[optionName] = variantName;
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
    var price = context.Products.Where(p => p.Code == product).Select(p => p.Price).SingleOrDefault();
    app.Logger.LogInformation($"Price for [{product}] is [{price}] rubles");
    return Results.Ok(price);
})/*.RequireCors(cpb => cpb.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin())*/;


app.MapGet("/label/{product}", (string product) =>
{
    using var context = new ApplicationContext();
    var label = context.Products.Where(p => p.Code == product).Select(p => p.Label).SingleOrDefault();
    app.Logger.LogInformation($"Label for [{product}] is [{label}]");
    return Results.Ok(label);
})/*.RequireCors(cpb => cpb.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin())*/;

// app.MapPost("/upload/{file}", (string file, string? token, IDbService) =>
// {

// });

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

app.Run();