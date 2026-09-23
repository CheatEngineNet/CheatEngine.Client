using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>The outcome of one ownership-checked release attempt, with the SDK fault that made it unavailable, if any.</summary>
internal readonly record struct SymbolLeaseRelease(SymbolLeaseReleaseKind Kind, Exception? Fault);

/// <summary>Activation-owned release path for a custom symbol that Client registered in Cheat Engine.</summary>
/// <remarks>
///     The release delegate runs on Cheat Engine's main thread, resolves the name and unregisters it only when it still
///     maps to the leased address (audit A14-25). <see cref="SymbolLeaseReleaseKind.Released" />,
///     <see cref="SymbolLeaseReleaseKind.Replaced" /> and <see cref="SymbolLeaseReleaseKind.ExternallyRemoved" /> are
///     terminal and release the activation-local name reservation; <see cref="SymbolLeaseReleaseKind.CleanupUnavailable" />
///     keeps the lease active, like a closed dispatch admission, so the hosting cleanup can retry it.
/// </remarks>
internal sealed class SymbolRegistrationLease(
	SymbolRegistration registration,
	ICheatEngineDispatcher dispatcher,
	Action<SymbolRegistrationLease> untrack,
	Func<string, Address, SymbolLeaseRelease> release,
	Action<string> releaseName) : IDetailedSymbolRegistrationLease
{
	private const string _releaseOperation = "Inspection.ReleaseSymbol";

	private readonly ICheatEngineDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	private readonly Lock _gate = new();

	private readonly Func<string, Address, SymbolLeaseRelease> _release =
		release ?? throw new ArgumentNullException(nameof(release));

	private readonly Action<string> _releaseName = releaseName ?? throw new ArgumentNullException(nameof(releaseName));

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

	/// <summary>Gets the outcome of the last release attempt, for diagnostics.</summary>
	internal SymbolLeaseReleaseKind LastReleaseKind
	{
		get;
		private set;
	}

	/// <summary>Releases the lease; an unconfirmed cleanup is reported as an exception so aggregated cleanup sees it.</summary>
	/// <exception cref="CheatEngineOperationException">The ownership check or the unregistration failed.</exception>
	public void Dispose()
	{
		SymbolLeaseRelease outcome = ReleaseCore();
		if (outcome.Kind is SymbolLeaseReleaseKind.Released or SymbolLeaseReleaseKind.Replaced
			or SymbolLeaseReleaseKind.ExternallyRemoved or SymbolLeaseReleaseKind.AlreadyReleased)
		{
			return;
		}

		throw new CheatEngineOperationException(new CheatEngineFailure(
			CheatEngineFailureKind.IndeterminateHostResult,
			_releaseOperation,
			"Cheat Engine did not confirm that the symbol registration is still owned by this lease, so it was not " +
			"removed; the lease stays active for a later cleanup attempt.",
			outcome.Fault,
			CheatEngineHostEffect.CleanupUnconfirmed));
	}

	public SymbolLeaseReleaseKind ReleaseDetailed()
	{
		return ReleaseCore().Kind;
	}

	private SymbolLeaseRelease ReleaseCore()
	{
		lock (_gate)
		{
			if (Volatile.Read(ref _released) != 0)
			{
				LastReleaseKind = SymbolLeaseReleaseKind.AlreadyReleased;
				return new SymbolLeaseRelease(SymbolLeaseReleaseKind.AlreadyReleased, null);
			}

			// Keep the lease active and registered when normal dispatch admission is closed during disable: the
			// dispatcher throws and the hosting cleanup scope can retry this exact release on CE's main thread.
			SymbolLeaseRelease outcome = default;
			_dispatcher.Invoke(() => outcome = _release(Name, Address));
			LastReleaseKind = outcome.Kind;
			if (outcome.Kind is not (SymbolLeaseReleaseKind.Released or SymbolLeaseReleaseKind.Replaced
				or SymbolLeaseReleaseKind.ExternallyRemoved))
			{
				return outcome;
			}

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

			return outcome;
		}
	}
}
