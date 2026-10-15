using System.Runtime.InteropServices;

namespace SystemHotkeys.Hotkeys.Linux;

// x86-64 values: the open flags and the struct layouts below differ on other architectures.
internal static unsafe partial class NativeMethods
{
	private const string LibC = "libc";

	internal const int O_RDONLY = 0x0;
	internal const int O_NONBLOCK = 0x800;
	internal const int O_CLOEXEC = 0x80000;

	internal const int EPERM = 1;
	internal const int EAGAIN = 11;
	internal const int EACCES = 13;

	internal const short POLLIN = 0x1;
	internal const short POLLERR = 0x8;
	internal const short POLLHUP = 0x10;
	internal const short POLLNVAL = 0x20;

	internal const ushort EV_SYN = 0x00;
	internal const ushort EV_KEY = 0x01;
	internal const ushort SYN_REPORT = 0;
	internal const ushort SYN_DROPPED = 3;
	internal const int KEY_MAX = 0x2ff;
	internal const int EV_MAX = 0x1f;

	internal static nuint EVIOCGBIT(int eventType, int length) => EvdevRead(0x20 + eventType, length);

	internal static nuint EVIOCGNAME(int length) => EvdevRead(0x06, length);

	private static nuint EvdevRead(int number, int size) =>
		(nuint)((2u << 30) | ((uint)size << 16) | ((uint)'E' << 8) | (uint)number);

	[StructLayout(LayoutKind.Sequential)]
	internal struct InputEvent
	{
		public long Seconds;
		public long Microseconds;
		public ushort Type;
		public ushort Code;
		public int Value;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct PollFd
	{
		public int Fd;
		public short Events;
		public short ReturnedEvents;
	}

	[LibraryImport(LibC, EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
	internal static partial int Open(string path, int flags);

	[LibraryImport(LibC, EntryPoint = "close")]
	internal static partial int Close(int fd);

	[LibraryImport(LibC, EntryPoint = "read", SetLastError = true)]
	internal static partial nint Read(int fd, void* buffer, nuint count);

	[LibraryImport(LibC, EntryPoint = "poll", SetLastError = true)]
	internal static partial int Poll(PollFd* fds, nuint count, int timeoutMilliseconds);

	[LibraryImport(LibC, EntryPoint = "ioctl", SetLastError = true)]
	internal static partial int Ioctl(int fd, nuint request, void* argument);
}
