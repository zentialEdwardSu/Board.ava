using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using saint.Board.ava.utils;

namespace saint.Board.ava.Views;

internal enum CanvasState
{
    Normal = 1,
    Pinching,
    Panning,
}

internal class StaticStroke
{
    public string OriginId { get; set; }
    public RenderTargetBitmap Bitmap { get; set; }
    public Rect Bounds { get; set; }
    public Matrix Transform { get; set; }
}

internal class CanvasImage(IImage? bitmap, Rect bounds, Matrix? transform)
{
    public IImage? Bitmap { get; set; } = bitmap;
    public Rect Bounds { get; set; } = bounds;
    public Matrix Transform { get; set; } = transform ?? Matrix.Identity;

    public CanvasImage(IImage bitmap, Point position)
        : this(bitmap, new Rect(position, bitmap?.Size ?? default), Matrix.Identity)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
    }
    public void Render(DrawingContext context)
    {
        if (Bitmap != null) context.DrawImage(Bitmap, new Rect(Bitmap.Size), Bounds);
    }
}

public class BoardCanvas:UserControl
{    
    // Transform fields
    private CanvasTransform _canvasTransform = new();
    private CanvasState _state = CanvasState.Normal;
    // private bool _isPanning;
    private Point _lastPointerPosition;
    private PointerPointProperties _panStartProperties;
    private PointerEventArgs? _lastMoveEvent;
    private string _dropStatus;
    
    // Performance monitoring fields
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _events; // Event counter for FPS calculation
    private IDisposable? _statusUpdated;
    // private Dictionary<int, PointerPoints> _pointers = new(); // hold stroke create by Pen
    private PointerPointProperties? _lastProperties;
    private PointerUpdateKind? _lastNonOtherUpdateKind;// for Mouse button event
    
    // For gesture
    private Dictionary<int, Point> _activeTouches = new();
    private double _initialDistance;

    public Dictionary<int, Point> ActiveTouch => _activeTouches;
    
    private Dictionary<string, PointerPoints> _activePointers = new();
    private LimitedQueue<StaticStroke> _staticStrokes = new(100); 
    
    // For Images
    private ImageLoader _imageLoader = new();
    private Dictionary<string,CanvasImage> _images = [];
    private Point? _dragStartPosition;
    private CanvasImage? _draggingImage;
    private readonly HashSet<string> _imageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".svg"];
    
    private enum EraserMode { Partial, Full }
    private EraserMode _eraserMode = EraserMode.Full;
    private Rect _eraserArea;
    private bool _isErasing;
    private const double EraserBaseRadius = 10;
    private const double MinEraserRadius = EraserBaseRadius;
    private const double MaxEraserRadius = EraserBaseRadius * 15;
    private const int ErGamma = 2;
    private double EraserRadius => MinEraserRadius + (MaxEraserRadius - MinEraserRadius) * Math.Pow((double?)_lastProperties?.Pressure??0.1, ErGamma);
    
    // For selection
    private List<string> _selectionID = [];

    private int _threadSleep; // Sampling interval control
    public static readonly DirectProperty<BoardCanvas, int> ThreadSleepProperty =
        AvaloniaProperty.RegisterDirect<BoardCanvas, int>(nameof(ThreadSleep), c => c.ThreadSleep, (c, v) => c.ThreadSleep = v);

    public int ThreadSleep
    {
        get => _threadSleep;
        set => SetAndRaise(ThreadSleepProperty, ref _threadSleep, value);
    }

    private bool _drawOnlyPoints; // Points vs lines rendering
    public static readonly DirectProperty<BoardCanvas, bool> DrawOnlyPointsProperty =
        AvaloniaProperty.RegisterDirect<BoardCanvas, bool>(nameof(DrawOnlyPoints), c => c.DrawOnlyPoints, (c, v) => c.DrawOnlyPoints = v);

    public bool DrawOnlyPoints
    {
        get => _drawOnlyPoints;
        set => SetAndRaise(DrawOnlyPointsProperty, ref _drawOnlyPoints, value);
    }

    private string? _status; // use to expose Status information
    public static readonly DirectProperty<BoardCanvas, string?> StatusProperty =
        AvaloniaProperty.RegisterDirect<BoardCanvas, string?>(nameof(Status), c => c.Status, (c, v) => c.Status = v,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public string? Status
    {
        get => _status;
        set => SetAndRaise(StatusProperty, ref _status, value);
    }

    private List<PointerType>? _inputdevices; // inputdevices limitation
    public static readonly DirectProperty<BoardCanvas, List<PointerType>?> InputDevicesProperty =
        AvaloniaProperty.RegisterDirect<BoardCanvas, List<PointerType>?>(nameof(InputDevices), c => c.InputDevices, (c, v) => c.InputDevices = v);
    public List<PointerType>? InputDevices
    {
        get => _inputdevices;
        set => SetAndRaise(InputDevicesProperty, ref _inputdevices, value);
    }

    private double _scalingSoftFactor = 0.1;
    
    public BoardCanvas()
    {
        _isErasing = false;
        AddHandler(DragDrop.DropEvent, Drop);
        AddHandler(DragDrop.DragOverEvent, DragOver);
    }
    
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        _imageLoader.NotificationManager = new WindowNotificationManager(TopLevel.GetTopLevel(this)!);
        
        // Setup status update timer
        _statusUpdated = DispatcherTimer.Run(() =>
        {
            if (_stopwatch.Elapsed.TotalMilliseconds > 250)
            {
                Status = $@"Events per second: {(_events / _stopwatch.Elapsed.TotalSeconds)}
PointerUpdateKind: {_lastProperties?.PointerUpdateKind}
Last PointerUpdateKind != Other: {_lastNonOtherUpdateKind}
IsLeftButtonPressed: {_lastProperties?.IsLeftButtonPressed}
IsRightButtonPressed: {_lastProperties?.IsRightButtonPressed}
IsMiddleButtonPressed: {_lastProperties?.IsMiddleButtonPressed}
IsXButton1Pressed: {_lastProperties?.IsXButton1Pressed}
IsXButton2Pressed: {_lastProperties?.IsXButton2Pressed}
IsBarrelButtonPressed: {_lastProperties?.IsBarrelButtonPressed}
IsEraser: {_lastProperties?.IsEraser}
IsInverted: {_lastProperties?.IsInverted}
Pressure: {_lastProperties?.Pressure}
XTilt: {_lastProperties?.XTilt}
YTilt: {_lastProperties?.YTilt}
Twist: {_lastProperties?.Twist}
ZoomLevel: {_canvasTransform.ZoomLevel}
ActiveTouch: {_activeTouches?.Count}
CanvasState: {_state.ToString()}
StrokesNumber: {_activePointers?.Count}
ImageNumber: {_images?.Count}
MemoryPressure: {GetMemoryPressure()}
DropStatus: {_dropStatus}
EraserMode: {_eraserMode}";
                _stopwatch.Restart();
                _events = 0;
            }

            return true;
        }, TimeSpan.FromMilliseconds(10));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Cleanup timer
        _statusUpdated?.Dispose();
    }

    private void HandleEvent(PointerEventArgs e)
    {
        _events++;

        _isErasing = _lastProperties?.IsEraser ?? false;
        
        // Throttle sampling rate
        if (_threadSleep != 0)
        {
            // Use sleep to prevent too dense sampling
            Thread.Sleep(_threadSleep);
        }
        InvalidateVisual(); // request repaint

        var currentPoint = e.GetCurrentPoint(this);
        _lastProperties = currentPoint.Properties;
        
        // For Mouse, check mouse button event
        // https://reference.avaloniaui.net/api/Avalonia.Input/PointerUpdateKind/
        if (_lastProperties?.PointerUpdateKind != PointerUpdateKind.Other)
        {
            _lastNonOtherUpdateKind = _lastProperties?.PointerUpdateKind;
        }
        
        
        // Input device filtering
        if (!_inputdevices.Contains(e.Pointer.Type))
        {
            return;
        }
        
        var canvasPos = ScreenToCanvas(currentPoint.Position);
        _eraserArea = new Rect(
            canvasPos.X - EraserRadius,
            canvasPos.Y - EraserRadius,
            EraserRadius * 2,
            EraserRadius * 2
        );
        
        if (_isErasing && currentPoint.Properties.Pressure > 0)
        {
            ProcessErasure(currentPoint);
            e.Handled = true;
            return;
        }
        
        // Handle active input (pen with pressure or non-pen devices)
        if (e.Pointer.Type != PointerType.Pen
            || currentPoint.Properties.Pressure > 0)
        {
            if (!_activePointers.TryGetValue(e.Pointer.Id.ToString(), out var pt))
                _activePointers[e.Pointer.Id.ToString()] = pt = new PointerPoints();
            pt.HandleEvent(e, this,_isErasing);
        }
        
        // _dirtyRegion = _dirtyRegion.Union(new Rect(currentPoint.Position, new Size(10,10)));
    }

    public override void Render(DrawingContext context)
    {
        using (context.PushTransform(_canvasTransform.TransformMatrix))
        {
            // draw background 
            var infiniteBounds = new Rect(-100000, -100000, 200000, 200000);
            context.FillRectangle(Brushes.White, infiniteBounds);

            var b = new SolidColorBrush(new Color());
            
            // render Image
            foreach (var (_,img) in _images)
            {
                img.Render(context);
                DrawSelectRect(context,img.Bounds,Brushes.Indigo, Brushes.Blue);
            }

            // render static stroke
            // foreach (var staticStroke in _staticStrokes.Where(staticStroke =>
            //              staticStroke.Transform == _canvasTransform.TransformMatrix))
            // {
            //     context.DrawImage(staticStroke.Bitmap,
            //         new Rect(staticStroke.Bitmap.Size),
            //         staticStroke.Bounds);
            // }
        }

        using (context.PushTransform(_canvasTransform.TransformMatrix))
        {
            foreach (var stroke in _activePointers.Values)
            {
                stroke.Render(context, _drawOnlyPoints);
            }
        
        }

#if DEBUG
        if (_state == CanvasState.Pinching && _activeTouches.Count == 2)
        {
            var center = MidPoint(_activeTouches.Values.First(), _activeTouches.Values.Last());
            var canvasCenter = _canvasTransform.ScreenToCanvas(center);
            
            var brush = new SolidColorBrush(Colors.Red);
            var pen = new Pen(Brushes.Black, 2);
            context.DrawEllipse(brush, pen, canvasCenter, 10, 10);
        }
#endif
        
        if (_isErasing)
        {
            var screenPos = CanvasToScreen(_eraserArea.Center);
            var brush = new SolidColorBrush(Colors.Gray, 0.3);
            context.DrawEllipse(brush, null, screenPos, EraserRadius, EraserRadius);
        }
        
        // TryStaticizeStrokes();
        // _dirtyRegion = default;
    }

    public void ClearBoard()
    {
        _activePointers.Clear();
        InvalidateVisual();
    }
    
    public void ResetView()
    {
        _canvasTransform = new CanvasTransform();
        InvalidateVisual();
    }
    
    // export private transform instants
    public Point ScreenToCanvas(Point screenPoint) => 
        _canvasTransform.ScreenToCanvas(screenPoint);

    public Point CanvasToScreen(Point canvasPoint) => 
        _canvasTransform.CanvasToScreen(canvasPoint);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _activePointers.Clear();
            InvalidateVisual();
            return;
        }
        
        var pointer = e.GetCurrentPoint(this);

        if (pointer.Pointer.Type == PointerType.Touch)
        {
            _activeTouches[pointer.Pointer.Id] = pointer.Position;

            if (_activeTouches.Count == 1)
            {
                _lastPointerPosition = pointer.Position;
            }
            
            // if 2 calculate distance between them
            if (_activeTouches.Count == 2)
            {
                _initialDistance = GetDistanceBetweenPoints(_activeTouches.Values);
            }
        }

        if (pointer.Properties.IsMiddleButtonPressed || 
            (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && pointer.Properties.IsLeftButtonPressed))
        {
            StateTransition(CanvasState.Panning);
            // _state = CanvasState.Panning;
            _lastPointerPosition = pointer.Position;
            _panStartProperties = pointer.Properties;
            e.Handled = true;
            return;
        }

        HandleEvent(e);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _lastMoveEvent = e;
        // Multi-touch gestures
        var pointer = e.GetCurrentPoint(this);
        if (_activeTouches.ContainsKey(pointer.Pointer.Id))
        {
            _activeTouches[pointer.Pointer.Id] = pointer.Position;

            switch (_activeTouches.Count)
            {
                case 1:
                    if (_state == CanvasState.Normal)
                    {
                        StateTransition(CanvasState.Panning);
                    }
                    break;
                case 2:
                {
                    
                    var plist = new List<Point>(_activeTouches.Values);
                    var currentDistance = GetDistanceBetweenPoints(plist);
                    var scale = currentDistance / _initialDistance;
                
                    var center = MidPoint(plist.First(), plist.Last());
                    var screenCenter = _canvasTransform.ScreenToCanvas(center);

                    var zoomlevel = scale - 1;
                    if (zoomlevel != 0)
                    {
                        StateTransition(CanvasState.Pinching); // we don't want to pan when Pinching
                        _canvasTransform.Zoom(zoomlevel, screenCenter,0.02);
                    }

                    _initialDistance = currentDistance;
                    break;
                }
            }
        }

        if (_state == CanvasState.Panning)
        {
            var currentPoint = e.GetCurrentPoint(this);
            var delta = currentPoint.Position - _lastPointerPosition;
        
            // apply pan
            _canvasTransform.Pan(delta);
            _lastPointerPosition = currentPoint.Position;
        
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        
        HandleEvent(e);
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        if (_activeTouches.ContainsKey(pointer.Pointer.Id))
        {
            // remove it
            _activeTouches.Remove(pointer.Pointer.Id);
            
        }
        
        if (_activeTouches.Count == 0)
        {
            StateTransition(CanvasState.Normal);
            e.Handled = true;
            return;
        }
        
        HandleEvent(e);
        base.OnPointerReleased(e);
    }

    // Capture loss handling
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _lastProperties = null;
        base.OnPointerCaptureLost(e);
    }
    
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        var zoomCenter = pointer.Position;
        
        _canvasTransform.Zoom(e.Delta.Y > 0 ? 1 : -1, zoomCenter);
    
        InvalidateVisual();
        e.Handled = true;
    
        base.OnPointerWheelChanged(e);
    }
    
    void DragOver(object? sender, DragEventArgs e)
    {
        
        e.DragEffects = e.DragEffects & (DragDropEffects.Copy);

        // Only allow if the dragged data contains text or filenames.
        if (!e.Data.Contains(DataFormats.Text)
            && !e.Data.Contains(DataFormats.Files))
            e.DragEffects = DragDropEffects.None;
        
        // also check mime type
        if (!e.Data.Contains(DataFormats.Files)) return;
        
        // check if the files are image format
        var files = e.Data.GetFiles()?.ToList();
        if (files == null || files.Count == 0)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        
        foreach (var item in files)
        {
            // exclude floders
            if (item is not IStorageFile file)
            {
                e.DragEffects = DragDropEffects.None;
                break;
            }

            // check if image
            var extension = Path.GetExtension(file.Name);
            if (!string.IsNullOrEmpty(extension) &&
                _imageExtensions.Contains(extension.ToLowerInvariant())) continue;
            e.DragEffects = DragDropEffects.None;
            break;
        }
    }

    async void Drop(object? sender, DragEventArgs e)
    {
        var pos = e.GetPosition(this);
        
        if (e.Data.Contains(DataFormats.Text))
        {
            _dropStatus = $@"{e.Data.GetText()} on ({pos.X}, {pos.Y})";
        }
        else if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles() ?? Array.Empty<IStorageItem>();
            var contentString = "";
            foreach (var f in files)
            {
                if (f is not IStorageFile file) continue;
                var bitmap = await _imageLoader.LoadImageFromFolder(file);
                if (bitmap != null)
                {
                    var ci = new CanvasImage(bitmap,pos);
                    _images.Add(IdGenerator.GenerateUuid(false),ci);
                }
                contentString += $@"{f.Path.ToString()};";
            }
            _dropStatus = $@"{contentString} on ({pos.X}, {pos.Y})";
        }
    }
    
    /// <summary>
    /// Get a dictionary, calculate Euler distance between first ttwo points
    /// </summary>
    /// <param name="points"></param>
    /// <returns></returns>
    private static double GetDistanceBetweenPoints(IEnumerable<Point> points)
    {
        var pointList = new List<Point>(points);
        if (pointList.Count < 2)
            return 0;

        var p1 = pointList[0];
        var p2 = pointList[1];
        return Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
    }
    
    private static Point MidPoint(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private bool StateTransition(CanvasState s)
    {
        // Pinching <-> Normal <-> Panning
        if (_state == CanvasState.Pinching && s == CanvasState.Panning) return false;
        _state = s;
        return true;
    }
    
    public void ToggleEraserMode()
    {
        _eraserMode = _eraserMode == EraserMode.Partial ? EraserMode.Full : EraserMode.Partial;
    }
    
    private void ProcessErasure(PointerPoint currentPoint)
    {
        if (_eraserMode == EraserMode.Partial)
        {
            foreach (var strokeKey in _activePointers.Keys.ToList())
            {
                var stroke = _activePointers[strokeKey];
                var removed = stroke.ErasePointsInArea(_eraserArea);
        
                if (stroke.IsEmpty)
                {
                    _activePointers.Remove(strokeKey);
                }
                else if (removed)
                {
                    InvalidateVisual();
                }
            }
        }
        else
        {
            var strokesToRemove = _activePointers
                .Where(kvp => StrokeIntersectsEraserArea(kvp.Value))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in strokesToRemove)
            {
                _activePointers.Remove(key);
            }

            if (strokesToRemove.Count > 0)
            {
                InvalidateVisual();
            }
        }
        
    }
    
    private bool StrokeIntersectsEraserArea(PointerPoints stroke)
    {
        // exclude
        if (!stroke.Bounds.Intersects(_eraserArea))
            return false;

        var points = stroke.GetActivePoints().Select(p => p.Point).ToList();
        
        // check line segment
        for (var i = 1; i < points.Count; i++)
        {
            if (LineSegmentIntersectsRect(points[i-1], points[i], _eraserArea))
                return true;
        }
        return false;
    }

    private bool LineSegmentIntersectsRect(Point p1, Point p2, Rect rect)
    {
        // Cohen-Sutherland
        var outcode1 = ComputeOutCode(p1, rect);
        var outcode2 = ComputeOutCode(p2, rect);
        
        while (true)
        {
            if ((outcode1 | outcode2) == 0) return true;
            if ((outcode1 & outcode2) != 0) return false;
            
            var outside = outcode1 != 0 ? outcode1 : outcode2;
            var p = CalculateIntersection(p1, p2, outside, rect);
            
            if (outside == outcode1)
            {
                p1 = p;
                outcode1 = ComputeOutCode(p1, rect);
            }
            else
            {
                p2 = p;
                outcode2 = ComputeOutCode(p2, rect);
            }
        }
    }

    private static OutCode ComputeOutCode(Point p, Rect rect)
    {
        var code = OutCode.Inside;
        if (p.X < rect.Left) code |= OutCode.Left;
        else if (p.X > rect.Right) code |= OutCode.Right;
        if (p.Y < rect.Top) code |= OutCode.Top;
        else if (p.Y > rect.Bottom) code |= OutCode.Bottom;
        return code;
    }

    [Flags]
    private enum OutCode
    {
        Inside = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8
    }

    private Point CalculateIntersection(Point p1, Point p2, OutCode clipTo, Rect rect)
    {
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;

        var slopeY = dx == 0 ? double.PositiveInfinity : dy / dx;
        var slopeX = dy == 0 ? double.PositiveInfinity : dx / dy;

        if (clipTo.HasFlag(OutCode.Top))
        {
            double x = p1.X + (rect.Top - p1.Y) * slopeX;
            return new Point(x, rect.Top);
        }
        else if (clipTo.HasFlag(OutCode.Bottom))
        {
            double x = p1.X + (rect.Bottom - p1.Y) * slopeX;
            return new Point(x, rect.Bottom);
        }
        else if (clipTo.HasFlag(OutCode.Right))
        {
            double y = p1.Y + (rect.Right - p1.X) * slopeY;
            return new Point(rect.Right, y);
        }
        else if (clipTo.HasFlag(OutCode.Left))
        {
            double y = p1.Y + (rect.Left - p1.X) * slopeY;
            return new Point(rect.Left, y);
        }

        return p1;
    }
    
    [Obsolete("Deprecated pending refactoring",true)]
    private void TryStaticizeStrokes()
    {
        var now = DateTime.Now;
        var memPressure = GetMemoryPressure();
        var dynamicThreshold = memPressure switch {
            MemoryPressure.High => TimeSpan.FromSeconds(10),
            MemoryPressure.Medium => TimeSpan.FromSeconds(20),
            _ => TimeSpan.FromSeconds(30)
        };
    
        foreach (var kvp in _activePointers.ToList())
        {
            if (now - kvp.Value.LastUpdated > dynamicThreshold)
            {
                // 生成位图
                var stroke = kvp.Value;
                var bmp = RenderToBitmap(stroke);
            
                // 记录变换矩阵
                _staticStrokes.Enqueue(new StaticStroke {
                    OriginId = kvp.Key,
                    Bitmap = bmp,
                    Bounds = stroke.Bounds,
                    Transform = _canvasTransform.TransformMatrix
                });
            
                // 移出活动集合
                _activePointers.Remove(kvp.Key);
            }
        }
    }

    [Obsolete("Deprecated pending refactoring",true)]
    private static RenderTargetBitmap RenderToBitmap(PointerPoints stroke)
    {
        var bounds = stroke.Bounds.Inflate(1);
        var size = new Size(bounds.Width, bounds.Height);
    
        PixelSize pixelSize = new((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height));
        
        var bmp = new RenderTargetBitmap(pixelSize, new Vector(96, 96));

        using var ctx = bmp.CreateDrawingContext();
        ctx.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y));
        stroke.Render(ctx, drawPoints: false);

        return bmp;
    }
    
    private enum MemoryPressure { Low, Medium, High }

    private MemoryPressure GetMemoryPressure()
    {
        var process = Process.GetCurrentProcess();
        var totalMem = process.WorkingSet64;
    
        return totalMem switch {
            > 500_000_000 => MemoryPressure.High,
            > 200_000_000 => MemoryPressure.Medium,
            _ => MemoryPressure.Low
        };
    }
    
    public static void DrawSelectRect(DrawingContext context, Rect rect, IBrush strokeBrush, IBrush vertexBrush)
    {
        const double vertexSize = 15;  
        var pen = new Pen(strokeBrush, 3); 
    
        context.DrawLine(pen, rect.TopLeft, rect.TopRight);      
        context.DrawLine(pen, rect.BottomLeft, rect.BottomRight);
        context.DrawLine(pen, rect.TopLeft, rect.BottomLeft);    
        context.DrawLine(pen, rect.TopRight, rect.BottomRight);  

        void DrawVertex(Point center)
        {
            var rect = new Rect(
                center.X - vertexSize/2,
                center.Y - vertexSize/2,
                vertexSize,
                vertexSize);
            context.FillRectangle(vertexBrush, rect);
        }

        DrawVertex(rect.TopLeft);     
        DrawVertex(rect.TopRight);    
        DrawVertex(rect.BottomLeft);  
        DrawVertex(rect.BottomRight); 
    }
    
}