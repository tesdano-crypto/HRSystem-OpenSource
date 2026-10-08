using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.CompanyCalendars;
using HRSystem.Domain.CompanyCalendars;

namespace HRSystem.Infrastructure.CompanyCalendars;

public sealed class JsonCompanyCalendarManifestReader : ICompanyCalendarManifestReader
{
    private const int MaximumBytes = 256 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<CompanyCalendarManifest> ReadAsync(
        Stream manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var bytes = await ReadBoundedAsync(manifest, cancellationToken);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            throw Invalid("檔案不得包含 UTF-8 BOM。");
        }

        try
        {
            _ = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw Invalid($"檔案必須是有效的 UTF-8（{exception.GetType().Name}）。");
        }

        ManifestDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ManifestDocument>(bytes, JsonOptions)
                ?? throw Invalid("檔案內容不可為 null。");
        }
        catch (JsonException exception)
        {
            throw Invalid($"JSON 結構無效或包含未核准欄位（{exception.GetType().Name}）。");
        }

        var canonical = SerializeCanonical(document);
        if (!bytes.AsSpan().SequenceEqual(canonical))
        {
            var mismatch = FirstMismatch(bytes, canonical);
            throw Invalid(
                $"檔案不符合 UTF-8、LF、兩空格縮排、固定欄位順序與排序的 canonical 格式（byte {mismatch}）。");
        }

        var dates = document.Dates.Select(ParseDate).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new CompanyCalendarManifest(
            document.SchemaVersion,
            document.CalendarYear,
            new CompanyCalendarManifestSource(
                document.Source.Authority,
                document.Source.Title,
                ParseDateOnly(document.Source.PublicationDate, "source.publicationDate"),
                document.Source.Url,
                document.Source.DocumentIdentifier,
                document.Source.ContentSha256),
            document.ManifestVersion,
            dates,
            hash);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var block = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(block, cancellationToken);
            if (count == 0)
            {
                break;
            }

            if (buffer.Length + count > MaximumBytes)
            {
                throw Invalid("檔案不得超過 256 KiB。");
            }

            await buffer.WriteAsync(block.AsMemory(0, count), cancellationToken);
        }

        if (buffer.Length == 0)
        {
            throw Invalid("檔案不可為空白。");
        }

        return buffer.ToArray();
    }

    private static byte[] SerializeCanonical(ManifestDocument document)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        return StrictUtf8.GetBytes(json + "\n");
    }

    private static int FirstMismatch(byte[] actual, byte[] expected)
    {
        var length = Math.Min(actual.Length, expected.Length);
        for (var index = 0; index < length; index++)
        {
            if (actual[index] != expected[index])
            {
                return index;
            }
        }

        return length;
    }

    private static CompanyCalendarManifestDate ParseDate(ManifestDate item)
    {
        var dayType = item.DayType switch
        {
            "NationalHoliday" => CompanyCalendarDayType.NationalHoliday,
            "SubstituteHoliday" => CompanyCalendarDayType.SubstituteHoliday,
            _ => throw Invalid("dates.dayType 只允許 NationalHoliday 或 SubstituteHoliday。")
        };
        return new CompanyCalendarManifestDate(
            ParseDateOnly(item.Date, "dates.date"),
            dayType,
            item.Name,
            item.SourceNote,
            item.SourceReference);
    }

    private static DateOnly ParseDateOnly(string value, string field)
    {
        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            throw Invalid($"{field} 必須使用 yyyy-MM-dd。");
        }

        return date;
    }

    private static ApplicationValidationException Invalid(string detail) =>
        new($"行事曆檔案格式或日期資料不正確：{detail}");

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class ManifestDocument
    {
        [JsonPropertyOrder(1)]
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; init; } = string.Empty;

        [JsonPropertyOrder(2)]
        [JsonPropertyName("calendarYear")]
        public int CalendarYear { get; init; }

        [JsonPropertyOrder(3)]
        [JsonPropertyName("source")]
        public ManifestSource Source { get; init; } = new();

        [JsonPropertyOrder(4)]
        [JsonPropertyName("manifestVersion")]
        public string ManifestVersion { get; init; } = string.Empty;

        [JsonPropertyOrder(5)]
        [JsonPropertyName("dates")]
        public ManifestDate[] Dates { get; init; } = [];
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class ManifestSource
    {
        [JsonPropertyOrder(1)]
        [JsonPropertyName("authority")]
        public string Authority { get; init; } = string.Empty;

        [JsonPropertyOrder(2)]
        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyOrder(3)]
        [JsonPropertyName("publicationDate")]
        public string PublicationDate { get; init; } = string.Empty;

        [JsonPropertyOrder(4)]
        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyOrder(5)]
        [JsonPropertyName("documentIdentifier")]
        public string? DocumentIdentifier { get; init; }

        [JsonPropertyOrder(6)]
        [JsonPropertyName("contentSha256")]
        public string? ContentSha256 { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class ManifestDate
    {
        [JsonPropertyOrder(1)]
        [JsonPropertyName("date")]
        public string Date { get; init; } = string.Empty;

        [JsonPropertyOrder(2)]
        [JsonPropertyName("dayType")]
        public string DayType { get; init; } = string.Empty;

        [JsonPropertyOrder(3)]
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyOrder(4)]
        [JsonPropertyName("sourceNote")]
        public string? SourceNote { get; init; }

        [JsonPropertyOrder(5)]
        [JsonPropertyName("sourceReference")]
        public string? SourceReference { get; init; }
    }
}
