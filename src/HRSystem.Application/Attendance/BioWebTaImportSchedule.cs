namespace HRSystem.Application.Attendance;

public static class BioWebTaImportSchedule
{
    private static readonly IReadOnlyList<TimeOnly> RunTimes =
        Array.AsReadOnly(
        [
            new TimeOnly(8, 5),
            new TimeOnly(8, 15),
            new TimeOnly(8, 30),
            new TimeOnly(17, 45),
            new TimeOnly(18, 0),
            new TimeOnly(21, 0)
        ]);

    public static IReadOnlyList<TimeOnly> WeekdayRunTimes => RunTimes;

    public static DateTimeOffset GetNextRunAtUtc(
        DateTimeOffset nowUtc,
        string timeZoneId)
    {
        var zone = ResolveTimeZone(timeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime;

        for (var dayOffset = 0; dayOffset <= 7; dayOffset++)
        {
            var date = DateOnly.FromDateTime(nowLocal.Date.AddDays(dayOffset));
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            foreach (var time in RunTimes)
            {
                var candidate = DateTime.SpecifyKind(
                    date.ToDateTime(time),
                    DateTimeKind.Unspecified);
                if (candidate <= nowLocal)
                {
                    continue;
                }

                return new DateTimeOffset(
                    candidate,
                    zone.GetUtcOffset(candidate)).ToUniversalTime();
            }
        }

        throw new InvalidOperationException(
            "The next BioWebTA import schedule could not be resolved.");
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (
            string.Equals(id, "Asia/Taipei", StringComparison.Ordinal))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
        }
    }
}
