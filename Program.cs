using Syracuse;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics;
using AutoMapper;
using FluentValidation;

CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
CultureInfo.CurrentUICulture = new CultureInfo("ru-RU");

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

var mappingConfig = new MapperConfiguration(mc =>
{
    mc.AddProfile(new AgendaMapper());
    mc.AddProfile(new ClientMapper());
    mc.AddProfile(new WorkerMapper());
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

builder.Host.UseSystemd();

// --------------------------------------------------------------------------------

WebApplication app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(options =>
    {
        options.Run(
            context =>
            {
                var exceptionObject = context.Features.Get<IExceptionHandlerFeature>();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                app.Logger.LogWarning($"From Exception fandler: {exceptionObject.Error.Message}\n{exceptionObject.Error.StackTrace}");
                return Task.CompletedTask;
            });
    });
}

// --------------------------------------------------------------------------------


app.MapGet("/", (HttpContext context) =>
{
    app.Logger.LogInformation($"Access to root path: {context.Request.Host}");
    Results.Ok("These Are Not the Droids You Are Looking For");
});

app.MapPost("/tilda", async (HttpContext context, ICustomerService customerService) =>
{
    IFormCollection requestForm = context.Request.Form;
    var data = requestForm.ToDictionary(x => x.Key.ToLower(), x => x.Value.ToString());
    app.Logger.LogInformation(message: LogHelper.RawData(data));

    if (!data.TryGetValue("token", out var token) || !token.ToString().Equals(KeyHelper.ApiToken))
    {
        app.Logger.LogInformation($"Unauthorized: {token} != req {KeyHelper.ApiToken}");
        Results.Unauthorized();
        return;
    }

    if (data.Key("test") == "test")
    {
        app.Logger.LogInformation("External testing of a TILDA webhook");
        Results.Ok("test");
        return;
    }

    try
    {
        await customerService.HandleTildaAsync(data);
    }
    catch (Exception e)
    {
        app.Logger.LogError($"T ERROR TEMP: {e.Message} \n STACKTRACE: {e.StackTrace}");
    }
    finally
    {
        Results.Ok();
    }
});


app.MapPost("/yandex", async (HttpContext context, ICustomerService customerService) =>
{

    IHeaderDictionary headers = context.Request.Headers;
    var data = context.Request.Headers.ToDictionary(list => list.Key.ToLower(), list => Regex.Unescape(list.Value.ToString()));
    app.Logger.LogInformation(message: LogHelper.RawData(data));

    if (!data.TryGetValue("token", out var token) || !token.ToString().Equals(KeyHelper.ApiToken))
    {
        app.Logger.LogInformation($"Unauthorized: {token} != req {KeyHelper.ApiToken}");
        Results.Unauthorized();
        return;
    }

    try
    {
        await customerService.HandleYandexAsync(data);
    }
    catch (Exception e)
    {
        app.Logger.LogError($"Y ERROR TEMP: {e.Message} \n STACKTRACE: {e.StackTrace}");
    }
    finally
    {
        Results.Ok();
    }
});

// --------------------------------------------------------------------------------

app.Run();

app.Logger.LogInformation("Env: API_TOKEN", Environment.GetEnvironmentVariable("API_TOKEN"));
app.Logger.LogInformation("Env: UNIVERSAL_KEY", Environment.GetEnvironmentVariable("UNIVERSAL_KEY"));
app.Logger.LogInformation("Env: MAIL_USER", Environment.GetEnvironmentVariable("MAIL_USER"));
app.Logger.LogInformation("Env: MAIL_PASS", Environment.GetEnvironmentVariable("MAIL_PASS"));
app.Logger.LogInformation("Env: MAIL_FROM_NAME", Environment.GetEnvironmentVariable("MAIL_FROM_NAME"));
app.Logger.LogInformation("Env: MAIL_FROM_ADDR", Environment.GetEnvironmentVariable("MAIL_FROM_ADDR"));

//try
//{
//}
//catch (MailEx�eption ex)
//{

//}
//catch (CustomerEx�eption ex)
//{

//}
//catch (PdfEx�eption ex)
//{

//}
//catch (DbEx�eption ex)
//{

//}