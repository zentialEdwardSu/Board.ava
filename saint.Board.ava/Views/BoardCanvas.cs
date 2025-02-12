using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using saint.Board.ava.utils;

namespace saint.Board.ava.Views;

public class BoardCanvas:Control
{    
    // Transform fields
    private CanvasTransform _canvasTransform = new CanvasTransform();
    private bool _isPanning;
    private Point _lastPointerPosition;
    private PointerPointProperties _panStartProperties;
    
    // Performance monitoring fields
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _events; // Event counter for FPS calculation
    private IDisposable? _statusUpdated;
    private Dictionary<int, PointerPoints> _pointers = new(); // hold stroke create by Pen
    private PointerPointProperties? _lastProperties;
    private PointerUpdateKind? _lastNonOtherUpdateKind;// for Mouse button event

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
    
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
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
                            ZoomLevel: {_canvasTransform.ZoomLevel}";
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

    void HandleEvent(PointerEventArgs e)
    {
        _events++;

        // Throttle sampling rate
        if (_threadSleep != 0)
        {
            // Use sleep to prevent too dense sampling
            Thread.Sleep(_threadSleep);
        }
        InvalidateVisual(); // request repaint

        var lastPointer = e.GetCurrentPoint(this);
        _lastProperties = lastPointer.Properties;
        
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
        
        // Handle active input (pen with pressure or non-pen devices)
        if (e.Pointer.Type != PointerType.Pen
            || lastPointer.Properties.Pressure > 0)
        {
            if (!_pointers.TryGetValue(e.Pointer.Id, out var pt))
                _pointers[e.Pointer.Id] = pt = new PointerPoints();
            pt.HandleEvent(e, this);
        }
    }

    public override void Render(DrawingContext context)
    {
        using (context.PushTransform(_canvasTransform.TransformMatrix))
        {
            // draw background 
            var infiniteBounds = new Rect(-100000, -100000, 200000, 200000);
            context.FillRectangle(Brushes.White, infiniteBounds);
        
            // render strokes
            foreach (var pt in _pointers.Values)
                pt.Render(context, _drawOnlyPoints);
        }
    }

    public void ClearBoard()
    {
        _pointers.Clear();
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _pointers.Clear();
            InvalidateVisual();
            return;
        }
        
        var pointer = e.GetCurrentPoint(this);
        
        if (pointer.Properties.IsMiddleButtonPressed || 
            (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && pointer.Properties.IsLeftButtonPressed))
        {
            _isPanning = true;
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
        
        if (_isPanning)
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
        if (_isPanning)
        {
            _isPanning = false;
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
}