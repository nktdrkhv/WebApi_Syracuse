using System.Text;
using AutoMapper;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

var mappingConfig = new MapperConfiguration(mc => mc.AddProfile(new CustomerMapper()));
IMapper autoMapper = mappingConfig.CreateMapper();

builder.Services.AddSingleton(autoMapper);
builder.Services.AddScoped<IValidator<Customer>, CustomerValidator>();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapPost("/tilda", async (HttpContext context, IValidator<Customer> validator, IMapper mapper) =>
{
    app.Logger.LogDebug("Got a request");
    var requestForm = context.Request.Form;

    if (requestForm["test"] == "test")
    {
        app.Logger.LogInformation("External testing of a webhook");
        return context.Response.WriteAsync("test");
    }

    PrintFormData(requestForm);

    var customer = mapper.Map<IFormCollection, Customer>(requestForm); //
    var validationResult = await validator.ValidateAsync(customer); //

    if (validationResult.IsValid)
        app.Logger.LogInformation(customer.ToString());
    return Task.CompletedTask;
});

app.Run();

void PrintFormData(IFormCollection form)
{
    var log = new StringBuilder();
    log.Append("Raw input data from a form:");
    foreach (var input in form)
        _ = log.AppendLine($"{input.Key} - {input.Value}");
    log[^1] = ' ';
    app.Logger.LogInformation(log.ToString());
}
