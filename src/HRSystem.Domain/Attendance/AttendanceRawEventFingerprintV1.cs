using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Domain.Common;

namespace HRSystem.Domain.Attendance;

public static class AttendanceRawEventFingerprintV1
{
    public const short Version = 1;
    public const string PayloadVersionLiteral = "v1";
    public const int HashLength = 32;
    public const string EventLocalDateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff";

    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static byte[] Compute(
        string sourceSystem,
        string sourcePersonPin,
        string? deviceSerialNumber,
        DateTime eventLocalDateTime,
        int? statusCode,
        int? verifyCode) =>
        SHA256.HashData(CreatePayload(
            sourceSystem,
            sourcePersonPin,
            deviceSerialNumber,
            eventLocalDateTime,
            statusCode,
            verifyCode));

    public static byte[] CreatePayload(
        string sourceSystem,
        string sourcePersonPin,
        string? deviceSerialNumber,
        DateTime eventLocalDateTime,
        int? statusCode,
        int? verifyCode)
    {
        if (eventLocalDateTime.Kind != DateTimeKind.Unspecified)
        {
            throw new DomainValidationException(
                "Event local date/time must have DateTimeKind.Unspecified.");
        }

        var fields = new string?[]
        {
            PayloadVersionLiteral,
            NormalizeRequired(sourceSystem, nameof(sourceSystem)),
            NormalizeRequired(sourcePersonPin, nameof(sourcePersonPin)),
            deviceSerialNumber?.Trim(),
            eventLocalDateTime.ToString(
                EventLocalDateTimeFormat,
                CultureInfo.InvariantCulture),
            statusCode?.ToString(CultureInfo.InvariantCulture),
            verifyCode?.ToString(CultureInfo.InvariantCulture)
        };

        using var payload = new MemoryStream();
        Span<byte> lengthBytes = stackalloc byte[sizeof(uint)];
        foreach (var field in fields)
        {
            if (field is null)
            {
                payload.WriteByte(0x00);
                lengthBytes.Fill(0xFF);
                payload.Write(lengthBytes);
                continue;
            }

            var valueBytes = Utf8.GetBytes(field);
            payload.WriteByte(0x01);
            BinaryPrimitives.WriteUInt32BigEndian(
                lengthBytes,
                checked((uint)valueBytes.Length));
            payload.Write(lengthBytes);
            payload.Write(valueBytes);
        }

        return payload.ToArray();
    }

    public static string ToHex(ReadOnlySpan<byte> fingerprint)
    {
        if (fingerprint.Length != HashLength)
        {
            throw new DomainValidationException(
                $"Attendance raw-event fingerprint must contain {HashLength} bytes.");
        }

        return Convert.ToHexString(fingerprint);
    }

    private static string NormalizeRequired(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{field} is required.");
        }

        return value.Trim();
    }
}
