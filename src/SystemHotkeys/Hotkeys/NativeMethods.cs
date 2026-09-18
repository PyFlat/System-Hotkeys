using System.Runtime.InteropServices;

namespace SystemHotkeys.Hotkeys;

/// <summary>
/// Win32 P/Invoke surface for the global, passive <c>WH_KEYBOARD_LL</c> hook.
/// </summary>
internal static partial class NativeMethods
{
	internal const int WH_KEYBOARD_LL = 13;
	internal const int WM_KEYDOWN = 0x0100;
	internal const int WM_KEYUP = 0x0101;
	internal const int WM_SYSKEYDOWN = 0x0104;
	internal const int WM_SYSKEYUP = 0x0105;
	internal const int WM_QUIT = 0x0012;

	internal delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

	[StructLayout(LayoutKind.Sequential)]
	internal struct KbdLlHookStruct
	{
		internal uint VkCode;
		internal uint ScanCode;
		internal uint Flags;
		internal uint Time;
		internal nint ExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct Point
	{
		internal int X;
		internal int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct Msg
	{
		internal nint Hwnd;
		internal uint Message;
		internal nint WParam;
		internal nint LParam;
		internal uint Time;
		internal Point Pt;
	}

	[LibraryImport("user32.dll", SetLastError = true)]
	internal static partial nint SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

	[LibraryImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool UnhookWindowsHookEx(nint hhk);

	[LibraryImport("user32.dll")]
	internal static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
	internal static partial nint GetModuleHandleW(string? lpModuleName);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool GetMessageW(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool TranslateMessage(in Msg lpMsg);

	[LibraryImport("user32.dll")]
	internal static partial nint DispatchMessageW(in Msg lpMsg);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);

	[LibraryImport("kernel32.dll")]
	internal static partial uint GetCurrentThreadId();
}
