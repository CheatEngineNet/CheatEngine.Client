namespace CheatEngine.Client.Hotkeys;

/// <summary>Describes one Client-owned hotkey registration.</summary>
public readonly record struct HotkeyRegistration
{
	/// <summary>Creates a hotkey registration.</summary>
	/// <exception cref="ArgumentException"><paramref name="name" /> is blank.</exception>
	public HotkeyRegistration(string name, HotkeyGesture gesture)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
		Gesture = gesture;
	}

	/// <summary>Gets the Client-unique diagnostic name.</summary>
	public string Name
	{
		get;
	}

	/// <summary>Gets the registered hotkey gesture.</summary>
	public HotkeyGesture Gesture
	{
		get;
	}
}
