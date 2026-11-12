using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace backend_api.Services;

public static class AcademicDocumentTextExtractor
{
    private const int MaxTextCharacters = 120_000;
    private const long MaxXmlEntryBytes = 8 * 1024 * 1024;
    private const long MaxXmlTotalBytes = 20 * 1024 * 1024;

    public static async Task<string> ReadAsync(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        string text = extension switch
        {
            ".txt" => await File.ReadAllTextAsync(path),
            ".pdf" => ReadPdf(path),
            ".docx" => ReadOfficeXml(path, "word/document.xml"),
            ".pptx" => ReadOfficeXml(path, "ppt/slides/slide"),
            _ => throw new InvalidDataException("Định dạng tài liệu không được hỗ trợ.")
        };

        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("Tài liệu không chứa văn bản có thể trích xuất.");
        return text.Length > MaxTextCharacters ? text[..MaxTextCharacters] : text;
    }

    private static string ReadPdf(string path)
    {
        using var pdf = PdfDocument.Open(path);
        if (pdf.NumberOfPages > 300)
            throw new InvalidDataException("Tài liệu PDF vượt quá 300 trang.");

        var content = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            var pageText = ContentOrderTextExtractor.GetText(page);
            AppendWithinLimit(content, pageText);
            if (content.Length >= MaxTextCharacters) break;
        }
        return content.ToString();
    }

    private static string ReadOfficeXml(string path, string prefix)
    {
        using var archive = ZipFile.OpenRead(path);
        var entries = prefix == "word/document.xml"
            ? archive.Entries.Where(e => e.FullName.Equals(prefix, StringComparison.OrdinalIgnoreCase)).ToList()
            : archive.Entries.Where(e => Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$", RegexOptions.IgnoreCase))
                .OrderBy(e => int.Parse(Regex.Match(e.Name, @"\d+").Value)).ToList();

        if (entries.Count == 0 || entries.Count > 300 || entries.Any(e => e.Length > MaxXmlEntryBytes) ||
            entries.Sum(e => e.Length) > MaxXmlTotalBytes)
            throw new InvalidDataException("Tài liệu Office không hợp lệ hoặc quá lớn để trích xuất.");

        var content = new StringBuilder();
        foreach (var entry in entries)
        {
            using var stream = entry.Open();
            using var xml = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxXmlEntryBytes
            });
            while (xml.Read())
            {
                if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "t" &&
                    (xml.NamespaceURI == "http://schemas.openxmlformats.org/wordprocessingml/2006/main" ||
                     xml.NamespaceURI == "http://schemas.openxmlformats.org/drawingml/2006/main"))
                {
                    AppendWithinLimit(content, xml.ReadElementContentAsString(), separator: " ");
                }
                else if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "p")
                {
                    AppendWithinLimit(content, "\n", separator: "");
                }
                if (content.Length >= MaxTextCharacters) break;
            }
            if (content.Length >= MaxTextCharacters) break;
        }
        return content.ToString();
    }

    private static void AppendWithinLimit(StringBuilder content, string value, string separator = "\n")
    {
        if (string.IsNullOrEmpty(value) || content.Length >= MaxTextCharacters) return;
        if (content.Length > 0 && separator.Length > 0)
            content.Append(separator.AsSpan(0, Math.Min(separator.Length, MaxTextCharacters - content.Length)));
        if (content.Length < MaxTextCharacters)
            content.Append(value.AsSpan(0, Math.Min(value.Length, MaxTextCharacters - content.Length)));
    }
}
