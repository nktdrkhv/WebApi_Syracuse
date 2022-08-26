using AutoMapper;

namespace Syracuse;

public record WorkoutProgram
{
    public int Id { get; set; }
    public bool IgnoreDiseases { get; set; }
    public string ProgramPath { get; set; }

    public string? Gender { get; set; }
    public int? ActivityLevel { get; set; }
    public int? Purpouse { get; set; }
    public int? Focus { get; set; }
    public string? Diseases { get; set; }
}

public class WorkoutProgramMapper : Profile
{
    public WorkoutProgramMapper()
    {
        CreateMap<Dictionary<string, string>, WorkoutProgram>()
            .ForMember(dest => dest.Gender,
                opt => opt.MapFrom(src => src.Key("gender").AsValue().AsString())) ////
            .ForMember(dest => dest.ActivityLevel,
                opt => opt.MapFrom(src => src.Key("activity_level").AsValue()))
            .ForMember(dest => dest.Diseases,
                opt => opt.MapFrom(src => src.Key("diseases").ToLower().Trim()))
            .ForMember(dest => dest.Purpouse,
                opt => opt.MapFrom(src => src.Key("purpouse").AsValue()))
            .ForMember(dest => dest.Focus,
                opt => opt.MapFrom(src => src.Key("focus").AsValue()))
            .ForMember(dest => dest.IgnoreDiseases,
                opt => opt.MapFrom(src => src.Key("ignore_diseases").AsValue().AsBool()));
    }
}