using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScreenTranslator;

public partial class RegionSelector : Window
{
    private Point _start;
    private bool _dragging;

    public Rect SelectedRegion { get; private set; }
    public bool Confirmed { get; private set; }

    public RegionSelector()
    {
        InitializeComponent();
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Loaded += (_, _) => StealFocus();
    }


    private void StealFocus()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var fg = WinApi.GetForegroundWindow();

        if (fg != hwnd && fg != IntPtr.Zero)
        {
            uint fgThread = WinApi.GetWindowThreadProcessId(fg, out _);
            uint myThread = WinApi.GetCurrentThreadId();
            bool attached = fgThread != myThread &&
                            WinApi.AttachThreadInput(myThread, fgThread, true);

            WinApi.SetForegroundWindow(hwnd);
            WinApi.BringWindowToTop(hwnd);

            if (attached) WinApi.AttachThreadInput(myThread, fgThread, false);
        }

        Activate();
        Focus();
    }

    private static class WinApi
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);
    }


    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        _dragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        UpdateRect(_start);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging) UpdateRect(e.GetPosition(this));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        var rect = MakeRect(e.GetPosition(this));

        var topLeft = PointToScreen(new Point(rect.Left, rect.Top));
        var bottomRight = PointToScreen(new Point(rect.Right, rect.Bottom));
        SelectedRegion = new Rect(
            topLeft.X, topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);

        Confirmed = rect.Width > 5 && rect.Height > 5;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Confirmed = false;
            Close();
        }
    }

    private void UpdateRect(Point end)
    {
        var r = MakeRect(end);
        Canvas.SetLeft(SelectionRect, r.X);
        Canvas.SetTop(SelectionRect, r.Y);
        SelectionRect.Width = r.Width;
        SelectionRect.Height = r.Height;
    }

    private Rect MakeRect(Point end) => new Rect(
        Math.Min(_start.X, end.X),
        Math.Min(_start.Y, end.Y),
        Math.Abs(end.X - _start.X),
        Math.Abs(end.Y - _start.Y));
}
