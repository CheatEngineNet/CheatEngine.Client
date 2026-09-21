namespace CheatEngine.Client.Hotkeys;

/// <summary>Contains a copied target hotkey activation.</summary>
public readonly record struct HotkeyEvent
{
	/// <summary>Creates a copied hotkey activation.</summary>
	/// <exception cref="ArgumentException"><paramref name="name" /> is blank.</exception>
	public HotkeyEvent(string name, HotkeyGesture gesture, DateTimeOffset occurredAt)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
		Gesture = gesture;
		OccurredAt = occurredAt;
	}

	/// <summary>Gets the Client registration name.</summary>
	public string Name
	{
		get;
	}

	/// <summary>Gets the copied gesture that was activated.</summary>
	public HotkeyGesture Gesture
	{
		get;
	}

	/// <summary>Gets the copied activation timestamp.</summary>
	public DateTimeOffset OccurredAt
	{
		get;
	}
}
