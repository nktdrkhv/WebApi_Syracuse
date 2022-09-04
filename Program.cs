using Syracuse;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;
using AutoMapper;
using FluentValidation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
builder.Services.AddSingleton(autoMapper);
builder.Services.AddScoped<IValidator<Client>, ClientValidator>();
builder.Services.AddScoped<IValidator<Agenda>, AgendaValidator>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IPdfService, PdfService>();
builder.Services.AddScoped<IMailService, MailService>();
builder.Services.AddScoped<IDbService, DbService>();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.WithOrigins(
        "http://korablev-team.ru", "https://korablev-team.ru").AllowAnyMethod()));
builder.Host.UseSystemd();

// --------------------------------------------------------------------------------

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseCors();
    //app.UseHttpsRedirection();
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

// --------------------------------------------------------------------------------

app.MapPost("/tilda", async (HttpContext context, ICustomerService customerService) =>
{
    IFormCollection requestForm = context.Request.Form;
    var data = requestForm.ToDictionary(x => x.Key.ToLower(), x => x.Value.ToString());
    app.Logger.LogInformation(message: LogHelper.RawData(data));

    if (!string.Equals(data.Key("token"), KeyHelper.ApiToken))
    {
        app.Logger.LogInformation($"Unauthorized: post tilda");
        return Results.Unauthorized();
    }

    if (data.Key("test") == "test")
    {
        app.Logger.LogInformation("External testing of a TILDA webhook");
        return Results.Ok("test");
    }

    context.Response.OnCompleted(() => Task.Run(async () =>
    {
        try
        {
            await customerService.HandleTildaAsync(data);
        }
        catch (Exception e)
        {
            app.Logger.LogError($"T ERROR MESSAGE: {e.Message} \n INNER :{e.InnerException} \n SOURCE: {e.Source} \n STACKTRACE: {e.StackTrace}");
        }
    }));

    return Results.Ok();
});

app.MapPost("/yandex", async (HttpContext context, YandexJsonrpc json, ICustomerService customerService) =>
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
            app.Logger.LogError($"Y ERROR MESSAGE: {e.Message} \n INNER :{e.InnerException} \n SOURCE: {e.Source} \n STACKTRACE: {e.StackTrace}");
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

app.MapGet("/nondone", async (string token, IDbService db) =>
{
    if (token != KeyHelper.ApiToken)
    {
        app.Logger.LogInformation($"Unauthorized: get nondone");
        return Results.Unauthorized();
    }

    return Results.Json(await db.FindNonDoneSalesAsync("<br />"));
});

app.MapGet("/team", async (string token, IDbService db) =>
{
    if (token != KeyHelper.ApiToken)
    {
        app.Logger.LogInformation($"Unauthorized: get team");
        return Results.Unauthorized();
    }

    return Results.Json(await db.FindTeamAsync("<br />"));
});

app.MapGet("/wp", async (string token, IDbService db) =>
{
    if (token != KeyHelper.ApiToken)
    {
        app.Logger.LogInformation($"Unauthorized: get wp");
        return Results.Unauthorized();
    }

    return Results.Json(await db.FindWorkoutProgramsAsync("<br />"));
});

// --------------------------------------------------------------------------------

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

// --------------------------------------------------------------------------------

app.Run();