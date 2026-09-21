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
	private readonly Action<string>? _admitStatefulOperation;
	private readonly Func<long> _epochProvider;

	private readonly Func<bool> _isContextCurrent;

	// This is deliberately a single lock-protected reservation. A generated module has two identities that must move
	// together: its managed module instance and the immutable Lua name set published in its descriptor. Reserving the
	// complete set before entering the dispatcher prevents a concurrent registration from partially mutating Lua.
	private readonly Dictionary<ILuaModule, LuaModuleReservation> _registeredModules =
		new(ReferenceEqualityComparer.Instance);

	private readonly Lock _registeredModulesLock = new();
	private readonly HashSet<string> _reservedExportNames = new(StringComparer.Ordinal);
	private readonly HashSet<string> _reservedModuleNames = new(StringComparer.Ordinal);
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
			initialization.UntrackLease,
			initialization.AdmitStatefulOperation)
	{
	}

	/// <summary>Deterministic internal seam for Core tests; production construction uses the SDK dispatcher overload.</summary>
	internal LuaClient(
		ICheatEngineDispatcher dispatcher,
		Func<long> epochProvider,
		Func<bool> isContextCurrent,
		Action<ILuaModuleLease>? trackLease = null,
		Action<ILuaModuleLease>? untrackLease = null,
		Action<string>? admitStatefulOperation = null)
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
		_admitStatefulOperation = admitStatefulOperation;
	}

	public bool TryRegisterModule(ILuaModule luaModule, [NotNullWhen(true)] out ILuaModuleLease? lease,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(luaModule);
		lease = null;
		Admit("Lua.RegisterModule");
		if (cancellationToken.IsCancellationRequested)
		{
			failure = CoreFailureFactory.Cancelled("Lua.RegisterModule");
			return false;
		}

		try
		{
			if (!TryReserveModule(luaModule, out failure))
			{
				return false;
			}
		}
		catch (Exception exception)
		{
			failure = CoreFailureFactory.FromException("Lua.RegisterModule", exception);
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

	public bool TryExecute<TResult>(ILuaOperation<TResult> operation, [MaybeNullWhen(false)] out TResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);
		Admit("Lua.Execute");
		if (cancellationToken.IsCancellationRequested)
		{
			result = default;
			failure = CoreFailureFactory.Cancelled("Lua.Execute");
			return false;
		}

		long epoch = _epochProvider();
		if (!TryDispatchOperation(operation, epoch, out LuaOperationResult<TResult> operationResult, out failure,
			    cancellationToken))
		{
			result = default;
			return false;
		}

		return TryMaterializeOperationResult(operationResult, out result, out failure);
	}

	public TResult Execute<TResult>(ILuaOperation<TResult> operation, CancellationToken cancellationToken = default)
	{
		if (TryExecute<TResult>(operation, out TResult? result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		return ThrowFailure<TResult>(failure);
	}

	public bool TryExecute<TOperation, TResult>(TOperation operation, [MaybeNullWhen(false)] out TResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken)
		where TOperation : struct, ILuaOperation<TResult>
	{
		Admit("Lua.Execute");
		if (cancellationToken.IsCancellationRequested)
		{
			result = default;
			failure = CoreFailureFactory.Cancelled("Lua.Execute");
			return false;
		}

		long epoch = _epochProvider();
		if (!TryDispatchOperation(operation, epoch, out LuaOperationResult<TResult> operationResult, out failure,
			    cancellationToken))
		{
			result = default;
			return false;
		}

		return TryMaterializeOperationResult(operationResult, out result, out failure);
	}

	public TResult Execute<TOperation, TResult>(TOperation operation, CancellationToken cancellationToken)
		where TOperation : struct, ILuaOperation<TResult>
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

	private LuaOperationResult<TResult> ExecuteOperation<TResult>(ILuaOperation<TResult> operation, long epoch)
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

	private LuaOperationResult<TResult> ExecuteOperation<TOperation, TResult>(TOperation operation, long epoch)
		where TOperation : struct, ILuaOperation<TResult>
	{
		LuaOperationContext context = new(epoch, _isContextCurrent);
		try
		{
			context.ThrowIfExpired();
			// The constraint produces a constrained interface call for generated readonly record structs. This keeps the
			// normal generated-operation path free of an ILuaOperation<TResult> box.
			bool succeeded = operation.TryExecute(context, out TResult? result, out CheatEngineFailure failure);
			return new LuaOperationResult<TResult>(succeeded, result!, failure);
		}
		finally
		{
			context.Expire();
		}
	}

	private bool TryDispatchOperation<TResult>(
		ILuaOperation<TResult> operation,
		long epoch,
		out LuaOperationResult<TResult> result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken)
	{
		if (_dispatcher is IStatefulCheatEngineDispatcher statefulDispatcher)
		{
			return statefulDispatcher.TryInvoke(
				new LuaOperationDispatchState<TResult>(this, operation, epoch),
				static dispatchState => dispatchState.Execute(),
				out result,
				out failure,
				cancellationToken);
		}

		return _dispatcher.TryInvoke(
			() => ExecuteOperation(operation, epoch),
			out result,
			out failure,
			cancellationToken);
	}

	private bool TryDispatchOperation<TOperation, TResult>(
		TOperation operation,
		long epoch,
		out LuaOperationResult<TResult> result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken)
		where TOperation : struct, ILuaOperation<TResult>
	{
		if (_dispatcher is IStatefulCheatEngineDispatcher statefulDispatcher)
		{
			return statefulDispatcher.TryInvoke(
				new LuaOperationDispatchState<TOperation, TResult>(this, operation, epoch),
				static dispatchState => dispatchState.Execute(),
				out result,
				out failure,
				cancellationToken);
		}

		return _dispatcher.TryInvoke(
			() => ExecuteOperation<TOperation, TResult>(operation, epoch),
			out result,
			out failure,
			cancellationToken);
	}

	private static bool TryMaterializeOperationResult<TResult>(
		LuaOperationResult<TResult> operationResult,
		[MaybeNullWhen(false)] out TResult result,
		out CheatEngineFailure failure)
	{
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

	private static bool IsDefined(CheatEngineFailure failure)
	{
		return !string.IsNullOrWhiteSpace(failure.Operation) && !string.IsNullOrWhiteSpace(failure.Message);
	}

	private bool TryReserveModule(ILuaModule module, out CheatEngineFailure failure)
	{
		lock (_registeredModulesLock)
		{
			if (_registeredModules.ContainsKey(module))
			{
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Lua.RegisterModule",
					"This client activation already owns the supplied Lua module instance.");
				return false;
			}

			if (module is IDescribedLuaModule describedModule)
			{
				LuaModuleDescriptor descriptor = describedModule.Descriptor;
				if (_reservedModuleNames.Contains(descriptor.Name))
				{
					failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Lua.RegisterModule",
						$"The Lua module identity '{descriptor.Name}' is already reserved by this client activation.");
					return false;
				}

				foreach (LuaExportDescriptor export in descriptor.Exports)
				{
					if (_reservedExportNames.Contains(export.Name))
					{
						failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Lua.RegisterModule",
							$"The Lua export '{export.Name}' is already reserved by this client activation.");
						return false;
					}
				}

				LuaModuleReservation reservation = LuaModuleReservation.Create(descriptor);
				_registeredModules.Add(module, reservation);
				_reservedModuleNames.Add(reservation.ModuleName!);
				foreach (string exportName in reservation.ExportNames)
				{
					_reservedExportNames.Add(exportName);
				}

				failure = default;
				return true;
			}

			// Manual ILuaModule implementations remain a supported escape hatch. They participate in instance ownership
			// only because the Client cannot truthfully infer their global Lua names without reflection or raw Lua access.
			_registeredModules.Add(module, LuaModuleReservation.Manual);
			failure = default;
			return true;
		}
	}

	private void ReleaseModule(ILuaModule module)
	{
		lock (_registeredModulesLock)
		{
			if (!_registeredModules.Remove(module, out LuaModuleReservation? reservation))
			{
				return;
			}

			if (reservation.ModuleName is not null)
			{
				_reservedModuleNames.Remove(reservation.ModuleName);
				foreach (string exportName in reservation.ExportNames)
				{
					_reservedExportNames.Remove(exportName);
				}
			}
		}
	}

	private static LuaClientInitialization CreateProductionInitialization(CoreLifetime lifetime)
	{
		ArgumentNullException.ThrowIfNull(lifetime);
		return new LuaClientInitialization(
			() => lifetime.Epoch,
			() => lifetime.IsActivationCurrent,
			lease => lifetime.Track(lease),
			lease => lifetime.Untrack(lease),
			lifetime.ThrowIfInactive);
	}

	private readonly record struct LuaOperationResult<TResult>(
		bool Succeeded,
		TResult Result,
		CheatEngineFailure Failure);

	private readonly record struct LuaClientInitialization(
		Func<long> EpochProvider,
		Func<bool> IsContextCurrent,
		Action<ILuaModuleLease> TrackLease,
		Action<ILuaModuleLease> UntrackLease,
		Action<string> AdmitStatefulOperation);

	private void Admit(string operation) => _admitStatefulOperation?.Invoke(operation);

	private readonly struct LuaOperationDispatchState<TResult>
	{
		private readonly LuaClient _client;
		private readonly long _epoch;
		private readonly ILuaOperation<TResult> _operation;

		internal LuaOperationDispatchState(LuaClient client, ILuaOperation<TResult> operation, long epoch)
		{
			_client = client;
			_operation = operation;
			_epoch = epoch;
		}

		internal LuaOperationResult<TResult> Execute()
		{
			return _client.ExecuteOperation(_operation, _epoch);
		}
	}

	private readonly struct LuaOperationDispatchState<TOperation, TResult>
		where TOperation : struct, ILuaOperation<TResult>
	{
		private readonly LuaClient _client;
		private readonly long _epoch;
		private readonly TOperation _operation;

		internal LuaOperationDispatchState(LuaClient client, TOperation operation, long epoch)
		{
			_client = client;
			_operation = operation;
			_epoch = epoch;
		}

		internal LuaOperationResult<TResult> Execute()
		{
			return _client.ExecuteOperation<TOperation, TResult>(_operation, _epoch);
		}
	}

	private sealed class LuaModuleReservation
	{
		private LuaModuleReservation(string? moduleName, string[] exportNames)
		{
			ModuleName = moduleName;
			ExportNames = exportNames;
		}

		internal static LuaModuleReservation Manual
		{
			get;
		} = new(null, []);

		internal string[] ExportNames
		{
			get;
		}

		internal string? ModuleName
		{
			get;
		}

		internal static LuaModuleReservation Create(LuaModuleDescriptor descriptor)
		{
			string[] exportNames = new string[descriptor.Exports.Length];
			for (int index = 0; index < exportNames.Length; index++)
			{
				exportNames[index] = descriptor.Exports[index].Name;
			}

			return new LuaModuleReservation(descriptor.Name, exportNames);
		}
	}
}
