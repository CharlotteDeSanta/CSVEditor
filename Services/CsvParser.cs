using System.Text;

namespace CSVEditor.Services;

/// <summary>
/// 高性能 CSV 解析器：按字符状态机处理引号包裹、转义引号、字段内换行与自定义分隔符。
/// 不修改任何数据，保持字段原样（含前导零、日期格式、超大数字）。
/// </summary>
public sealed class CsvParser
{
    private readonly string _delimiter;
    private readonly char _quote;

    public CsvParser(string delimiter, string quoteChar)
    {
        _delimiter = string.IsNullOrEmpty(delimiter) ? "," : delimiter;
        _quote = string.IsNullOrEmpty(quoteChar) ? '"' : quoteChar[0];
    }

    /// <summary>解析开始位置之后的全部记录，支持字段内换行。</summary>
    public List<string[]> Parse(ReadOnlySpan<char> text)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder(64);
        var inQuotes = false;
        var fieldStarted = false;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == _quote)
                {
                    if (i + 1 < text.Length && text[i + 1] == _quote)
                    {
                        field.Append(_quote);
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(c);
                i++;
                continue;
            }

            if (c == _quote && field.Length == 0)
            {
                inQuotes = true;
                fieldStarted = true;
                i++;
                continue;
            }

            if (IsDelimiterAt(text, i))
            {
                fields.Add(fieldStarted ? field.ToString() : string.Empty);
                field.Clear();
                fieldStarted = false;
                i += _delimiter.Length;
                continue;
            }

            if (c is '\r' or '\n')
            {
                fields.Add(fieldStarted ? field.ToString() : string.Empty);
                records.Add(fields.ToArray());
                fields.Clear();
                field.Clear();
                fieldStarted = false;
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                continue;
            }

            field.Append(c);
            fieldStarted = true;
            i++;
        }

        if (fieldStarted || fields.Count > 0)
        {
            fields.Add(fieldStarted ? field.ToString() : string.Empty);
            records.Add(fields.ToArray());
        }

        return records;
    }

    private bool IsDelimiterAt(ReadOnlySpan<char> text, int index)
    {
        if (index + _delimiter.Length > text.Length)
        {
            return false;
        }

        for (var k = 0; k < _delimiter.Length; k++)
        {
            if (text[index + k] != _delimiter[k])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 猜测分隔符：统计首行的候选分隔符出现次数，取列数最多且在后续行中保持一致的那个。
    /// </summary>
    public static string DetectDelimiter(string sample)
    {
        string[] candidates = [",", "\t", ";", "|", ":"];
        var lines = sample.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
        {
            return ",";
        }

        var probe = lines.Take(20).ToArray();
        var best = ",";
        var bestScore = -1;

        foreach (var candidate in candidates)
        {
            var counts = probe.Select(l => CountOutsideQuotes(l, candidate)).ToArray();
            if (counts[0] == 0)
            {
                continue;
            }

            // 一致性：各行分隔符数量是否相同
            var consistent = counts.All(c => c == counts[0]);
            var score = counts[0] * (consistent ? 10 : 1);

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static int CountOutsideQuotes(string line, string delimiter)
    {
        var count = 0;
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
            {
                continue;
            }

            if (i + delimiter.Length <= line.Length &&
                string.CompareOrdinal(line, i, delimiter, 0, delimiter.Length) == 0)
            {
                count++;
                i += delimiter.Length - 1;
            }
        }

        return count;
    }
}
