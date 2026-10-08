using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Domain.Overtime;

namespace HRSystem.Application.Overtime;

public sealed record OvertimeRecognitionSource(
    DateTime? ScheduledEndAt,
    DateTime? ObservedClockOutAt,
    int ObservedOvertimeMinutes,
    DateTime? SuggestedStartAt,
    DateTime? SuggestedEndAt,
    int? SuggestedMinutes,
    int ExcessBeyondApprovalMinutes,
    bool MissingClockOut,
    byte[] Fingerprint)
{
    public OvertimeRecognitionStatus InitialStatus => MissingClockOut
        ? OvertimeRecognitionStatus.NeedsReview
        : OvertimeRecognitionStatus.Pending;
}

public static class OvertimeRecognitionPolicy
{
    public static OvertimeRecognitionSource Build(
        Guid overtimeRequestId,
        Guid employeeId,
        DateOnly workDate,
        OvertimeRequestStatus requestStatus,
        byte[] requestRowVersion,
        DateTime approvedStartAt,
        DateTime approvedEndAt,
        Guid? attendanceResultId,
        byte[]? attendanceRowVersion,
        TimeOnly? scheduledEndTime,
        bool isOvernightShift,
        DateTime? effectiveClockOutAt,
        bool missingClockIn,
        bool missingClockOut)
    {
        DateTime? scheduledEndAt = scheduledEndTime.HasValue
            ? MinuteLocal(workDate.ToDateTime(scheduledEndTime.Value)
                .AddDays(isOvernightShift ? 1 : 0))
            : null;
        DateTime? observed = !missingClockOut && effectiveClockOutAt.HasValue
            ? MinuteLocal(effectiveClockOutAt.Value)
            : null;
        var observedMinutes = scheduledEndAt.HasValue && observed.HasValue
            ? Math.Max(0, checked((int)(observed.Value - scheduledEndAt.Value).TotalMinutes))
            : 0;

        DateTime? suggestedStart = null;
        DateTime? suggestedEnd = null;
        int? suggestedMinutes = null;
        if (scheduledEndAt.HasValue && observed.HasValue)
        {
            suggestedStart = approvedStartAt > scheduledEndAt
                ? approvedStartAt : scheduledEndAt;
            suggestedEnd = observed < approvedEndAt ? observed : approvedEndAt;
            if (suggestedEnd <= suggestedStart)
            {
                suggestedEnd = suggestedStart;
                suggestedMinutes = 0;
            }
            else
            {
                suggestedMinutes = checked((int)
                    (suggestedEnd.Value - suggestedStart.Value).TotalMinutes);
            }
        }

        var excess = observed.HasValue && observed > approvedEndAt
            ? checked((int)(observed.Value - approvedEndAt).TotalMinutes)
            : 0;
        var fingerprint = OvertimeRecognitionFingerprint.Compute(
            overtimeRequestId, employeeId, workDate, requestStatus,
            requestRowVersion, approvedStartAt, approvedEndAt,
            attendanceResultId, attendanceRowVersion,
            effectiveClockOutAt.HasValue
                ? Local(effectiveClockOutAt.Value) : null,
            missingClockIn, missingClockOut, observedMinutes);
        return new(scheduledEndAt, observed, observedMinutes,
            suggestedStart, suggestedEnd, suggestedMinutes, excess,
            missingClockOut || !scheduledEndAt.HasValue || !observed.HasValue,
            fingerprint);
    }

    public static OvertimeRecognitionStatus EffectiveStatus(
        OvertimeRecognition? recognition,
        OvertimeRecognitionSource source,
        out bool isStale) => EffectiveStatus(
            recognition?.Status,
            recognition?.SourceFingerprint,
            source,
            out isStale);

    public static OvertimeRecognitionStatus EffectiveStatus(
        OvertimeRecognitionStatus? recognitionStatus,
        byte[]? recognitionFingerprint,
        OvertimeRecognitionSource source,
        out bool isStale)
    {
        isStale = recognitionStatus.HasValue &&
            (recognitionFingerprint is null ||
             !CryptographicOperations.FixedTimeEquals(
                 recognitionFingerprint, source.Fingerprint));
        if (!recognitionStatus.HasValue) return source.InitialStatus;
        if (recognitionStatus == OvertimeRecognitionStatus.Confirmed && isStale)
            return OvertimeRecognitionStatus.NeedsReview;
        return recognitionStatus.Value;
    }

    private static DateTime Local(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static DateTime MinuteLocal(DateTime value)
    {
        var local = Local(value);
        return new DateTime(local.Year, local.Month, local.Day,
            local.Hour, local.Minute, 0, DateTimeKind.Unspecified);
    }
}

public static class OvertimeRecognitionFingerprint
{
    public static byte[] Compute(
        Guid overtimeRequestId,
        Guid employeeId,
        DateOnly workDate,
        OvertimeRequestStatus requestStatus,
        byte[]? requestRowVersion,
        DateTime approvedStartAt,
        DateTime approvedEndAt,
        Guid? attendanceResultId,
        byte[]? attendanceRowVersion,
        DateTime? finalClockOut,
        bool missingClockIn,
        bool missingClockOut,
        int overstayMinutes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "v1");
        Append(hash, overtimeRequestId.ToString("D"));
        Append(hash, employeeId.ToString("D"));
        Append(hash, workDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(hash, ((byte)requestStatus).ToString(CultureInfo.InvariantCulture));
        Append(hash, requestRowVersion is null ? null : Convert.ToBase64String(requestRowVersion));
        Append(hash, approvedStartAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        Append(hash, approvedEndAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        Append(hash, attendanceResultId?.ToString("D"));
        Append(hash, attendanceRowVersion is null ? null : Convert.ToBase64String(attendanceRowVersion));
        Append(hash, finalClockOut?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        Append(hash, missingClockIn ? "1" : "0");
        Append(hash, missingClockOut ? "1" : "0");
        Append(hash, overstayMinutes.ToString(CultureInfo.InvariantCulture));
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
