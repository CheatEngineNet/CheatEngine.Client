using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Activation-owned release path for one explicitly registered application Lua module.</summary>
/// <remarks>
///     <para>
///         The release runs on Cheat Engine's main thread through <see cref="HostResourceLease" />: it calls the module's
///         <see cref="ILuaModule.Unregister" /> and maps the reported <see cref="LuaModuleReleaseOutcome" /> with
///         <see cref="LuaModuleReleaseMapping" />. Dispose never throws; an exception from the module is an unconfirmed
///         cleanup that is never retried.
///     </para>
///     <para>
///         The lease is tracked through the activation delegates <see cref="LuaClient" /> supplies. It leaves them only when
///         the outcome is complete, so a retryable outcome is retried by the activation cleanup and an outcome that requires
///         manual recovery is reported by it (audit Q43). The module's name reservation ends with the lease: once the
///         outcome is not retryable, the same module name and exports can be registered again.
///     </para>
/// </remarks>
internal sealed class LuaModuleLease : HostResourceLease, ILuaModuleLease
{
	private const string ReleaseOperation = "Lua.Release";

	private readonly Lock _gate = new();
	private readonly ILuaModule _module;
	private readonly Action<ILuaModule> _releaseReservation;
	private readonly Action<ILuaModuleLease> _untrack;
	private bool _abandoned;
	private LuaModuleReleaseOutcome? _moduleReleaseOutcome;
	private bool _registered;
	private bool _reservationReleased;

	internal LuaModuleLease(ILuaModule module, ICheatEngineDispatcher dispatcher, ICoreDiagnostics? diagnostics,
		Action<ILuaModuleLease> untrack, Action<ILuaModule> releaseReservation)
		: base(ReleaseOperation, dispatcher, diagnostics)
	{
		_module = module ?? throw new ArgumentNullException(nameof(module));
		_untrack = untrack ?? throw new ArgumentNullException(nameof(untrack));
		_releaseReservation = releaseReservation ?? throw new ArgumentNullException(nameof(releaseReservation));
	}

	public LuaModuleReleaseOutcome? ModuleReleaseOutcome
	{
		get
		{
			lock (_gate)
			{
				return _moduleReleaseOutcome;
			}
		}
	}

	/// <summary>Marks the module as registered while the registration dispatcher callback still owns the main thread.</summary>
	/// <exception cref="InvalidOperationException">The lease was abandoned before registration completed.</exception>
	internal void ConfirmRegistration()
	{
		lock (_gate)
		{
			if (_abandoned)
			{
				throw new InvalidOperationException("The Lua module lease was abandoned before registration completed.");
			}

			_registered = true;
		}
	}

	/// <summary>
	///     Ends a tracked handoff whose <see cref="ILuaModule.Register" /> never completed: nothing is released in Cheat
	///     Engine, the lease leaves the activation and the name reservation ends.
	/// </summary>
	/// <exception cref="InvalidOperationException">The module was registered; only a release can end the lease.</exception>
	internal void AbandonRegistration()
	{
		lock (_gate)
		{
			if (_registered)
			{
				throw new InvalidOperationException(
					"A registered Lua module lease cannot be abandoned without unregistration.");
			}

			if (_abandoned)
			{
				return;
			}

			_abandoned = true;
		}

		try
		{
			_untrack(this);
		}
		finally
		{
			ReleaseReservation();
		}
	}

	protected override LeaseReleaseOutcome ReleaseOnMainThread()
	{
		lock (_gate)
		{
			if (!_registered)
			{
				// Nothing was registered, so nothing can remain in Cheat Engine.
				ReleaseReservation();
				return new LeaseReleaseOutcome(LeaseReleaseKind.AlreadyReleased, CheatEngineHostEffect.NotStarted);
			}
		}

		LuaModuleReleaseOutcome moduleOutcome;
		try
		{
			moduleOutcome = _module.Unregister() ??
							throw new InvalidOperationException("The Lua module reported no release outcome.");
		}
		catch (Exception)
		{
			// The base records an unconfirmed cleanup that is never retried: the lease ends here.
			ReleaseReservation();
			throw;
		}

		lock (_gate)
		{
			_moduleReleaseOutcome = moduleOutcome;
		}

		LeaseReleaseOutcome outcome = LuaModuleReleaseMapping.ToLeaseOutcome(moduleOutcome.Kind);
		if (!outcome.IsRetryable)
		{
			ReleaseReservation();
		}

		if (outcome.IsComplete)
		{
			Untrack();
		}

		return outcome;
	}

	private void Untrack()
	{
		try
		{
			_untrack(this);
		}
		catch (Exception)
		{
			// The release itself completed: an activation that still tracks the lease finds it released at its drain.
		}
	}

	private void ReleaseReservation()
	{
		lock (_gate)
		{
			if (_reservationReleased)
			{
				return;
			}

			_reservationReleased = true;
		}

		_releaseReservation(_module);
	}
}
