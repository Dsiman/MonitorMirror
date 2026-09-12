using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorMirror;

internal enum TriggerType { None, Keyboard, Mouse }

internal sealed class GlobalInputHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;

    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    private IntPtr _keyboardHookId = IntPtr.Zero;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;

    private bool CtrlDown, AltDown, ShiftDown;

    public bool Capturing { get; set; }

    public event Action<Keys, uint>? KeyCaptured;
    public event Action<MouseButtons, uint>? MouseCaptured;

    public event Action? MatchDown;
    public event Action? MatchUp;

    public TriggerType TriggerType { get; set; } = TriggerType.None;
    public Keys BoundKey { get; set; } = Keys.None;
    public MouseButtons BoundMouseButton { get; set; } = MouseButtons.None;
    public uint BoundModifiers { get; set; }

    private bool _triggerIsDown;

    public GlobalInputHook()
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    public void Install()
    {
        if (_keyboardHookId != IntPtr.Zero) return;
        var hMod = GetModuleHandle(null);
        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
    }

    public void Uninstall()
    {
        if (_keyboardHookId != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHookId); _keyboardHookId = IntPtr.Zero; }
        if (_mouseHookId != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHookId); _mouseHookId = IntPtr.Zero; }
    }

    private static bool IsModifierVk(uint vk) =>
        vk is 0x11 or 0xA2 or 0xA3
           or 0x12 or 0xA4 or 0xA5
           or 0x10 or 0xA0 or 0xA1
           or 0x5B or 0x5C;

    private uint CurrentModifiers()
    {
        uint mods = 0;
        if (CtrlDown) mods |= MOD_CONTROL;
        if (AltDown) mods |= MOD_ALT;
        if (ShiftDown) mods |= MOD_SHIFT;
        return mods;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = data.vkCode;
            int msg = wParam.ToInt32();
            bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool isUp = msg is WM_KEYUP or WM_SYSKEYUP;

            if (vk is 0x11 or 0xA2 or 0xA3) CtrlDown = isDown;
            else if (vk is 0x12 or 0xA4 or 0xA5) AltDown = isDown;
            else if (vk is 0x10 or 0xA0 or 0xA1) ShiftDown = isDown;

            if (Capturing)
            {
                if (isDown && !IsModifierVk(vk))
                    KeyCaptured?.Invoke((Keys)vk, CurrentModifiers());
            }
            else if (TriggerType == TriggerType.Keyboard && vk == (uint)BoundKey)
            {
                if (isDown && !_triggerIsDown && CurrentModifiers() == BoundModifiers)
                {
                    _triggerIsDown = true;
                    MatchDown?.Invoke();
                }
                else if (isUp && _triggerIsDown)
                {
                    _triggerIsDown = false;
                    MatchUp?.Invoke();
                }
            }
        }
        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            MouseButtons button = msg switch
            {
                WM_LBUTTONDOWN or WM_LBUTTONUP => MouseButtons.Left,
                WM_RBUTTONDOWN or WM_RBUTTONUP => MouseButtons.Right,
                WM_MBUTTONDOWN or WM_MBUTTONUP => MouseButtons.Middle,
                _ => MouseButtons.None
            };
            bool isDown = msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN;
            bool isUp = msg is WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP;

            if (button != MouseButtons.None)
            {
                if (Capturing)
                {
                    if (isDown)
                        MouseCaptured?.Invoke(button, CurrentModifiers());
                }
                else if (TriggerType == TriggerType.Mouse && button == BoundMouseButton)
                {
                    if (isDown && !_triggerIsDown && CurrentModifiers() == BoundModifiers)
                    {
                        _triggerIsDown = true;
                        MatchDown?.Invoke();
                    }
                    else if (isUp && _triggerIsDown)
                    {
                        _triggerIsDown = false;
                        MatchUp?.Invoke();
                    }
                }
            }
        }
        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
