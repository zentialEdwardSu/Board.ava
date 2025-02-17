﻿using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;
using saint.Board.ava.Views;

namespace saint.Board.ava.utils;

public class PointerPoints
{
    public DateTime LastUpdated { get; private set; } = DateTime.Now;
    public Rect Bounds { get; set; }
    
    private double MaxRadius => GetActivePoints()
        .Select(p => p.Radius)
        .DefaultIfEmpty(0)
        .Max();
    
    private void UpdateBounds(Point newPoint)
    {
        var rect = new Rect(newPoint,new Size(5,5));
        Bounds = Bounds == default(Rect) ? rect : Bounds.Union(rect);
    }
    
    // Represents a single point on canvas with drawing properties
    public struct CanvasPoint
    {
        public IBrush? Brush;       // Brush color for the point
        public Point Point;         // Position coordinates
        public double Radius;       // Base radius before pressure adjustment
        public double? Pressure;    // Pressure value (0.0-1.0)
    }

    private const int BufferSize = 2048; // Max number of points that PointerPoints will Hold
    private readonly CanvasPoint[] _points = new CanvasPoint[BufferSize];
    private int _index; // Current write position
    private int _count; // Valid points count
    
    public bool IsEmpty => _count == 0;
    
    // Smooth
    private Point _lastSmoothedPoint; // Last smoothed position
    private bool _hasPreviousPoint; // Smoothing state flag
    private readonly Queue<Point> _smoothingBuffer = new Queue<Point>(3); // Raw input buffer
    
    // Brush Cache
    private readonly Dictionary<(IBrush, double), Pen> _penCache = new Dictionary<(IBrush, double), Pen>();
    
    public Geometry GetGeometry()
    {
        var path = new PathGeometry();
        using (var ctx = path.Open())
        {
            CanvasPoint? prev = null;
            foreach (var pt in GetActivePoints())
            {
                if (prev.HasValue)
                {
                    ctx.BeginFigure(prev.Value.Point, false);
                    ctx.LineTo(pt.Point);
                    ctx.EndFigure(false);
                }
                prev = pt;
            }
        }
        return path;
    }
    
    public IEnumerable<CanvasPoint> GetActivePoints()
    {
        var startIndex = (_index - _count + BufferSize) % BufferSize;
        for (var i = 0; i < _count; i++)
        {
            var index = (startIndex + i) % BufferSize;
            if (_points[index].Radius > 0)
                yield return _points[index];
        }
    }

    /// <summary>
    /// Main rendering method with performance optimizations
    /// </summary>
    /// <param name="context"></param>
    /// <param name="drawPoints"></param>
    public void Render(DrawingContext context, bool drawPoints)
    {
        if (_count == 0) return;

        // Calculate valid points range
        var startIndex = (_index - _count + BufferSize) % BufferSize;
        CanvasPoint? prev = null;
        
        // Single pass rendering loop
        for (var i = 0; i < _count; i++)
        {
            int currentIndex = (startIndex + i) % BufferSize;
            var pt = _points[currentIndex];
#if DEBUG
            context.DrawRectangle(null,new Pen(Brushes.Aqua,1),Bounds);
#endif
            if (pt.Radius <= 0)
            {
                prev = null; // cut the line
                continue; // skip deleted point
            }
            
            // Line drawing logic
            if (prev.HasValue && !drawPoints)
            {
                // Dynamic pressure calculation
                var pressure = (pt.Pressure ?? prev.Value.Pressure ?? 0.5);
                var thickness = pressure * 10;
                
                if (prev.Value.Brush != null && pt.Brush != null)
                {
                    var pen = GetOrCreatePen(pt.Brush, thickness);
                    context.DrawLine(pen, prev.Value.Point, pt.Point);
                }
            }
            
            // Point Drawing logic
            if (drawPoints && pt.Brush != null)
            {
                var pressure = pt.Pressure ?? 0.5;
                var radius = pressure * pt.Radius;
                context.DrawEllipse(pt.Brush, null, pt.Point, radius, radius);
            }

            prev = pt;
        }
    }

    /// <summary>
    /// Pen object caching system to reduce GC pressure
    /// </summary>
    /// <param name="brush"></param>
    /// <param name="thickness"></param>
    /// <returns></returns>
    private Pen GetOrCreatePen(IBrush brush, double thickness)
    {
        var key = (brush, thickness);
        if (!_penCache.TryGetValue(key, out var pen))
        {
            pen = new Pen(brush, thickness, null, PenLineCap.Round, PenLineJoin.Round);
            _penCache[key] = pen;
        }
        return pen;
    }

    /// <summary>
    /// Input processing with dual-stage smoothing algorithm
    /// </summary>
    /// <param name="rawPoint"></param>
    /// <param name="brush"></param>
    /// <param name="radius"></param>
    /// <param name="pressure"></param>
    private void AddPoint(Point rawPoint, IBrush brush, double radius, float? pressure = null)
    {
        // Stage 1: Weighted average smoothing
        _smoothingBuffer.Enqueue(rawPoint);
        if (_smoothingBuffer.Count > 3) _smoothingBuffer.Dequeue();

        Point smoothed = rawPoint;
        if (_smoothingBuffer.Count > 1)
        {
            // Weighted average calculation (1:2:3)
            double totalWeight = 0;
            double x = 0, y = 0;
            int weight = 1;
            
            foreach (var pt in _smoothingBuffer)
            {
                x += pt.X * weight;
                y += pt.Y * weight;
                totalWeight += weight;
                weight++;
            }
            smoothed = new Point(x / totalWeight, y / totalWeight);
        }

        // Stage 2: Exponential smoothing
        if (_hasPreviousPoint)
        {
            const double alpha = 0.7; // Smoothing factor
            smoothed = new Point(
                smoothed.X * alpha + _lastSmoothedPoint.X * (1 - alpha),
                smoothed.Y * alpha + _lastSmoothedPoint.Y * (1 - alpha));
        }

        // Update smoothing state
        _lastSmoothedPoint = smoothed;
        _hasPreviousPoint = true;
        
        // Store processed point
        _points[_index] = new CanvasPoint 
        { 
            Point = smoothed,
            Brush = brush,
            Radius = radius,
            Pressure = pressure 
        };
        
        // Update buffer pointers
        _index = (_index + 1) % BufferSize;
        _count = System.Math.Min(_count + 1, BufferSize);
    }

    /// <summary>
    /// Pointer event handler with state management
    /// </summary>
    /// <param name="e"></param>
    /// <param name="canvas"></param>
    /// <param name="isErasing"></param>
    public void HandleEvent(PointerEventArgs e, BoardCanvas canvas,bool isErasing)
    {
        e.Handled = true;
        e.PreventGestureRecognition();
        
        if (isErasing)
        {
            // Not add point will use eraser
            return;
        }
        
        var currentPoint = e.GetCurrentPoint(canvas);
        var points = e.GetIntermediatePoints(canvas);
        
        var canvasPoint = canvas.ScreenToCanvas(currentPoint.Position);

        // Reset smoothing state on pen down
        if (e.RoutedEvent == InputElement.PointerPressedEvent)
        {
            _smoothingBuffer.Clear();
            _hasPreviousPoint = false;
            AddPoint(canvasPoint, Brushes.Green, 10);
        }
        else if (e.RoutedEvent == InputElement.PointerReleasedEvent)
        {
            AddPoint(canvasPoint, Brushes.Red, 10);
            _hasPreviousPoint = false;
        }
        else
        {
            for (int c = 0; c < points.Count; c++)
            {
                var pt = points[c];
                AddPoint(canvas.ScreenToCanvas(pt.Position), 
                    c == points.Count - 1 ? Brushes.Blue : Brushes.Black,
                    c == points.Count - 1 ? 5 : 2, 
                    pt.Properties.Pressure);
            }
        }
        LastUpdated = DateTime.Now;
        UpdateBounds(canvasPoint);
    }
    
    public bool ErasePointsInArea(Rect area)
    {
        var startIndex = (_index - _count + BufferSize) % BufferSize;
        
        var removed = false;
        for (var i = 0; i < _count; i++)
        {
            var index = (startIndex + i) % _points.Length;
            if (area.Contains(_points[index].Point))
            {
                // set radius to 0 to mark the point as deleted
                _points[index].Radius = 0;
                removed = true;
            }
        }
        return removed;
    }
}
