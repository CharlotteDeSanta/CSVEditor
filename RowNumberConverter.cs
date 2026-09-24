using System.Globalization;
using System.Windows.Data;
using CSVEditor.Models;
using CSVEditor.Services;

namespace CSVEditor;

/// <summary>
/// 把 [数据行, 文档] 转换成显示行号。有表头时第一条数据显示为“2”，
/// 与原始文件行号一致，方便对照。
/// </summary>
public sealed class RowNumberConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not CsvRow row || values[1] is not CsvDocument document)
        {
            return string.Empty;
        }

        var index = document.Rows.IndexOf(row);
        if (index < 0)
        {
            return string.Empty;
        }

        return (document.HasHeaderRow ? index + 2 : index + 1).ToString(CultureInfo.InvariantCulture);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
