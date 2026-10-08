namespace HRSystem.Infrastructure.Persistence.Migrations;

public static class AttendanceRawEventFingerprintSqlV1
{
    public const string ScalarFingerprintSql = """
        SELECT HASHBYTES(
            'SHA2_256',
            0x01000000027631 +
            0x01 + CONVERT(binary(4), DATALENGTH(source_value.[Bytes])) + source_value.[Bytes] +
            0x01 + CONVERT(binary(4), DATALENGTH(pin_value.[Bytes])) + pin_value.[Bytes] +
            CASE WHEN @DeviceSerialNumber IS NULL
                THEN 0x00FFFFFFFF
                ELSE 0x01 + CONVERT(binary(4), DATALENGTH(device_value.[Bytes])) + device_value.[Bytes]
            END +
            0x01 + CONVERT(binary(4), DATALENGTH(time_value.[Bytes])) + time_value.[Bytes] +
            CASE WHEN @StatusCode IS NULL
                THEN 0x00FFFFFFFF
                ELSE 0x01 + CONVERT(binary(4), DATALENGTH(status_value.[Bytes])) + status_value.[Bytes]
            END +
            CASE WHEN @VerifyCode IS NULL
                THEN 0x00FFFFFFFF
                ELSE 0x01 + CONVERT(binary(4), DATALENGTH(verify_value.[Bytes])) + verify_value.[Bytes]
            END)
        FROM (VALUES (CONVERT(varbinary(max),
                CONVERT(varchar(max), @SourceSystem COLLATE Latin1_General_100_BIN2_UTF8))))
            source_value([Bytes])
        CROSS JOIN (VALUES (CONVERT(varbinary(max),
                CONVERT(varchar(max), @SourcePersonPin COLLATE Latin1_General_100_BIN2_UTF8))))
            pin_value([Bytes])
        CROSS JOIN (VALUES (CONVERT(varbinary(max),
                CONVERT(varchar(max), @DeviceSerialNumber COLLATE Latin1_General_100_BIN2_UTF8))))
            device_value([Bytes])
        CROSS JOIN (VALUES (CONVERT(varbinary(max),
                CONVERT(char(19), @EventLocalDateTime, 126) + '.' +
                RIGHT('0000000' + CONVERT(varchar(7),
                    DATEPART(NANOSECOND, @EventLocalDateTime) / 100), 7))))
            time_value([Bytes])
        CROSS JOIN (VALUES (CONVERT(varbinary(max),
                CONVERT(varchar(11), @StatusCode))))
            status_value([Bytes])
        CROSS JOIN (VALUES (CONVERT(varbinary(max),
                CONVERT(varchar(11), @VerifyCode))))
            verify_value([Bytes]);
        """;

    public const string PreflightAndBackfillSql = """
        IF EXISTS
        (
            SELECT 1
            FROM [AttendanceRawEvents]
            WHERE [SourceSystem] IS NULL OR [SourceSystem] = N'' OR
                  [SourcePersonPin] IS NULL OR [SourcePersonPin] = N'' OR
                  [EventLocalDateTime] IS NULL OR
                  [SourceSystem] <> LTRIM(RTRIM([SourceSystem])) OR
                  [SourcePersonPin] <> LTRIM(RTRIM([SourcePersonPin])) OR
                  ([DeviceSerialNumber] IS NOT NULL AND
                   [DeviceSerialNumber] <> LTRIM(RTRIM([DeviceSerialNumber])))
        )
            THROW 51001,
                'Attendance raw-event fingerprint preflight found unnormalized or uncomputable source data.',
                1;

        CREATE TABLE #AttendanceRawEventFingerprintV1
        (
            [Id] uniqueidentifier NOT NULL PRIMARY KEY,
            [SourceSystemUtf8] varchar(200)
                COLLATE Latin1_General_100_BIN2_UTF8 NOT NULL,
            [SourcePersonPinUtf8] varchar(80)
                COLLATE Latin1_General_100_BIN2_UTF8 NOT NULL,
            [DeviceSerialNumberUtf8] varchar(80)
                COLLATE Latin1_General_100_BIN2_UTF8 NULL,
            [EventLocalDateTimeText] varchar(27) NOT NULL,
            [StatusCodeText] varchar(11) NULL,
            [VerifyCodeText] varchar(11) NULL
        );

        INSERT INTO #AttendanceRawEventFingerprintV1
        (
            [Id],
            [SourceSystemUtf8],
            [SourcePersonPinUtf8],
            [DeviceSerialNumberUtf8],
            [EventLocalDateTimeText],
            [StatusCodeText],
            [VerifyCodeText]
        )
        SELECT
            [Id],
            [SourceSystem],
            [SourcePersonPin],
            [DeviceSerialNumber],
            CONVERT(char(19), [EventLocalDateTime], 126) + '.' +
                RIGHT('0000000' + CONVERT(varchar(7),
                    DATEPART(NANOSECOND, [EventLocalDateTime]) / 100), 7),
            CONVERT(varchar(11), [StatusCode]),
            CONVERT(varchar(11), [VerifyCode])
        FROM [AttendanceRawEvents];

        IF (SELECT COUNT_BIG(*) FROM #AttendanceRawEventFingerprintV1) <>
           (SELECT COUNT_BIG(*) FROM [AttendanceRawEvents])
            THROW 51002,
                'Attendance raw-event fingerprint UTF-8 preflight was incomplete.',
                1;

        UPDATE raw_event
        SET [SourceFingerprintVersion] = 1,
            [SourceFingerprint] = HASHBYTES(
                'SHA2_256',
                0x01000000027631 +
                0x01 + CONVERT(binary(4), DATALENGTH(encoded.[SourceSystemUtf8])) +
                    CONVERT(varbinary(max), encoded.[SourceSystemUtf8]) +
                0x01 + CONVERT(binary(4), DATALENGTH(encoded.[SourcePersonPinUtf8])) +
                    CONVERT(varbinary(max), encoded.[SourcePersonPinUtf8]) +
                CASE WHEN raw_event.[DeviceSerialNumber] IS NULL
                    THEN 0x00FFFFFFFF
                    ELSE 0x01 + CONVERT(binary(4), DATALENGTH(encoded.[DeviceSerialNumberUtf8])) +
                        CONVERT(varbinary(max), encoded.[DeviceSerialNumberUtf8])
                END +
                0x01 + CONVERT(binary(4), DATALENGTH(encoded.[EventLocalDateTimeText])) +
                    CONVERT(varbinary(max), encoded.[EventLocalDateTimeText]) +
                CASE WHEN raw_event.[StatusCode] IS NULL
                    THEN 0x00FFFFFFFF
                    ELSE 0x01 + CONVERT(binary(4), DATALENGTH(encoded.[StatusCodeText])) +
                        CONVERT(varbinary(max), encoded.[StatusCodeText])
                END +
                CASE WHEN raw_event.[VerifyCode] IS NULL
                    THEN 0x00FFFFFFFF
                    ELSE 0x01 + CONVERT(binary(4), DATALENGTH(encoded.[VerifyCodeText])) +
                        CONVERT(varbinary(max), encoded.[VerifyCodeText])
                END)
        FROM [AttendanceRawEvents] raw_event
        INNER JOIN #AttendanceRawEventFingerprintV1 encoded
            ON encoded.[Id] = raw_event.[Id];

        DROP TABLE #AttendanceRawEventFingerprintV1;

        IF EXISTS
        (
            SELECT 1
            FROM [AttendanceRawEvents]
            WHERE [SourceFingerprintVersion] IS NULL OR
                  [SourceFingerprintVersion] <> 1 OR
                  [SourceFingerprint] IS NULL OR
                  DATALENGTH([SourceFingerprint]) <> 32
        )
            THROW 51002,
                'Attendance raw-event fingerprint backfill was incomplete.',
                1;

        IF EXISTS
        (
            SELECT 1
            FROM [AttendanceRawEvents]
            GROUP BY [SourceSystem], [ExternalEventId]
            HAVING COUNT_BIG(*) > 1 OR COUNT_BIG(DISTINCT [SourceFingerprint]) > 1
        )
            THROW 51003,
                'Attendance source event id has conflicting content.',
                1;

        IF EXISTS
        (
            SELECT 1
            FROM [AttendanceRawEvents]
            GROUP BY [SourceSystem], [SourceFingerprintVersion], [SourceFingerprint]
            HAVING COUNT_BIG(*) > 1
        )
            THROW 51004,
                'Attendance raw-event v1 fingerprint collision requires human review.',
                1;
        """;
}
