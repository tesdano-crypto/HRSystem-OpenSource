using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Domain.Attendance;

namespace HRSystem.Application.Attendance;

public static class AttendanceReviewFingerprint
{
    public static byte[] ComputeNonWorkingDay(Guid employeeId, DateOnly date, bool required,
        AttendanceCalendarClassification classification, Guid? calendarId,
        HRSystem.Domain.CompanyCalendars.CompanyCalendarDayType? dayType,
        IEnumerable<AttendancePunchEvidence> punches)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "non-working-day-punch-v1");
        Append(hash, employeeId.ToString("D"));
        Append(hash, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(hash, required ? "1" : "0");
        Append(hash, classification.ToString());
        Append(hash, calendarId?.ToString("D"));
        Append(hash, dayType?.ToString());
        foreach (var punch in punches.OrderBy(x => x.LocalTime).ThenBy(x => x.Id))
        {
            Append(hash, punch.Id.ToString("D"));
            Append(hash, punch.LocalTime.ToString("O", CultureInfo.InvariantCulture));
            Append(hash, Convert.ToHexString(punch.Fingerprint));
        }
        return hash.GetHashAndReset();
    }

    public static byte[] Compute(
        Guid employeeId,
        DateOnly workDate,
        AttendanceReviewAnomalyType anomalyType,
        DateTime? recognizedClockIn,
        DateTime? recognizedClockOut,
        int lateMinutes,
        int earlyLeaveMinutes,
        int overstayMinutes,
        bool missingClockIn,
        bool missingClockOut)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, employeeId.ToString("D"));
        Append(hash, workDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(hash, ((byte)anomalyType).ToString(CultureInfo.InvariantCulture));
        Append(hash, recognizedClockIn?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        Append(hash, recognizedClockOut?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        Append(hash, lateMinutes.ToString(CultureInfo.InvariantCulture));
        Append(hash, earlyLeaveMinutes.ToString(CultureInfo.InvariantCulture));
        Append(hash, overstayMinutes.ToString(CultureInfo.InvariantCulture));
        Append(hash, missingClockIn ? "1" : "0");
        Append(hash, missingClockOut ? "1" : "0");
        return hash.GetHashAndReset();
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            hash.AppendData([0]);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> header = stackalloc byte[5];
        header[0] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(header[1..], (uint)bytes.Length);
        hash.AppendData(header);
        hash.AppendData(bytes);
    }
}
