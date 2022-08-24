using FluentValidation;
using AutoMapper;

namespace Syracuse;

public record Client
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Name { get; set; } 
}

public class ClientMapper : Profile
{
    public ClientMapper()
    {
        CreateMap<Dictionary<string, string>, Client>()
            .ForMember(dest => dest.Email, 
                opt => opt.MapFrom(src => src.Key("email")))
            .ForMember(dest => dest.Phone, 
                opt => opt.MapFrom(src => src.Key("phone")))
            .ForMember(dest => dest.Name, 
                opt => opt.MapFrom(src => src.Key("name")));
    }
}

public class ClientValidator : AbstractValidator<Client>
{
    public ClientValidator()
    {
        RuleFor(customer => customer.Email)
            .NotEmpty().WithName("Электронная почта")
            .EmailAddress().WithMessage("Вы указали неверный адрес электронной почты");
        RuleFor(customer => customer.Name)
            .NotEmpty().WithName("Имя")
            .Length(2, 20).WithMessage("Укажите корректное имя");
        RuleFor(customer => customer.Phone)
            .NotEmpty().WithName("Номер телефона");
    }
}
