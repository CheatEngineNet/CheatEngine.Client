using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Events;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Hotkeys;

/// <summary>Registers Client-owned hotkeys for the current activation.</summary>
public interface IHotkeyClient
{
	/// <summary>Tries to register one hotkey and a bounded copied event stream.</summary>
	public bool TryRegister(
		HotkeyRegistration registration,
		HotkeyHandler handler,
		EventStreamOptions streamOptions,
		[NotNullWhen(true)] out IHotkeyLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Registers one hotkey or throws when the capability is unavailable or registration fails.</summary>
	public IHotkeyLease Register(
		HotkeyRegistration registration,
		HotkeyHandler handler,
		EventStreamOptions streamOptions,
		CancellationToken cancellationToken = default);
}
