namespace CSVEditor.Models;

/// <summary>字段加引号的策略。</summary>
public enum QuotePolicy
{
    /// <summary>仅在必要时加引号（推荐，输出最干净）。</summary>
    Minimal,

    /// <summary>所有字段都加引号。</summary>
    Always,

    /// <summary>从不加引号（危险，含分隔符的数据会被破坏）。</summary>
    Never,
}

/// <summary>换行符风格。</summary>
public enum LineEnding
{
    Crlf,
    Lf,
    Cr,
}

public static class LineEndingExtensions
{
    public static string ToLiteral(this LineEnding ending) => ending switch
    {
        LineEnding.Crlf => "\r\n",
        LineEnding.Lf => "\n",
        _ => "\r",
    };

    public static string ToDisplayName(this LineEnding ending) => ending switch
    {
        LineEnding.Crlf => "CRLF (Windows)",
        LineEnding.Lf => "LF (Unix)",
        _ => "CR (Mac 旧版)",
    };
}
