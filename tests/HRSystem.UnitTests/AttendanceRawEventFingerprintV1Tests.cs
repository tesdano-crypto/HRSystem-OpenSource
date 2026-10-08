using HRSystem.Application.Attendance;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Common;

namespace HRSystem.UnitTests;

public sealed class AttendanceRawEventFingerprintV1Tests
{
    [Fact]
    public void Keeps_Database_Version_Separate_From_Payload_Literal()
    {
        Assert.Equal(1, AttendanceRawEventFingerprintV1.Version);
        Assert.Equal("v1", AttendanceRawEventFingerprintV1.PayloadVersionLiteral);
    }

    public static TheoryData<string, string, string?, DateTime, int?, int?, string>
        GoldenVectors => new()
        {
            {
                "BioWebTA", "42", "DEVICE-01", Local(2026, 8, 3, 8, 15),
                0, 1, "A1BAE2DE591F3305BEDE43F3CC350D0A1674C0E0265EBC964D99DCCAF5797BCD"
            },
            {
                "BioWebTA", "00042", "DEVICE-01", Local(2026, 8, 3, 8, 15),
                0, 1, "C1E9740F35EFFD3CB0FC29C98A24805AF7A372A2D51E98B095DA05413CCAD272"
            },
            {
                "BioWebTA", "00042", null, Local(2026, 8, 3, 12, 15),
                null, null, "51909A3CD84F4281C64BD937034F6A30A47C77D3D676F1761D4E52CC9804106A"
            },
            {
                "BioWebTA", "00042", string.Empty, Local(2026, 8, 3, 12, 15),
                null, null, "9D74CAD1B8BF4D7965A9E167A091E2788FD1356743B217FEF152A082843DBA71"
            },
            {
                "來源系統", "員工０１２", "裝置甲", Local(2026, 8, 3, 17, 45, 1, 234),
                -1, -7, "07501A9D4D449D71389E9F53FF6E27DD0737B21F858DFBA8B27ABA682CBFF37A"
            }
        };

    [Theory]
    [MemberData(nameof(GoldenVectors))]
    public void Computes_Approved_Golden_Vector(
        string sourceSystem,
        string pin,
        string? deviceSerial,
        DateTime eventLocalDateTime,
        int? status,
        int? verify,
        string expectedHex)
    {
        var fingerprint = AttendanceRawEventFingerprintV1.Compute(
            sourceSystem,
            pin,
            deviceSerial,
            eventLocalDateTime,
            status,
            verify);

        Assert.Equal(expectedHex, Convert.ToHexString(fingerprint));
        Assert.Equal(32, fingerprint.Length);
    }

    [Fact]
    public void Payload_Uses_Markers_BigEndian_Lengths_And_Fixed_Field_Order()
    {
        var payload = AttendanceRawEventFingerprintV1.CreatePayload(
            "S",
            "0",
            null,
            Local(2026, 8, 3, 8, 15),
            -1,
            null);

        Assert.Equal(
            "01000000027631" +
            "010000000153" +
            "010000000130" +
            "00FFFFFFFF" +
            "010000001B323032362D30382D30335430383A31353A30302E30303030303030" +
            "01000000022D31" +
            "00FFFFFFFF",
            Convert.ToHexString(payload));
    }

    [Fact]
    public void Null_And_Empty_Device_Serials_Have_Different_Fingerprints()
    {
        var time = Local(2026, 8, 3, 12, 15);

        var nullFingerprint = AttendanceRawEventFingerprintV1.Compute(
            "BioWebTA", "00042", null, time, null, null);
        var emptyFingerprint = AttendanceRawEventFingerprintV1.Compute(
            "BioWebTA", "00042", string.Empty, time, null, null);

        Assert.NotEqual(nullFingerprint, emptyFingerprint);
    }

    [Fact]
    public void External_Id_And_Employee_Id_Are_Excluded_From_Fingerprint()
    {
        var first = Event(1, null);
        var second = Event(999, Guid.NewGuid());

        Assert.Equal(first.SourceFingerprint, second.SourceFingerprint);
    }

    [Fact]
    public void Pin_Leading_Zeros_Are_Preserved()
    {
        var withoutZeros = AttendanceRawEventFingerprintV1.Compute(
            "BioWebTA", "42", "D", Local(2026, 8, 3, 8, 15), 0, 1);
        var withZeros = AttendanceRawEventFingerprintV1.Compute(
            "BioWebTA", "00042", "D", Local(2026, 8, 3, 8, 15), 0, 1);

        Assert.NotEqual(withoutZeros, withZeros);
    }

    [Fact]
    public void Requires_Unspecified_Local_DateTime()
    {
        Assert.Throws<DomainValidationException>(() =>
            AttendanceRawEventFingerprintV1.Compute(
                "BioWebTA",
                "00042",
                null,
                DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                null,
                null));
    }

    private static AttendanceRawEvent Event(long externalId, Guid? employeeId) =>
        new(
            Guid.NewGuid(),
            AttendanceSourceSystems.BioWebTa,
            externalId,
            employeeId,
            "00042",
            "DEVICE-01",
            Local(2026, 8, 3, 8, 15),
            0,
            1,
            null,
            new DateTimeOffset(2026, 8, 3, 0, 15, 0, TimeSpan.Zero));

    private static DateTime Local(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second = 0,
        int millisecond = 0) =>
        new(
            year,
            month,
            day,
            hour,
            minute,
            second,
            millisecond,
            DateTimeKind.Unspecified);
}
