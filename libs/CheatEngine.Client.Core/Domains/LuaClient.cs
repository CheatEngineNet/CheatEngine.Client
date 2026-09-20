using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains;

internal sealed class LuaClient : ILuaClient
{
	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly Func<long> _epochProvider;
	private readonly Func<bool> _isContextCurrent;
	private readonly HashSet<ILuaModule> _registeredModules = new(ReferenceEqualityComparer.Instance);
	private readonly object _registeredModulesLock = new();
	private readonly Action<ILuaModuleLease> _trackLease;
	private readonly Action<ILuaModuleLease> _untrackLease;

	internal LuaClient(SdkMainThreadDispatcher dispatcher, CoreLifetime lifetime)
		: this(dispatcher ?? throw new ArgumentNullException(nameof(dispatcher)),
			CreateProductionInitialization(lifetime))
	{
	}

	private LuaClient(ICheatEngineDispatcher dispatcher, LuaClientInitialization initialization)
		: this(
			dispatcher,
			initialization.EpochProvider,
			initialization.IsContextCurrent,
			initialization.TrackLease,
			initialization.UntrackLease)
	{
	}

	/// <summary>Deterministic internal seam for Core tests; production construction uses the SDK dispatcher overload.</summary>
	internal LuaClient(
		ICheatEngineDispatcher dispatcher,
		Func<long> epochProvider,
		Func<bool> isContextCurrent,
		Action<ILuaModuleLease>? trackLease = null,
		Action<ILuaModuleLease>? untrackLease = null)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_epochProvider = epochProvider ?? throw new ArgumentNullException(nameof(epochProvider));
		_isContextCurrent = isContextCurrent ?? throw new ArgumentNullException(nameof(isContextCurrent));
		_trackLease = trackLease ?? (static _ =>
		{
		});
		_untrackLease = untrackLease ?? (static _ =>
		{
		});
	}

	public bool TryRegisterModule(ILuaModule luaModule, [NotNullWhen(true)] out ILuaModuleLease? lease,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(luaModule);
		lease = null;
		if (cancellationToken.IsCancellationRequested)
		{
			failure = CoreFailureFactory.Cancelled("Lua.RegisterModule");
			return false;
		}

		if (!TryReserveModule(luaModule, out failure))
		{
			return false;
		}

		bool registered = false;
		CheatEngineFailure moduleFailure = default;
		if (!_dispatcher.TryInvoke(
			    () =>
			    {
				    try
				    {
					    luaModule.Register();
					    registered = true;
				    }
				    catch (Exception exception)
				    {
					    moduleFailure = CoreFailureFactory.FromException("Lua.RegisterModule", exception);
				    }
			    },
			    out failure,
			    cancellationToken))
		{
			ReleaseModule(luaModule);
			return false;
		}

		if (!registered)
		{
			ReleaseModule(luaModule);
			failure = moduleFailure;
			return false;
		}

		LuaModuleLease created = new(luaModule, _epochProvider(), _dispatcher, _isContextCurrent, _untrackLease,
			ReleaseModule);
		try
		{
			_trackLease(created);
			lease = created;
			failure = default;
			return true;
		}
		catch (Exception exception)
		{
			try
			{
				created.Dispose();
			}
			catch
			{
				// The track failure remains the meaningful result; the module was still removed from this Client scope.
			}

			failure = CoreFailureFactory.FromException("Lua.RegisterModule", exception);
			return false;
		}
	}

	public ILuaModuleLease RegisterModule(ILuaModule luaModule, CancellationToken cancellationToken = default)
	{
		if (TryRegisterModule(luaModule, out ILuaModuleLease? lease, out CheatEngineFailure failure, cancellationToken))
		{
			return lease;
		}

		failure.Throw();
		throw new InvalidOperationException("Unreachable failure flow.");
	}

	public bool TryExecute<TOperation, TResult>(TOperation operation, [MaybeNullWhen(false)] out TResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		where TOperation : ILuaOperation<TResult>
	{
		ArgumentNullException.ThrowIfNull(operation);
		if (cancellationToken.IsCancellationRequested)
		{
			result = default;
			failure = CoreFailureFactory.Cancelled("Lua.Execute");
			return false;
		}

		long epoch = _epochProvider();
		LuaOperationResult<TResult> operationResult = default;
		if (!_dispatcher.TryInvoke(
			    () => operationResult = ExecuteOperation<TOperation, TResult>(operation, epoch),
			    out failure,
			    cancellationToken))
		{
			result = default;
			return false;
		}

		if (!operationResult.Succeeded)
		{
			result = default;
			failure = IsDefined(operationResult.Failure)
				? operationResult.Failure
				: new CheatEngineFailure(
					CheatEngineFailureKind.Unknown,
					"Lua.Execute",
					"The typed Lua operation returned failure without an associated Cheat Engine failure.");
			return false;
		}

		result = operationResult.Result;
		failure = default;
		return true;
	}

	public TResult Execute<TOperation, TResult>(TOperation operation, CancellationToken cancellationToken = default)
		where TOperation : ILuaOperation<TResult>
	{
		if (TryExecute<TOperation, TResult>(operation, out TResult? result, out CheatEngineFailure failure,
			    cancellationToken))
		{
			return result;
		}

		return ThrowFailure<TResult>(failure);
	}

	private static T ThrowFailure<T>(CheatEngineFailure failure)
	{
		failure.Throw();
		throw new UnreachableException();
	}

	private LuaOperationResult<TResult> ExecuteOperation<TOperation, TResult>(TOperation operation, long epoch)
		where TOperation : ILuaOperation<TResult>
	{
		LuaOperationContext context = new(epoch, _isContextCurrent);
		try
		{
			context.ThrowIfExpired();
			bool succeeded = operation.TryExecute(context, out TResult? result, out CheatEngineFailure failure);
			return new LuaOperationResult<TResult>(succeeded, result!, failure);
		}
		finally
		{
			context.Expire();
		}
	}

	private static bool IsDefined(CheatEngineFailure failure)
	{
		return !string.IsNullOrWhiteSpace(failure.Operation) && !string.IsNullOrWhiteSpace(failure.Message);
	}

	private bool TryReserveModule(ILuaModule module, out CheatEngineFailure failure)
	{
		lock (_registeredModulesLock)
		{
			if (_registeredModules.Add(module))
			{
				failure = default;
				return true;
			}
		}

		failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Lua.RegisterModule",
			"This client activation already owns the supplied Lua module instance.");
		return false;
	}

	private void ReleaseModule(ILuaModule module)
	{
		lock (_registeredModulesLock)
		{
			_registeredModules.Remove(module);
		}
	}

	private static LuaClientInitialization CreateProductionInitialization(CoreLifetime lifetime)
	{
		ArgumentNullException.ThrowIfNull(lifetime);
		return new LuaClientInitialization(
			() => lifetime.Epoch,
			() => lifetime.IsActivationCurrent,
			lease => lifetime.Track(lease),
			lease => lifetime.Untrack(lease));
	}

	private readonly record struct LuaOperationResult<TResult>(
		bool Succeeded,
		TResult Result,
		CheatEngineFailure Failure);

	private readonly record struct LuaClientInitialization(
		Func<long> EpochProvider,
		Func<bool> IsContextCurrent,
		Action<ILuaModuleLease> TrackLease,
		Action<ILuaModuleLease> UntrackLease);
}
