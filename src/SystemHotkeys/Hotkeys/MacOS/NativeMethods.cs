using System.Runtime.InteropServices;

namespace SystemHotkeys.Hotkeys.MacOS;

/// <summary>
/// Quartz event tap and CoreFoundation run loop P/Invoke surface.
/// </summary>
internal static partial class NativeMethods
{
	private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
	private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
	private const string ApplicationServices =
		"/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

	internal const uint kCGSessionEventTap = 1;
	internal const uint kCGHeadInsertEventTap = 0;
	internal const uint kCGEventTapOptionDefault = 0;
	internal const uint kCGEventTapOptionListenOnly = 1;

	internal const uint kCGEventKeyDown = 10;
	internal const uint kCGEventKeyUp = 11;
	internal const uint kCGEventFlagsChanged = 12;
	internal const uint kCGEventTapDisabledByTimeout = 0xFFFFFFFE;
	internal const uint kCGEventTapDisabledByUserInput = 0xFFFFFFFF;

	internal const uint kCGKeyboardEventAutorepeat = 8;
	internal const uint kCGKeyboardEventKeycode = 9;

	private static readonly Lazy<nint> _defaultRunLoopMode = new(() =>
		Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), "kCFRunLoopDefaultMode"))
	);

	internal delegate nint CGEventTapCallBack(nint proxy, uint type, nint cgEvent, nint userInfo);

	/// <summary>The <c>kCFRunLoopDefaultMode</c> CFStringRef, a data export rather than a function.</summary>
	internal static nint DefaultRunLoopMode => _defaultRunLoopMode.Value;

	[LibraryImport(CoreGraphics)]
	internal static partial nint CGEventTapCreate(
		uint tap,
		uint place,
		uint options,
		ulong eventsOfInterest,
		CGEventTapCallBack callback,
		nint userInfo
	);

	[LibraryImport(CoreGraphics)]
	internal static partial void CGEventTapEnable(nint tap, [MarshalAs(UnmanagedType.U1)] bool enable);

	[LibraryImport(CoreGraphics)]
	internal static partial long CGEventGetIntegerValueField(nint cgEvent, uint field);

	[LibraryImport(CoreGraphics)]
	internal static partial ulong CGEventGetFlags(nint cgEvent);

	[LibraryImport(CoreGraphics)]
	[return: MarshalAs(UnmanagedType.U1)]
	internal static partial bool CGRequestListenEventAccess();

	[LibraryImport(ApplicationServices)]
	[return: MarshalAs(UnmanagedType.U1)]
	internal static partial bool AXIsProcessTrusted();

	[LibraryImport(CoreFoundation)]
	internal static partial nint CFMachPortCreateRunLoopSource(nint allocator, nint port, nint order);

	[LibraryImport(CoreFoundation)]
	internal static partial void CFMachPortInvalidate(nint port);

	[LibraryImport(CoreFoundation)]
	internal static partial nint CFRunLoopGetCurrent();

	[LibraryImport(CoreFoundation)]
	internal static partial void CFRunLoopAddSource(nint runLoop, nint source, nint mode);

	[LibraryImport(CoreFoundation)]
	internal static partial void CFRunLoopRemoveSource(nint runLoop, nint source, nint mode);

	[LibraryImport(CoreFoundation)]
	internal static partial int CFRunLoopRunInMode(
		nint mode,
		double seconds,
		[MarshalAs(UnmanagedType.U1)] bool returnAfterSourceHandled
	);

	[LibraryImport(CoreFoundation)]
	internal static partial void CFRunLoopStop(nint runLoop);

	[LibraryImport(CoreFoundation)]
	internal static partial void CFRelease(nint cf);
}
