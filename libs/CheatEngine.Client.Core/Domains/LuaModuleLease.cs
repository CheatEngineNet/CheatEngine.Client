using System.Runtime.ExceptionServices;

using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Activation-owned release path for one explicitly registered application Lua module.</summary>
internal sealed class LuaModuleLease(
	ILuaModule module,
	long epoch,
	ICheatEngineDispatcher dispatcher,
	Func<bool> isActivationCurrent,
	Action<ILuaModuleLease> untrack,
	Action<ILuaModule> releaseModule) : ILuaModuleLease
{
	private readonly ICheatEngineDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	private readonly object _disposeLock = new();

	private readonly Func<bool> _isActivationCurrent =
		isActivationCurrent ?? throw new ArgumentNullException(nameof(isActivationCurrent));

	private readonly ILuaModule _module = module ?? throw new ArgumentNullException(nameof(module));

	private readonly Action<ILuaModule> _releaseModule =
		releaseModule ?? throw new ArgumentNullException(nameof(releaseModule));

	private readonly Action<ILuaModuleLease> _untrack = untrack ?? throw new ArgumentNullException(nameof(untrack));
	private int _registered;
	private int _released;

	public long Epoch
	{
		get;
	} = epoch;

	public bool IsReleased => Volatile.Read(ref _released) != 0;

	public void Dispose()
	{
		lock (_disposeLock)
		{
			if (Volatile.Read(ref _released) != 0)
			{
				return;
			}

			if (Volatile.Read(ref _registered) == 0)
			{
				CompleteRelease();
				return;
			}

			// A detached activation has no legal SDK dispatch path left. There is nothing more that this lease can
			// safely do, so release managed ownership without attempting to call Cheat Engine.
			if (!_isActivationCurrent())
			{
				CompleteRelease();
				return;
			}

			LuaModuleReleaseOutcome? outcome = null;
			_dispatcher.Invoke(() =>
			{
				outcome = _module.Unregister();
			});

			// A release that could not begin leaves the registration with the module: the lease stays active so a later
			// dispose, or the activation cleanup, tries again.
			if (outcome is not null && new LeaseReleaseOutcome(outcome.Kind, CheatEngineHostEffect.Unknown).IsRetryable)
			{
				throw new CheatEngineOperationException(new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable,
					"Lua.UnregisterModule", $"The Lua module release could not begin ({outcome.Kind}); it will be retried.",
					null, CheatEngineHostEffect.NotStarted));
			}

			// Do not make any ownership transition until the application module confirmed unregistration by returning.
			// The write occurs while holding the lock, so another disposer either observes the completed release or
			// waits and retries after the original dispatch fault.
			CompleteRelease();
		}
	}

	/// <summary>Marks the module as registered while the registration dispatcher callback still owns the main thread.</summary>
	internal void ConfirmRegistration()
	{
		lock (_disposeLock)
		{
			if (Volatile.Read(ref _released) != 0)
			{
				throw new InvalidOperationException("The Lua module lease was released before registration completed.");
			}

			Volatile.Write(ref _registered, 1);
		}
	}

	/// <summary>Releases a tracked handoff that never completed <see cref="ILuaModule.Register" />.</summary>
	internal void AbandonRegistration()
	{
		lock (_disposeLock)
		{
			if (Volatile.Read(ref _released) != 0)
			{
				return;
			}

			if (Volatile.Read(ref _registered) != 0)
			{
				throw new InvalidOperationException(
					"A registered Lua module lease cannot be abandoned without unregistration.");
			}

			CompleteRelease();
		}
	}

	private void CompleteRelease()
	{
		Volatile.Write(ref _released, 1);
		List<Exception>? failures = null;
		try
		{
			_untrack(this);
		}
		catch (Exception exception)
		{
			(failures ??= []).Add(exception);
		}

		try
		{
			_releaseModule(_module);
		}
		catch (Exception exception)
		{
			(failures ??= []).Add(exception);
		}

		if (failures is null)
		{
			return;
		}

		if (failures.Count == 1)
		{
			ExceptionDispatchInfo.Capture(failures[0]).Throw();
		}

		throw new AggregateException("Lua module lease release encountered one or more cleanup failures.", failures);
	}
}
