namespace HRSystem.Web.Components.Shared;

public static class TaipeiTime
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static string Format(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, Zone).ToString("yyyy-MM-dd HH:mm:ss");

    public static DateTime ToLocalDateTime(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, Zone).DateTime;

    public static DateTimeOffset FromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Zone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"); }
    }
}
