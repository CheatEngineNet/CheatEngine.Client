namespace CheatEngine.Client.Hotkeys;

/// <summary>Describes a platform-neutral virtual-key gesture.</summary>
public readonly record struct HotkeyGesture
{
	/// <summary>Creates a hotkey gesture.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="virtualKey" /> is outside the byte-sized Windows
	///     virtual-key range.
	/// </exception>
	public HotkeyGesture(int virtualKey, HotkeyModifiers modifiers = HotkeyModifiers.None)
	{
		if (virtualKey is < byte.MinValue or > byte.MaxValue)
		{
			throw new ArgumentOutOfRangeException(nameof(virtualKey));
		}

		if ((modifiers & ~(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift |
		                   HotkeyModifiers.Windows)) != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(modifiers));
		}

		VirtualKey = virtualKey;
		Modifiers = modifiers;
	}

	/// <summary>Gets the platform virtual-key code.</summary>
	public int VirtualKey
	{
		get;
	}

	/// <summary>Gets the requested modifier combination.</summary>
	public HotkeyModifiers Modifiers
	{
		get;
	}
}
