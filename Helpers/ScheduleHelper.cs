namespace Syracuse;

public static class ScheduleHelper
{
    // public static DateTimeOffset GetSchedule()
    // {
    //     var now = DateTime.UtcNow + TimeSpan.FromHours(7);
    //     var schedule = now.DayOfWeek switch
    //     {
    //         DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday or DayOfWeek.Friday
    //             => true,

    //     }
    //     return DateTimeOffset.MaxValue;
    // }

    //public static TimeSpan GetSchedule() => TimeSpan.FromHours(new Random().Next(12, 24));
    public static TimeSpan GetSchedule() => TimeSpan.FromSeconds(new Random().Next(70, 120));
}