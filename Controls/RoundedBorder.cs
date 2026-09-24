using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CSVEditor.Controls;

/// <summary>
/// 带圆角裁剪的 Border。
///
/// 背景色本身会被 WPF 按圆角绘制，但**子元素不会被裁剪**——无边框窗口里如果不裁剪，
/// 内容（表格、工具条的描边）会从圆角处露出来。这里通过重写
/// <see cref="UIElement.GetLayoutClip"/> 让布局系统按圆角矩形裁掉溢出部分。
/// </summary>
public sealed class RoundedBorder : Border
{
    protected override Geometry GetLayoutClip(Size layoutSlotSize)
    {
        var width = ActualWidth > 0 ? ActualWidth : layoutSlotSize.Width;
        var height = ActualHeight > 0 ? ActualHeight : layoutSlotSize.Height;

        if (width <= 0 || height <= 0)
        {
            return Geometry.Empty;
        }

        var radius = CornerRadius;
        if (radius.TopLeft <= 0 && radius.TopRight <= 0 && radius.BottomLeft <= 0 && radius.BottomRight <= 0)
        {
            return new RectangleGeometry(new Rect(0, 0, width, height));
        }

        return new RectangleGeometry(new Rect(0, 0, width, height), radius.TopLeft, radius.TopLeft);
    }
}
