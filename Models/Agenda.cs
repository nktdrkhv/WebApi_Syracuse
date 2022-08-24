using FluentValidation;
using AutoMapper;

namespace Syracuse;

public record Agenda
{
    public int Id { get; set; }
    public string? Gender { get; set; }
    public int? Age { get; set; }
    public int? Height { get; set; }
    public int? Weight { get; set; }
    public int? ActivityLevel { get; set; }
    public int? DailyActivity { get; set; }
    public int? Purpouse { get; set; }
    public int? Focus { get; set; }
    public string? Diseases { get; set; }
    public string? Trainer { get; set; }
}

public class AgendaMapper : Profile
{
    public AgendaMapper()
    {
        CreateMap<Dictionary<string, string>, Agenda>()
            .ForMember(dest => dest.Gender,
                opt => opt.MapFrom(src => src.Key("gender").AsValue().AsString())) ////
            .ForMember(dest => dest.Age,
                opt => opt.MapFrom(src => src.Key("age").AsInt()))
            .ForMember(dest => dest.Height,
                opt => opt.MapFrom(src => src.Key("height").AsInt()))
            .ForMember(dest => dest.Weight,
                opt => opt.MapFrom(src => src.Key("weight").AsInt()))
            .ForMember(dest => dest.ActivityLevel,
                opt => opt.MapFrom(src => src.Key("activity_level").AsValue()))
            .ForMember(dest => dest.DailyActivity,
                opt => opt.MapFrom(src => src.Key("daily_activity").AsValue()))
            .ForMember(dest => dest.Diseases,
                opt => opt.MapFrom(src => src.Key("diseases")))
            .ForMember(dest => dest.Purpouse,
                opt => opt.MapFrom(src => src.Key("purpouse").AsValue()))
            .ForMember(dest => dest.Focus,
                opt => opt.MapFrom(src => src.Key("focus").AsValue()))
            .ForMember(dest => dest.Trainer,
                opt => opt.MapFrom(src => src.Key("trainer").AsValue().AsString()));
    }
}

public class AgendaValidator : AbstractValidator<Agenda>
{
    public SaleType SaleType { get; set; }

    public AgendaValidator()
    {
        RuleFor(customer => customer.Gender)
            .NotNull().WithName("Пол")
            .Matches("Мужчина|Женщина").WithMessage("Укажите корректный пол")
            .When(_ => SaleType is SaleType.Coach or SaleType.Standart or SaleType.Pro or SaleType.Begginer or SaleType.Profi);
        RuleFor(customer => customer.Age)
            .NotNull().WithName("Возраст")
            .InclusiveBetween(10, 99).WithMessage("Укажите корректный возраст")
            .When(_ => SaleType is SaleType.Coach or SaleType.Standart or SaleType.Pro);
        RuleFor(customer => customer.Height)
            .NotNull().WithName("Рост")
            .InclusiveBetween(100, 250).WithMessage("Укажите корректный рост")
            .When(_ => SaleType is SaleType.Coach or SaleType.Standart or SaleType.Pro);
        RuleFor(customer => customer.Weight)
            .NotNull().WithName("Вес")
            .InclusiveBetween(30, 300).WithMessage("Укажите корректный вес")
            .When(_ => SaleType is SaleType.Coach or SaleType.Standart or SaleType.Pro);
        RuleFor(customer => customer.ActivityLevel)
            .NotNull().WithMessage("Укажите еженедельную активность")
            .When(_ => SaleType is SaleType.Coach or SaleType.Begginer or SaleType.Profi);
        RuleFor(customer => customer.DailyActivity)
            .NotNull().WithMessage("Укажите уровень активности")
            .When(_ => SaleType is SaleType.Coach or SaleType.Standart or SaleType.Pro);
        RuleFor(customer => customer.Purpouse)
            .NotNull().WithMessage("Укажите цель тренировок")
            .When(_ => SaleType is SaleType.Coach or SaleType.Standart or SaleType.Pro or SaleType.Begginer or SaleType.Profi);
        RuleFor(customer => customer.Focus)
            .NotNull().WithMessage("Укажите акцент группы мышц")
            .When(_ => SaleType is SaleType.Coach or SaleType.Profi);
        RuleFor(customer => customer.Trainer)
            .NotNull().WithMessage("Выберите тренера")
            .When(_ => SaleType is SaleType.Coach);
    }
}
