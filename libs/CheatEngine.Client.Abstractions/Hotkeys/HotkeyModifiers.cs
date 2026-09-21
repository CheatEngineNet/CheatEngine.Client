namespace CheatEngine.Client.Hotkeys;

/// <summary>Specifies modifier keys that participate in a registered target hotkey.</summary>
[Flags]
public enum HotkeyModifiers
{
	/// <summary>No modifier key.</summary>
	None = 0,

	/// <summary>The Alt modifier.</summary>
	Alt = 1,

	/// <summary>The Control modifier.</summary>
	Control = 2,

	/// <summary>The Shift modifier.</summary>
	Shift = 4,

	/// <summary>The Windows modifier.</summary>
	Windows = 8
}
