using System;
using Avalonia;


namespace saint.Board.ava.utils;

public class CanvasTransform
{
    private Matrix _matrix = Matrix.Identity;
    private Point _lastPanPosition;
    private double _zoom = 1.0;
    private const double MaxZoom = 20.0;
    private const double MinZoom = 0.1;
    
    public Point ScreenToCanvas(Point screenPoint) => _matrix.Invert().Transform(screenPoint);
    
    public Point CanvasToScreen(Point canvasPoint) => _matrix.Transform(canvasPoint);
    
    public void Zoom(double delta, Point center)
    {
        var oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * (delta > 0 ? 1.1 : 0.9), MinZoom, MaxZoom);
        
        var scale = _zoom / oldZoom;
        _matrix = Matrix.CreateTranslation(-center.X, -center.Y) 
                  * Matrix.CreateScale(scale, scale) 
                  * Matrix.CreateTranslation(center.X, center.Y) 
                  * _matrix;
    }
    
    public void Pan(Vector delta)
    {
        _matrix = Matrix.CreateTranslation(delta.X, delta.Y) * _matrix;
    }
    
    public Matrix TransformMatrix => _matrix;
    public double ZoomLevel => _zoom;
}