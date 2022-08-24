using Syracuse;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics;
using AutoMapper;
using FluentValidation;

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
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                var exceptionObject = context.Features.Get<IExceptionHandlerFeature>();
                app.Logger.LogWarning($"{exceptionObject.Error.Message}\n{exceptionObject.Error.StackTrace}");
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
    var data = requestForm.ToDictionary(x => x.Key, x => x.Value.ToString());

    if (!data.TryGetValue("token", out var token) || !token.ToString().Equals(KeyHelper.ApiToken))
    {
        app.Logger.LogInformation($"Unauthorized from: {context.Request.Headers["X-Forwarded-For"]}");
        Results.Unauthorized();
        return;
    }

    if (data.Key("test") == "test")
    {
        app.Logger.LogInformation("External testing of a TILDA webhook");
        Results.Ok("test");
        return;
    }

    app.Logger.LogInformation(message: LogHelper.RawData(data));
    await customerService.HandleTildaAsync(data);
    Results.Ok();
});


app.MapPost("/yandex", async (HttpContext context, ICustomerService customerService) =>
{

    IHeaderDictionary headers = context.Request.Headers;
    var data = context.Request.Headers.ToDictionary(list => list.Key, list => Regex.Unescape(list.Value.ToString()));

    if (!data.TryGetValue("token", out var token) || !token.ToString().Equals(KeyHelper.ApiToken))
    {
        app.Logger.LogInformation($"Unauthorized from: {context.Request.Headers["X-Forwarded-For"]}");
        Results.Unauthorized();
        return;
    }

    app.Logger.LogInformation(message: LogHelper.RawData(data));
    await customerService.HandleYandexAsync(data);
    Results.Ok();
});

// --------------------------------------------------------------------------------

app.Run();

//try
//{
//}
//catch (MailExñeption ex)
//{

//}
//catch (CustomerExñeption ex)
//{

//}
//catch (PdfExñeption ex)
//{

//}
//catch (DbExñeption ex)
//{

//}