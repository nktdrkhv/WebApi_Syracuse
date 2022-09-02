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
        // It's already value but in str 
        CreateMap<Dictionary<string, string>, WorkoutProgram>()
            .ForMember(dest => dest.Gender,
                opt => opt.MapFrom(src => src.Key("gender").AsInt().AsString()))
            .ForMember(dest => dest.ActivityLevel,
                opt => opt.MapFrom(src => src.Key("activity_level").AsInt()))
            .ForMember(dest => dest.Diseases,
                opt => opt.MapFrom(src => src.Key("diseases").ToLower().Trim()))
            .ForMember(dest => dest.Purpouse,
                opt => opt.MapFrom(src => src.Key("purpouse").AsInt()))
            .ForMember(dest => dest.Focus,
                opt => opt.MapFrom(src => src.Key("focus").AsInt()))
            .ForMember(dest => dest.IgnoreDiseases,
                opt => opt.MapFrom(src => src.Key("ignore_diseases").AsInt()));
    }
}