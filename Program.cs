var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseHttpsRedirection();

app.MapPost("/tilda", (context) =>
{
	var request = context.Request;
	if (request.Form["test"] == "test")
	{
		app.Logger.LogInformation("External texting of a Webhook");
		return context.Response.WriteAsync("test");
	}

	foreach (var input in context.Request.Form)
		Console.WriteLine($"{input.Key} - {input.Value}");

	return Task.CompletedTask;
});

app.Run();
