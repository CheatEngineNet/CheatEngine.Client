using CheatEngine.Client.Events;

namespace CheatEngine.Client.Hotkeys;

/// <summary>Owns one Client hotkey registration and its bounded copied event stream.</summary>
public interface IHotkeyLease : IEventStreamLease<HotkeyEvent>
{
	/// <summary>Gets the registration owned by this lease.</summary>
	public HotkeyRegistration Registration
	{
		get;
	}
}
