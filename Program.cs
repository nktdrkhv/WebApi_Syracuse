using AutoMapper;
using FluentValidation;
using Syracuse;
using System.Text;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

var mail = new MailService();
var pdf = new PdfService();
var mappingConfig = new MapperConfiguration(mc => mc.AddProfile(new CustomerMapper()));
IMapper autoMapper = mappingConfig.CreateMapper();

builder.Services.AddSingleton(pdf);
builder.Services.AddSingleton(mail);
builder.Services.AddSingleton(autoMapper);
builder.Services.AddScoped<IValidator<Customer>, CustomerValidator>();

WebApplication app = builder.Build();

app.UseHttpsRedirection();

app.MapPost("/tilda", async (HttpContext context, IValidator<Customer> validator, IMapper mapper, MailService mailSender, PdfService pdfCreator) =>
{
    IFormCollection requestForm = context.Request.Form;

    if (requestForm["test"] == "test")
    {
        app.Logger.LogInformation("External testing of a webhook");
        return context.Response.WriteAsync("test");
    }

    PrintFormData(requestForm);

    Customer customer = mapper.Map<IFormCollection, Customer>(requestForm);
    FluentValidation.Results.ValidationResult validationResult = await validator.ValidateAsync(customer);

    if (validationResult.IsValid)
    {
        app.Logger.LogInformation(message: customer.ToString());
        var programPath = pdfCreator.CreatePdf(customer);
        await mailSender.SendMailAsync(customer.Email, customer.Name, $"Здравствуйте, {customer.Name}, ваша программа готова!", "**красивое оформление**", ("Программа.pdf", programPath));
        app.Logger.LogInformation($"Программа отправлена: {customer.Email}");
    }
    else
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("При вводе данных на сайте, произошла ошибка\n");
        _ = sb.Append(validationResult.ToString());
        await mailSender.SendMailAsync(customer.Email, customer.Name, "Произошла ошибка!", sb.ToString());
        app.Logger.LogWarning("Письмо об ошибке отправлено");
    }

    return Task.CompletedTask;
});

app.Run();

void PrintFormData(IFormCollection form)
{
    var log = new StringBuilder();
    _ = log.AppendLine("Raw input data from a form:");
    foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> input in form)
        _ = log.AppendLine($"{input.Key} - {input.Value}");
    log[^1] = ' ';
    app.Logger.LogInformation(log.ToString());
}
