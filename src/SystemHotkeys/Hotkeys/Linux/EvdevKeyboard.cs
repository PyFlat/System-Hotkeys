using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using static SystemHotkeys.Hotkeys.Linux.NativeMethods;

namespace SystemHotkeys.Hotkeys.Linux;

[SupportedOSPlatform("linux")]
internal sealed unsafe class EvdevKeyboard : IDisposable
{
	private const int KeyBitsLength = (KEY_MAX + 1) / 8;

	// Below BTN_MISC: a device with none of these is a mouse, joystick or similar.
	private const int LastKeyboardKey = 0xff;

	private EvdevKeyboard(string path, string name, int fd)
	{
		Path = path;
		Name = name;
		Fd = fd;
	}

	internal string Path { get; }

	internal string Name { get; }

	internal int Fd { get; private set; }

	// Between SYN_DROPPED and the next SYN_REPORT every event is stale.
	internal bool IsResyncing { get; set; }

	// Null with error 0 means the device is not a keyboard.
	internal static EvdevKeyboard? TryOpen(string path, out int error)
	{
		error = 0;
		var fd = Open(path, O_RDONLY | O_NONBLOCK | O_CLOEXEC);
		if (fd < 0)
		{
			error = Marshal.GetLastPInvokeError();
			return null;
		}

		if (!IsKeyboard(fd) || ReadName(fd) is not { } name)
		{
			_ = Close(fd);
			return null;
		}

		return new EvdevKeyboard(path, name, fd);
	}

	// The event count, 0 when nothing is pending, -1 once the device is gone.
	internal int Read(Span<InputEvent> events)
	{
		nint bytes;
		fixed (InputEvent* buffer = events)
		{
			bytes = NativeMethods.Read(Fd, buffer, (nuint)(events.Length * sizeof(InputEvent)));
		}

		if (bytes >= 0)
		{
			return (int)(bytes / sizeof(InputEvent));
		}

		return Marshal.GetLastPInvokeError() == EAGAIN ? 0 : -1;
	}

	public void Dispose()
	{
		if (Fd < 0)
		{
			return;
		}

		_ = Close(Fd);
		Fd = -1;
	}

	private static bool IsKeyboard(int fd)
	{
		var eventBits = stackalloc byte[(EV_MAX + 1) / 8];
		if (Ioctl(fd, EVIOCGBIT(0, (EV_MAX + 1) / 8), eventBits) < 0 || !IsSet(eventBits, EV_KEY))
		{
			return false;
		}

		var keyBits = stackalloc byte[KeyBitsLength];
		if (Ioctl(fd, EVIOCGBIT(EV_KEY, KeyBitsLength), keyBits) < 0)
		{
			return false;
		}

		for (var code = 1; code <= LastKeyboardKey; code++)
		{
			if (IsSet(keyBits, code))
			{
				return true;
			}
		}

		return false;
	}

	private static string? ReadName(int fd)
	{
		const int Length = 256;
		var name = stackalloc byte[Length];
		var written = Ioctl(fd, EVIOCGNAME(Length), name);
		if (written < 0)
		{
			return null;
		}

		var text = new ReadOnlySpan<byte>(name, written);
		var end = text.IndexOf((byte)0);
		return Encoding.UTF8.GetString(end < 0 ? text : text[..end]);
	}

	private static bool IsSet(byte* bits, int bit) => (bits[bit / 8] & (1 << (bit % 8))) != 0;
}
