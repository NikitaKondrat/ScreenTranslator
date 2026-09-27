using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ScreenTranslator;

public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---------- низкоуровневый хук (fallback) ----------
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private readonly int _id;
    private readonly uint _vk;
    private readonly bool _ctrl;
    private readonly bool _alt;
    private readonly bool _shift;

    private HwndSource? _source;
    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private bool _keyDown;
    private bool _registered;

    public event Action? Pressed;

    public GlobalHotkey(System.Windows.Window window, int id, Key key,
        bool ctrl, bool alt, bool shift = false)
    {
        _id = id;
        _ctrl = ctrl;
        _alt = alt;
        _shift = shift;
        _vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        // Путь 1: системный RegisterHotKey
        var helper = new WindowInteropHelper(window);
        _source = HwndSource.FromHwnd(helper.Handle);

        bool registered = false;
        if (_source is not null)
        {
            uint modifiers = MOD_NOREPEAT;
            if (ctrl) modifiers |= MOD_CONTROL;
            if (alt) modifiers |= MOD_ALT;
            if (shift) modifiers |= MOD_SHIFT;
            registered = RegisterHotKey(_source.Handle, _id, modifiers, _vk);
        }

        if (registered)
        {
            _registered = true;
            _source!.AddHook(WndProc);
        }
        else
        {
            // Путь 2: хук клавиатуры — ему не мешает «занятость» сочетания
            _proc = HookCallback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                    "Не удалось зарегистрировать горячую клавишу");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                if (vk == (int)_vk)
                {
                    int msg = (int)wParam;
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        if (!_keyDown && ModifiersMatch())
                        {
                            _keyDown = true;
                            Pressed?.Invoke();
                        }
                    }
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        _keyDown = false;
                    }
                }
            }
        }
        catch { }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool ModifiersMatch() =>
        _ctrl == IsDown(VK_CONTROL) &&
        _alt == IsDown(VK_MENU) &&
        _shift == IsDown(VK_SHIFT);

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;

        if (_registered && _source is not null)
        {
            UnregisterHotKey(_source.Handle, _id);
            _source.RemoveHook(WndProc);
            _registered = false;
        }
    }
}
