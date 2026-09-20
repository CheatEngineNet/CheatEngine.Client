using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Activation-owned release path for a custom symbol that Client registered in Cheat Engine.</summary>
internal sealed class SymbolRegistrationLease(
	SymbolRegistration registration,
	ICheatEngineDispatcher dispatcher,
	Action<SymbolRegistrationLease> untrack,
	Action<string> unregisterSymbol,
	Action<string> releaseName) : ISymbolRegistrationLease
{
	private readonly ICheatEngineDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	private readonly Lock _gate = new();
	private readonly Action<string> _releaseName = releaseName ?? throw new ArgumentNullException(nameof(releaseName));

	private readonly Action<string> _unregisterSymbol =
		unregisterSymbol ?? throw new ArgumentNullException(nameof(unregisterSymbol));

	private readonly Action<SymbolRegistrationLease> _untrack =
		untrack ?? throw new ArgumentNullException(nameof(untrack));

	private int _released;

	public string Name
	{
		get;
	} = registration.Name;

	public Address Address
	{
		get;
	} = registration.Address;

	public bool IsReleased => Volatile.Read(ref _released) != 0;

	public void Dispose()
	{
		lock (_gate)
		{
			if (Volatile.Read(ref _released) != 0)
			{
				return;
			}

			// Keep the lease active and registered when normal dispatch admission is closed during disable. The hosting
			// cleanup scope can then retry this exact disposal on CE's main thread before Lua detaches.
			_dispatcher.Invoke(() => _unregisterSymbol(Name));

			try
			{
				_untrack(this);
			}
			finally
			{
				try
				{
					_releaseName(Name);
				}
				finally
				{
					Volatile.Write(ref _released, 1);
				}
			}
		}
	}
}
