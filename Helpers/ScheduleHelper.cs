namespace Syracuse;

public static class ScheduleHelper
{
    private static readonly int s_minMinutes = int.Parse(Environment.GetEnvironmentVariable("MIN_MINUTES") ?? throw new InvalidOperationException());
    private static readonly int s_maxMinutes = int.Parse(Environment.GetEnvironmentVariable("MAX_MINUTES") ?? throw new InvalidOperationException());

    public static TimeSpan GetSchedule() => TimeSpan.FromMinutes(new Random().Next(s_minMinutes, s_maxMinutes));
}