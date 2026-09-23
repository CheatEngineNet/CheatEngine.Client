using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Events;
using CheatEngine.Client.Hotkeys;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;

namespace CheatEngine.Client.Core.Domains.Hotkeys;

/// <summary>Preserves copied hotkey semantics until host callback lifecycle behavior passes the live-host gate.</summary>
internal sealed class UnavailableHotkeyClient : IHotkeyClient
{
	private readonly CoreLifetime? _lifetime;

	internal UnavailableHotkeyClient(CoreLifetime? lifetime = null)
	{
		_lifetime = lifetime;
	}

	public bool TryRegister(HotkeyRegistration registration, HotkeyHandler handler, EventStreamOptions streamOptions,
		[NotNullWhen(true)] out IHotkeyLease? lease, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(handler);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(streamOptions.Capacity);
		lease = null;
		failure = UnavailableCapabilityFailure.Create(_lifetime, ClientCapabilityId.Hotkeys, "Hotkeys",
			"Hotkeys.Register", cancellationToken);
		return false;
	}

	public IHotkeyLease Register(HotkeyRegistration registration, HotkeyHandler handler,
		EventStreamOptions streamOptions,
		CancellationToken cancellationToken = default)
	{
		_ = TryRegister(registration, handler, streamOptions, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<IHotkeyLease>(failure);
	}
}
