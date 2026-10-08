using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Domain.Attendance;

namespace HRSystem.Application.Attendance;

public static class AttendanceCorrectionFingerprint
{
    public static byte[] Compute(DailyAttendanceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "v1");
        Append(hash, result.EmployeeId.ToString("D"));
        Append(hash, result.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(hash, result.Id.ToString("D"));
        Append(hash, Local(result.RawClockInLocalTime));
        Append(hash, Local(result.RawClockOutLocalTime));
        Append(hash, Local(result.EffectiveClockInLocalTime));
        Append(hash, Local(result.EffectiveClockOutLocalTime));
        Append(hash, result.LateSeconds.ToString(CultureInfo.InvariantCulture));
        Append(hash, result.EarlyLeaveSeconds.ToString(CultureInfo.InvariantCulture));
        Append(hash, result.MissingClockIn ? "1" : "0");
        Append(hash, result.MissingClockOut ? "1" : "0");
        Append(hash, result.IsAdjusted ? "1" : "0");
        Append(hash, result.CurrentAdjustmentId?.ToString("D"));
        Append(hash, result.RowVersion.Length == 0
            ? string.Empty
            : Convert.ToBase64String(result.RowVersion));
        return hash.GetHashAndReset();
    }

    private static string? Local(DateTime? value) => value?.ToString(
        "yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

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
