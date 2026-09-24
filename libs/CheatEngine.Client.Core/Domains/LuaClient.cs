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
	private const string RegisterOperation = "Lua.RegisterModule";

	private readonly Action<string>? _admitStatefulOperation;
	private readonly ICoreDiagnostics _diagnostics;
	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly Func<long> _epochProvider;

	private readonly Func<bool> _isContextCurrent;
	private readonly Func<bool> _isStopping;

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
			initialization.AdmitStatefulOperation,
			initialization.IsStopping,
			initialization.Diagnostics)
	{
	}

	/// <summary>Deterministic internal seam for Core tests; production construction uses the SDK dispatcher overload.</summary>
	internal LuaClient(
		ICheatEngineDispatcher dispatcher,
		Func<long> epochProvider,
		Func<bool> isContextCurrent,
		Action<ILuaModuleLease>? trackLease = null,
		Action<ILuaModuleLease>? untrackLease = null,
		Action<string>? admitStatefulOperation = null,
		Func<bool>? isStopping = null,
		ICoreDiagnostics? diagnostics = null)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_diagnostics = GuardedCoreDiagnostics.Wrap(diagnostics);
		_epochProvider = epochProvider ?? throw new ArgumentNullException(nameof(epochProvider));
		_isContextCurrent = isContextCurrent ?? throw new ArgumentNullException(nameof(isContextCurrent));
		_isStopping = isStopping ?? (static () => false);
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
		LuaModuleDescriptor descriptor = luaModule.Descriptor;
		if (descriptor.Name is null)
		{
			throw new ArgumentException("The Lua module must declare its identity and exports in a descriptor.",
				nameof(luaModule));
		}

		lease = null;
		Admit(RegisterOperation);
		if (cancellationToken.IsCancellationRequested)
		{
			failure = CoreFailureFactory.Cancelled(RegisterOperation);
			return false;
		}

		if (!TryReserveModule(luaModule, descriptor, out failure))
		{
			return false;
		}

		LuaModuleLease created = new(luaModule, _epochProvider(), _dispatcher, _diagnostics, _untrackLease,
			ReleaseModule);
		try
		{
			_trackLease(created);
		}
		catch (Exception trackingException)
		{
			failure = FailAfterAbandoningUnregisteredLease(
				SdkBoundary.Classify(RegisterOperation, trackingException, CheatEngineHostEffect.NotStarted),
				created);
			return false;
		}

		bool registered = false;
		Exception? registrationFault = null;
		if (!_dispatcher.TryInvoke(
				() =>
				{
					try
					{
						luaModule.Register();
						created.ConfirmRegistration();
						registered = true;
					}
					catch (Exception exception)
					{
						registrationFault = exception;
					}
				},
				out failure,
				cancellationToken))
		{
			failure = FailAfterAbandoningUnregisteredLease(failure, created);
			return false;
		}

		if (!registered)
		{
			failure = FailAfterAbandoningUnregisteredLease(ClassifyRegistrationFault(registrationFault), created);
			return false;
		}

		try
		{
			// Registration and lifetime tracking are deliberately handed off in this order. If shutdown starts while
			// Register runs, the already-tracked lease is drained by the hosting cleanup scope rather than redispatched
			// from this worker after ordinary dispatch admission has closed.
			Admit(RegisterOperation);
		}
		catch (Exception exception)
		{
			CheatEngineFailure admissionFailure =
				SdkBoundary.Classify(RegisterOperation, exception, CheatEngineHostEffect.Unknown);
			if (_isStopping())
			{
				// The Core lifetime owns the pre-tracked, registered lease. Leaving it there gives the main-thread
				// cleanup scope one admitted unregistration attempt and avoids publishing a lease to a stopped caller.
				failure = admissionFailure;
				return false;
			}

			failure = FailAfterRegisteredLease(admissionFailure, created);
			return false;
		}

		lease = created;
		failure = default;
		return true;
	}

	public ILuaModuleLease RegisterModule(ILuaModule luaModule, CancellationToken cancellationToken = default)
	{
		if (TryRegisterModule(luaModule, out ILuaModuleLease? lease, out CheatEngineFailure failure, cancellationToken))
		{
			return lease;
		}

		failure.Throw(cancellationToken);
		throw new InvalidOperationException("Unreachable failure flow.");
	}

	public bool TryExecute<TOperation, TResult>(in TOperation operation, [MaybeNullWhen(false)] out TResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		where TOperation : ILuaOperation<TResult>
	{
		// A null test on an unconstrained type parameter never boxes a value operation.
		if (operation is null)
		{
			throw new ArgumentNullException(nameof(operation));
		}

		Admit("Lua.Execute");
		if (cancellationToken.IsCancellationRequested)
		{
			result = default;
			failure = CoreFailureFactory.Cancelled("Lua.Execute");
			return false;
		}

		long epoch = _epochProvider();
		long started = Stopwatch.GetTimestamp();
		bool completed;
		if (TryDispatchOperation(operation, epoch, out LuaOperationResult<TResult> operationResult, out failure,
				cancellationToken))
		{
			completed = TryMaterializeOperationResult(operationResult, out result, out failure);
		}
		else
		{
			result = default;
			completed = false;
		}

		ReportOperation(completed, failure, started);
		return completed;
	}

	public TResult Execute<TOperation, TResult>(in TOperation operation, CancellationToken cancellationToken = default)
		where TOperation : ILuaOperation<TResult>
	{
		if (TryExecute<TOperation, TResult>(in operation, out TResult? result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		return ThrowFailure<TResult>(failure, cancellationToken);
	}

	/// <summary>
	///     Reports a typed operation with its failure kind and duration only (EventId 1600): never the operation type, a
	///     Lua value or the failure message.
	/// </summary>
	private void ReportOperation(bool completed, CheatEngineFailure failure, long started)
	{
		_diagnostics.LuaOperationCompleted("Lua.Execute", completed ? "None" : failure.Kind.ToString(),
			(long) Stopwatch.GetElapsedTime(started).TotalMilliseconds, 0);
	}

	private static T ThrowFailure<T>(CheatEngineFailure failure, CancellationToken cancellationToken)
	{
		failure.Throw(cancellationToken);
		throw new UnreachableException();
	}

	private LuaOperationResult<TResult> ExecuteOperation<TOperation, TResult>(TOperation operation, long epoch)
		where TOperation : ILuaOperation<TResult>
	{
		LuaOperationContext context = new(epoch, _isContextCurrent);
		try
		{
			context.ThrowIfExpired();
			// The type parameter produces a constrained interface call for generated readonly record structs. This keeps
			// the generated-operation path free of an ILuaOperation<TResult> box.
			bool succeeded = operation.TryExecute(context, out TResult? result, out CheatEngineFailure failure);
			return new LuaOperationResult<TResult>(succeeded, result!, failure);
		}
		finally
		{
			context.Expire();
		}
	}

	private bool TryDispatchOperation<TOperation, TResult>(
		TOperation operation,
		long epoch,
		out LuaOperationResult<TResult> result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken)
		where TOperation : ILuaOperation<TResult>
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

	private bool TryReserveModule(ILuaModule module, LuaModuleDescriptor descriptor, out CheatEngineFailure failure)
	{
		// Every module declares its identity and exports, so the complete name set is reserved before any Lua work.
		lock (_registeredModulesLock)
		{
			if (_registeredModules.ContainsKey(module))
			{
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidState, RegisterOperation,
					"This client activation already owns the supplied Lua module instance.", null,
					CheatEngineHostEffect.NotStarted);
				return false;
			}

			if (_reservedModuleNames.Contains(descriptor.Name))
			{
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidState, RegisterOperation,
					$"The Lua module identity '{descriptor.Name}' is already reserved by this client activation.", null,
					CheatEngineHostEffect.NotStarted);
				return false;
			}

			foreach (LuaExportDescriptor export in descriptor.Exports)
			{
				if (_reservedExportNames.Contains(export.Name))
				{
					failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidState, RegisterOperation,
						$"The Lua export '{export.Name}' is already reserved by this client activation.", null,
						CheatEngineHostEffect.NotStarted);
					return false;
				}
			}

			LuaModuleReservation reservation = LuaModuleReservation.Create(descriptor);
			_registeredModules.Add(module, reservation);
			_reservedModuleNames.Add(reservation.ModuleName);
			foreach (string exportName in reservation.ExportNames)
			{
				_reservedExportNames.Add(exportName);
			}

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

			_reservedModuleNames.Remove(reservation.ModuleName);
			foreach (string exportName in reservation.ExportNames)
			{
				_reservedExportNames.Remove(exportName);
			}
		}
	}

	/// <summary>
	///     Reports the exception that a module's <see cref="ILuaModule.Register" /> threw: the failure a
	///     <see cref="CheatEngineOperationException" /> carries (a generated module classifies its own refusals), otherwise
	///     the <see cref="SdkBoundary" /> classification with an unknown host effect.
	/// </summary>
	private static CheatEngineFailure ClassifyRegistrationFault(Exception? fault)
	{
		return fault switch
		{
			CheatEngineOperationException reported => reported.Failure,
			null => new CheatEngineFailure(CheatEngineFailureKind.Unknown, RegisterOperation,
				"The Lua module registration ended without completing or reporting a failure."),
			_ => SdkBoundary.Classify(RegisterOperation, fault, CheatEngineHostEffect.Unknown)
		};
	}

	private static LuaClientInitialization CreateProductionInitialization(CoreLifetime lifetime)
	{
		ArgumentNullException.ThrowIfNull(lifetime);
		return new LuaClientInitialization(
			() => lifetime.Epoch,
			() => lifetime.IsActivationCurrent,
			() => lifetime.Stopping.IsCancellationRequested,
			lease => lifetime.Track(lease),
			lease => lifetime.Untrack(lease),
			lifetime.ThrowIfInactive,
			lifetime.Diagnostics);
	}

	private void Admit(string operation)
	{
		_admitStatefulOperation?.Invoke(operation);
	}

	private static CheatEngineFailure FailAfterAbandoningUnregisteredLease(
		CheatEngineFailure primaryFailure,
		LuaModuleLease lease)
	{
		try
		{
			lease.AbandonRegistration();
			return primaryFailure;
		}
		catch (Exception cleanupException)
		{
			return WithSecondaryFailure(primaryFailure, cleanupException);
		}
	}

	private static CheatEngineFailure FailAfterRegisteredLease(CheatEngineFailure primaryFailure, LuaModuleLease lease)
	{
		// Releasing never throws: an incomplete release stays tracked and is reported by the activation cleanup.
		LeaseReleaseOutcome release = lease.Release();
		return release.IsComplete
			? primaryFailure
			: new CheatEngineFailure(primaryFailure.Kind, primaryFailure.Operation,
				$"{primaryFailure.Message} The registration was then released with the outcome {release}.",
				primaryFailure.Exception, CheatEngineHostEffect.CleanupUnconfirmed);
	}

	private static CheatEngineFailure WithSecondaryFailure(CheatEngineFailure primaryFailure,
		Exception secondaryFailure)
	{
		Exception primaryException = primaryFailure.Exception ?? new CheatEngineOperationException(primaryFailure);
		AggregateException combined = new(
			"Lua module registration failed and its handoff cleanup encountered an additional failure.",
			primaryException,
			secondaryFailure);
		return new CheatEngineFailure(
			primaryFailure.Kind,
			primaryFailure.Operation,
			$"{primaryFailure.Message} The registration handoff cleanup also failed: {secondaryFailure.Message}",
			combined);
	}

	private readonly record struct LuaOperationResult<TResult>(
		bool Succeeded,
		TResult Result,
		CheatEngineFailure Failure);

	private readonly record struct LuaClientInitialization(
		Func<long> EpochProvider,
		Func<bool> IsContextCurrent,
		Func<bool> IsStopping,
		Action<ILuaModuleLease> TrackLease,
		Action<ILuaModuleLease> UntrackLease,
		Action<string> AdmitStatefulOperation,
		ICoreDiagnostics Diagnostics);

	private readonly struct LuaOperationDispatchState<TOperation, TResult>
		where TOperation : ILuaOperation<TResult>
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
		private LuaModuleReservation(string moduleName, string[] exportNames)
		{
			ModuleName = moduleName;
			ExportNames = exportNames;
		}

		internal string[] ExportNames
		{
			get;
		}

		internal string ModuleName
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
