using System;
using Avalonia;

namespace saint.Board.ava.utils;

public class MatrixExtension
{
    /// <summary>
    /// Transforms a rectangle through a matrix and returns the axis-aligned bounding box
    /// 通过矩阵变换矩形并返回轴对齐包围盒
    /// </summary>
    public static Rect TransformBounds(Matrix matrix, Rect rect)
    {
        // Transform all four corners
        // 变换四个角点
        var points = new[]
        {
            matrix.Transform(rect.TopLeft),
            matrix.Transform(rect.TopRight),
            matrix.Transform(rect.BottomLeft),
            matrix.Transform(rect.BottomRight)
        };

        // Find min/max coordinates
        // 查找最小/最大坐标
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Transforms a rectangle through a matrix inverse
    /// 通过逆矩阵变换矩形
    /// </summary>
    public static Rect InverseTransformBounds(Matrix matrix, Rect rect)
    {
        return TransformBounds(matrix.Invert(), rect);
    }
    
}