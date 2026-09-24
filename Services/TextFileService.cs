using System.IO;
using System.Text;
using CSVEditor.Models;

namespace CSVEditor.Services;

/// <summary>文本编码探测与按原样写回。</summary>
public static class TextFileService
{
    /// <summary>无 BOM 时的候选编码：优先 UTF-8，其次中文 / 繁体常见编码。</summary>
    private static readonly string[] FallbackEncodings = ["GB18030", "GBK", "Big5", "windows-1252"];

    public static string Decode(byte[] bytes, out Encoding encoding, out bool hasBom)
    {
        if (bytes.Length == 0)
        {
            encoding = new UTF8Encoding(false);
            hasBom = false;
            return string.Empty;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
            hasBom = true;
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(false, true);
            hasBom = true;
            return encoding.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(true, true);
            hasBom = true;
            return encoding.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
        {
            encoding = new UTF32Encoding(false, true);
            hasBom = true;
            return encoding.GetString(bytes, 4, bytes.Length - 4);
        }

        // 严格 UTF-8 校验：能通过就认为是 UTF-8（绝大多数情况）
        try
        {
            var strictUtf8 = new UTF8Encoding(false, true);
            var text = strictUtf8.GetString(bytes);
            encoding = new UTF8Encoding(false);
            hasBom = false;
            return text;
        }
        catch (DecoderFallbackException)
        {
            // 继续尝试本地代码页
        }

        foreach (var name in FallbackEncodings)
        {
            try
            {
                var candidate = Encoding.GetEncoding(name);
                var text = candidate.GetString(bytes);
                encoding = candidate;
                hasBom = false;
                return text;
            }
            catch (Exception)
            {
                // 该编码在当前平台不可用，试下一个
            }
        }

        encoding = Encoding.Latin1;
        hasBom = false;
        return Encoding.Latin1.GetString(bytes);
    }

    public static LineEnding DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0, cr = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf >= lf && crlf >= cr)
        {
            return LineEnding.Crlf;
        }

        return lf >= cr ? LineEnding.Lf : LineEnding.Cr;
    }

    public static void Write(string path, string text, Encoding encoding, bool withBom)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var effective = withBom ? WithBom(encoding) : WithoutBom(encoding);
        File.WriteAllText(path, text, effective);
    }

    public static Encoding WithBom(Encoding encoding)
    {
        if (encoding is UTF8Encoding)
        {
            return new UTF8Encoding(true);
        }

        if (encoding is UnicodeEncoding unicode)
        {
            return new UnicodeEncoding(unicode.CodePage == 1201, true);
        }

        if (encoding.CodePage == Encoding.UTF8.CodePage)
        {
            return new UTF8Encoding(true);
        }

        try
        {
            var clone = (Encoding)encoding.Clone();
            return clone;
        }
        catch (Exception)
        {
            return encoding;
        }
    }

    public static Encoding WithoutBom(Encoding encoding)
    {
        if (encoding is UTF8Encoding)
        {
            return new UTF8Encoding(false);
        }

        if (encoding.CodePage == Encoding.UTF8.CodePage)
        {
            return new UTF8Encoding(false);
        }

        return encoding;
    }

    /// <summary>把编码显示成友好名称。</summary>
    public static string Describe(Encoding encoding, bool hasBom)
    {
        var name = encoding.CodePage switch
        {
            65001 => "UTF-8",
            1200 => "UTF-16 LE",
            1201 => "UTF-16 BE",
            12000 => "UTF-32 LE",
            936 => "GBK / GB18030",
            950 => "Big5",
            1252 => "Windows-1252",
            _ => encoding.WebName,
        };

        return hasBom ? $"{name} (BOM)" : name;
    }
}
