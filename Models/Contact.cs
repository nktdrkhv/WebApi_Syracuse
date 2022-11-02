using AutoMapper;

namespace Syracuse;

public class Contact
{
    public int Id { get; set; }
    public Worker Worker { get; set; }
    public ContactType Type { get; set; }
    public string Info { get; set; }
}

public class ContactMapper : Profile
{
    public ContactMapper()
    {
        CreateMap<Dictionary<string, string>, Contact>()
            .ForMember(dest => dest.Info,
                opt => opt.MapFrom(src => src.Key("info")))
            .ForMember(dest => dest.Type,
                opt => opt.MapFrom(src => (ContactType)src.Key("type").AsValue()));
    }
}