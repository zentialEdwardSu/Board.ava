using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Input;


namespace saint.Board.ava.utils;

public class CanvasTransform
{
    private Matrix _matrix = Matrix.Identity;
    private Point _lastPanPosition;
    private double _zoom = 1.0;
    private const double MaxZoom = 20.0;
    private const double MinZoom = 0.1;
    
    private Point? _gestureStart1;
    private Point? _gestureStart2;
    private double _initialDistance;
    
    // 添加手势处理方法
    public void HandleGesture(IReadOnlyList<PointerPoint> points)
    {
        // var points = e.GetIntermediatePoints(null);

        if (points.Count < 2 || points[0].Pointer.Id == points[1].Pointer.Id) return;
        var pt1 = points[0].Position;
        var pt2 = points[1].Position;
        
        Debug.WriteLine($"{pt1.X}, {pt1.Y}, {pt2.X}, {pt2.Y}");
            
        if (!_gestureStart1.HasValue)
        {
            _gestureStart1 = pt1;
            _gestureStart2 = pt2;
            _initialDistance = Distance(pt1, pt2);
        }
        else
        {
            var currentDistance = Distance(pt1, pt2);
            var scale = currentDistance / _initialDistance;
            var center = MidPoint(pt1, pt2);
                
            Zoom(scale - 1.0, center);
            _initialDistance = currentDistance;
        }
    }
    
    private static double Distance(Point a, Point b) => 
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        
    private static Point MidPoint(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    
    public Point ScreenToCanvas(Point screenPoint) => _matrix.Invert().Transform(screenPoint);
    
    public Point CanvasToScreen(Point canvasPoint) => _matrix.Transform(canvasPoint);
    
    public void Zoom(double delta, Point center,double zoomoffset = 0.1)
    {
        Debug.WriteLine($"Zoom: {delta} at {center}");
        var oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * (delta > 0 ? 1+zoomoffset : 1-zoomoffset), MinZoom, MaxZoom);
        
        var scale = _zoom / oldZoom;
        _matrix = Matrix.CreateTranslation(-center.X, -center.Y) 
                  * Matrix.CreateScale(scale, scale) 
                  * Matrix.CreateTranslation(center.X, center.Y) 
                  * _matrix;
    }
    
    public void Pan(Vector delta)
    {
        Debug.WriteLine($"Pan: {delta.X/_zoom}, {delta.Y/_zoom}");
        _matrix = Matrix.CreateTranslation(delta.X/_zoom, delta.Y/_zoom) * _matrix;
    }
    
    public Matrix TransformMatrix => _matrix;
    public double ZoomLevel => _zoom;
}