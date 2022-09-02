using AutoMapper;

namespace Syracuse;

public record Worker
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Nickname { get; set; }
    public bool Admin { get; set; }
    public List<Contact>? Contacts { get; set; }
}

public class WorkerMapper : Profile
{
    public WorkerMapper()
    {
        CreateMap<Dictionary<string, string>, Worker>()
            .ForMember(dest => dest.Name,
                opt => opt.MapFrom(src => src.Key("name")))
            .ForMember(dest => dest.Nickname,
                opt => opt.MapFrom(src => src.Key("nickname").ToLower()))
            .ForMember(dest => dest.Admin,
                opt => opt.MapFrom(src => src.Key("is_admin").AsValue().AsBool()));
    }
}