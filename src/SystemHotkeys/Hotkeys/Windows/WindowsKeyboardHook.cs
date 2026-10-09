using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace SystemHotkeys.Hotkeys.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsKeyboardHook(ILogger logger) : KeyboardHook(logger)
{
    private NativeMethods.LowLevelKeyboardProc? _proc;

    private volatile Thread? _thread;
    private volatile uint _threadId;
    private nint _hook;

    internal override void Start()
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

    public override void Dispose()
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
            Logger.Warning(
                "Could not install the global keyboard hook (Win32 error {Error}).",
                Marshal.GetLastPInvokeError()
            );
            return;
        }

        Logger.Information("Global keyboard hook installed.");
        ReportAccess(KeyboardHookAccess.Full);

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

            var swallow = wParam switch
            {
                NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN => OnKeyDown(vkCode),
                NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP => OnKeyUp(vkCode),
                _ => false,
            };
            if (swallow)
            {
                return 1;
            }
        }

        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }
}
