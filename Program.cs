using Microsoft.Extensions.Logging;

using System.Text;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseHttpsRedirection();

// name, surname?, number, age, height, weight, gender, activity_level

app.MapPost("/tilda", (context) =>
{
    app.Logger.LogDebug("Got a request");
    var requestForm = context.Request.Form;
    if (requestForm["test"] == "test")
    {
        app.Logger.LogInformation("External texting of a Webhook");
        return context.Response.WriteAsync("test");
    }
    PrintFormData(requestForm);
    return Task.CompletedTask;
});


app.MapPost("/merchant", (context) =>
{
    var requestForm = context.Request.Form;
    PrintFormData(requestForm);
    return context.Response.WriteAsync("YES");
});

app.Run();

void PrintFormData(IFormCollection form)
{
    var log = new StringBuilder();
    foreach (var input in form)
        _ = log.AppendLine($"{input.Key} - {input.Value}");
    log[^1] = ' ';
    app.Logger.LogInformation(log.ToString());
}
