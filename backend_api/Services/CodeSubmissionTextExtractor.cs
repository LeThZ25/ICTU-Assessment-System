using System.IO.Compression;
using System.Text;

namespace backend_api.Services;

/// <summary>
/// Reads submitted source code as inert text for AI suggestions. Never extracts or runs files.
/// </summary>
public static class CodeSubmissionTextExtractor
{
    private const long MaxUploadBytes = 2 * 1024 * 1024;
    private const long MaxArchiveBytes = 8 * 1024 * 1024;
    private const int MaxArchiveEntries = 80;
    private const int MaxAiCharacters = 60_000;

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".java", ".sql", ".cpp", ".h"
    };

    public static async Task<string> ReadAsync(string storedPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storedPath) || !File.Exists(storedPath))
            throw new CodeSourceException(404, "SOURCE_NOT_FOUND", "Tệp mã nguồn không còn trên hệ thống lưu trữ.");

        var extension = Path.GetExtension(storedPath);
        var fileInfo = new FileInfo(storedPath);
        if (fileInfo.Length > MaxUploadBytes)
            throw new CodeSourceException(413, "SOURCE_TOO_LARGE", "Tệp nguồn vượt giới hạn dung lượng được phép.");

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            if (!SourceExtensions.Contains(extension))
                throw new CodeSourceException(422, "UNSUPPORTED_SOURCE", "Định dạng mã nguồn không được hỗ trợ để chấm bằng AI.");

            await using var stream = File.OpenRead(storedPath);
            var text = await ReadTextAsync(stream, MaxAiCharacters, cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
                throw new CodeSourceException(422, "EMPTY_SOURCE", "Mã nguồn nộp lên không có nội dung để chấm.");
            return text;
        }

        try
        {
            using var archive = ZipFile.OpenRead(storedPath);
            if (archive.Entries.Count > MaxArchiveEntries)
                throw new CodeSourceException(413, "ARCHIVE_TOO_LARGE", "Tệp ZIP có quá nhiều mục để phân tích.");

            long declaredBytes = 0;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourceEntries = new List<ZipArchiveEntry>();
            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName.Replace('\\', '/');
                if (path.StartsWith('/') || path.Contains(':') ||
                    path.Split('/').Any(segment => segment is ".." or "."))
                    throw new CodeSourceException(422, "INVALID_ARCHIVE_PATH", "Tệp ZIP chứa đường dẫn không an toàn.");

                if (entry.Length > MaxArchiveBytes - declaredBytes)
                    throw new CodeSourceException(413, "ARCHIVE_TOO_LARGE", "Dung lượng giải nén của ZIP vượt giới hạn 8MB.");
                declaredBytes += entry.Length;

                if (string.IsNullOrEmpty(entry.Name) || !SourceExtensions.Contains(Path.GetExtension(path)))
                    continue;

                if (!paths.Add(path))
                    throw new CodeSourceException(422, "DUPLICATE_SOURCE_PATH", "ZIP chứa nhiều tệp mã nguồn trùng đường dẫn.");
                sourceEntries.Add(entry);
            }

            if (sourceEntries.Count == 0)
                throw new CodeSourceException(422, "NO_SOURCE_FILES", "ZIP không chứa tệp mã nguồn được hỗ trợ (.cs, .py, .java, .sql, .cpp, .h).");

            var content = new StringBuilder();
            var hasNonEmptySource = false;
            foreach (var entry in sourceEntries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                var fileLabel = entry.FullName.Replace('\r', '_').Replace('\n', '_');
                var header = $"\n===== FILE: {fileLabel} =====\n";
                if (content.Length + header.Length >= MaxAiCharacters)
                    throw TooMuchCode();

                content.Append(header);
                using var stream = entry.Open();
                var source = await ReadTextAsync(stream, MaxAiCharacters - content.Length - 1, cancellationToken);
                hasNonEmptySource |= !string.IsNullOrWhiteSpace(source);
                content.Append(source);
                content.Append('\n');
            }

            if (!hasNonEmptySource)
                throw new CodeSourceException(422, "EMPTY_SOURCE", "Mã nguồn trong ZIP không có nội dung để chấm.");

            return content.ToString();
        }
        catch (InvalidDataException)
        {
            throw new CodeSourceException(422, "INVALID_ARCHIVE", "Không thể đọc tệp ZIP mã nguồn.");
        }
    }

    private static async Task<string> ReadTextAsync(Stream stream, int remainingCharacters, CancellationToken cancellationToken)
    {
        // Invalid UTF-8 is rejected instead of being silently replaced by malformed characters.
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var result = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                if (result.Length + count > remainingCharacters)
                    throw TooMuchCode();

                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] == '\uFFFD' ||
                        (char.IsControl(buffer[i]) && buffer[i] is not ('\r' or '\n' or '\t')))
                        throw new CodeSourceException(422, "BINARY_SOURCE", "Tệp mã nguồn chứa dữ liệu nhị phân hoặc ký tự điều khiển không hợp lệ.");
                }

                result.Append(buffer, 0, count);
            }
        }
        catch (DecoderFallbackException)
        {
            throw new CodeSourceException(422, "INVALID_SOURCE_ENCODING", "Tệp mã nguồn không có mã hóa văn bản hợp lệ.");
        }

        return result.ToString();
    }

    private static CodeSourceException TooMuchCode() =>
        new(413, "SOURCE_TOO_LARGE_FOR_AI", "Mã nguồn vượt giới hạn 60.000 ký tự cho một lần gợi ý chấm bằng AI.");
}

public sealed class CodeSourceException : Exception
{
    public int HttpStatusCode { get; }
    public string Code { get; }

    public CodeSourceException(int httpStatusCode, string code, string message) : base(message)
    {
        HttpStatusCode = httpStatusCode;
        Code = code;
    }
}
