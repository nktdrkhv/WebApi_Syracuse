namespace Syracuse;

public static class ScheduleHelper
{
    private static int _minMinutes = int.Parse(Environment.GetEnvironmentVariable("MIN_MINUTES"));
    private static int _maxMinutes = int.Parse(Environment.GetEnvironmentVariable("MAX_MINUTES"));
    public static TimeSpan GetSchedule() => TimeSpan.FromMinutes(new Random().Next(_minMinutes, _maxMinutes));
}