using System.Runtime.InteropServices;
using Serilog;

namespace SystemHotkeys.Hotkeys;

internal sealed class GlobalKeyboardHook : IDisposable
{
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly HashSet<int> _heldKeys = [];

    private readonly HashSet<int> _swallowedKeys = [];

    private volatile BoundHotkeyCombos _suppressed = BoundHotkeyCombos.None;

    private NativeMethods.LowLevelKeyboardProc? _proc;

    private volatile Thread? _thread;
    private volatile uint _threadId;
    private nint _hook;

    internal GlobalKeyboardHook(ILogger logger) =>
        _logger = logger.ForContext<GlobalKeyboardHook>();

    internal event Action<HotkeyCombo>? HotkeyPressed;

    internal void SetSuppressed(BoundHotkeyCombos suppressed) => _suppressed = suppressed;

    internal void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "SystemHotkeys.KeyboardHook",
        };
        _thread.Start();
    }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            NativeMethods.PostThreadMessageW(_threadId, NativeMethods.WM_QUIT, 0, 0);
        }

        _thread?.Join(TimeSpan.FromSeconds(2));

        _thread = null;
        _threadId = 0;
    }

    private void RunMessageLoop()
    {
        _threadId = NativeMethods.GetCurrentThreadId();

        _proc = HookCallback;
        _hook = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandleW(null),
            0
        );
        if (_hook == 0)
        {
            _logger.Warning(
                "Could not install the global keyboard hook (Win32 error {Error}).",
                Marshal.GetLastPInvokeError()
            );
            return;
        }

        _logger.Information("Global keyboard hook installed.");

        try
        {
            while (NativeMethods.GetMessageW(out var message, 0, 0, 0))
            {
                NativeMethods.TranslateMessage(message);
                NativeMethods.DispatchMessageW(message);
            }
        }
        finally
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
            var vkCode = (int)data.VkCode;

            if (wParam is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                if (HandleKeyDown(vkCode))
                {
                    return 1;
                }
            }
            else if (wParam is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
            {
                bool swallow;
                lock (_gate)
                {
                    _heldKeys.Remove(vkCode);
                    swallow = _swallowedKeys.Remove(vkCode);
                }

                if (swallow)
                {
                    return 1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private bool HandleKeyDown(int vkCode)
    {
        bool isNewPress;
        HashSet<int> snapshot;
        lock (_gate)
        {
            isNewPress = _heldKeys.Add(vkCode);
            snapshot = [.. _heldKeys];

            if (!isNewPress)
            {
                return _swallowedKeys.Contains(vkCode);
            }
        }

        if (HotkeyCombo.TryCreate(snapshot, vkCode) is not { } combo)
        {
            return false;
        }

        _logger.Debug("Detected hotkey {Combo}.", combo.Text);
        HotkeyPressed?.Invoke(combo);

        if (!_suppressed.Contains(combo))
        {
            return false;
        }

        _logger.Information(
            "Suppressing hotkey {Combo}: keeping it from reaching other applications.",
            combo.Text
        );

        lock (_gate)
        {
            _swallowedKeys.Add(vkCode);
        }

        return true;
    }
}
