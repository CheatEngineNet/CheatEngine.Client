using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

internal sealed class InspectionClient(
	SdkMainThreadDispatcher dispatcher,
	CoreLifetime lifetime,
	IInspectionPort? inspection = null) : IInspectionClient
{
	private const string RegisterOperation = "Inspection.RegisterSymbol";

	private const string ResolveNameOperation = "Inspection.ResolveName";

	private readonly SdkMainThreadDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	private readonly IInspectionPort _inspection = inspection ?? new SdkInspectionPort();

	private readonly CoreLifetime _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));

	// Cheat Engine's case rules for user symbols are not established, so the activation-local reservation is
	// conservative: names that differ only by case are treated as the same name.
	private readonly HashSet<string> _registeredSymbolNames = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _registeredSymbolNamesLock = new();

	public bool TryGetModules(InspectionCollectionRequest request, TargetProcessId? processId,
		out ImmutableArray<ModuleInfo> modules, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (IsDefault(request, "Inspection.GetModules", out failure))
		{
			modules = [];
			return false;
		}

		ImmutableArray<ModuleInfo> result = ImmutableArray<ModuleInfo>.Empty;
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!SdkBoundary.TryInvoke(_dispatcher, "Inspection.GetModules", () =>
			{
				ModuleInfo[] buffer = new ModuleInfo[request.MaximumItems];
				status = processId.HasValue
					? _inspection.EnumerateModules(processId.Value, buffer, out int written)
					: _inspection.EnumerateModules(buffer, out written);
				if (status == InspectionStatus.Success)
				{
					result = ImmutableArray.Create(buffer, 0, written);
				}
			}, CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
		{
			modules = [];
			return false;
		}

		modules = result;
		return TryMap(status, "Inspection.GetModules", out failure);
	}

	public ImmutableArray<ModuleInfo> GetModules(InspectionCollectionRequest request,
		TargetProcessId? processId = null, CancellationToken cancellationToken = default)
	{
		if (TryGetModules(request, processId, out ImmutableArray<ModuleInfo> result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return [];
	}

	public bool TryGetModuleSections(ModuleName moduleName, InspectionCollectionRequest request,
		out ImmutableArray<ModuleSectionInfo> sections, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (IsDefault(request, "Inspection.GetModuleSections", out failure))
		{
			sections = [];
			return false;
		}

		ImmutableArray<ModuleSectionInfo> result = ImmutableArray<ModuleSectionInfo>.Empty;
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!SdkBoundary.TryInvoke(_dispatcher, "Inspection.GetModuleSections", () =>
			{
				ModuleSectionInfo[] buffer = new ModuleSectionInfo[request.MaximumItems];
				status = _inspection.EnumerateSections(moduleName, buffer, out int written);
				if (status == InspectionStatus.Success)
				{
					result = ImmutableArray.Create(buffer, 0, written);
				}
			}, CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
		{
			sections = [];
			return false;
		}

		sections = result;
		return TryMap(status, "Inspection.GetModuleSections", out failure);
	}

	public ImmutableArray<ModuleSectionInfo> GetModuleSections(ModuleName moduleName,
		InspectionCollectionRequest request, CancellationToken cancellationToken = default)
	{
		if (TryGetModuleSections(moduleName, request, out ImmutableArray<ModuleSectionInfo> result,
				out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return [];
	}

	public bool TryGetMemoryRegions(InspectionCollectionRequest request,
		out ImmutableArray<MemoryRegionInfo> regions, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (IsDefault(request, "Inspection.GetMemoryRegions", out failure))
		{
			regions = [];
			return false;
		}

		ImmutableArray<MemoryRegionInfo> result = [];
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!SdkBoundary.TryInvoke(_dispatcher, "Inspection.GetMemoryRegions", () =>
			{
				MemoryRegionInfo[] buffer = new MemoryRegionInfo[request.MaximumItems];
				status = _inspection.EnumerateMemoryRegions(buffer, out int written);
				if (status == InspectionStatus.Success)
				{
					result = ImmutableArray.Create(buffer, 0, written);
				}
			}, CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
		{
			regions = [];
			return false;
		}

		regions = result;
		return TryMap(status, "Inspection.GetMemoryRegions", out failure);
	}

	public ImmutableArray<MemoryRegionInfo> GetMemoryRegions(InspectionCollectionRequest request,
		CancellationToken cancellationToken = default)
	{
		if (TryGetMemoryRegions(request, out ImmutableArray<MemoryRegionInfo> result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return [];
	}

	public bool TryGetMemoryRegion(Address address, out MemoryRegionInfo region,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		MemoryRegionInfo captured = default;
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!SdkBoundary.TryInvoke(_dispatcher, "Inspection.GetMemoryRegion",
				() => status = _inspection.GetMemoryRegion(address, out captured), CheatEngineHostEffect.Unknown,
				_lifetime, out failure, cancellationToken))
		{
			region = default;
			return false;
		}

		region = captured;
		return TryMap(status, "Inspection.GetMemoryRegion", out failure);
	}

	public MemoryRegionInfo GetMemoryRegion(Address address, CancellationToken cancellationToken = default)
	{
		if (TryGetMemoryRegion(address, out MemoryRegionInfo result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	public bool TryGetSymbol(SymbolExpression expression, out SymbolInfo symbol,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		SymbolInfo captured = default;
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!SdkBoundary.TryInvoke(_dispatcher, "Inspection.GetSymbol",
				() => status = _inspection.GetSymbol(expression, out captured), CheatEngineHostEffect.Unknown,
				_lifetime, out failure, cancellationToken))
		{
			symbol = default;
			return false;
		}

		symbol = captured;
		return TryMap(status, "Inspection.GetSymbol", out failure);
	}

	public SymbolInfo GetSymbol(SymbolExpression expression, CancellationToken cancellationToken = default)
	{
		if (TryGetSymbol(expression, out SymbolInfo result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	public bool TryResolveName(Address address, [NotNullWhen(true)] out string? name, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		string? captured = null;
		LuaOperationStatus status = default;
		if (!SdkBoundary.TryInvoke(_dispatcher, ResolveNameOperation,
				() => status = _inspection.TryGetName(address, out captured),
				CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
		{
			name = null;
			return false;
		}

		if (status.IsSuccess && captured is not null)
		{
			name = captured;
			return true;
		}

		name = null;
		failure = InspectionMapping.NameLookupFailure(ResolveNameOperation, status.Kind);
		return false;
	}

	public string ResolveName(Address address, CancellationToken cancellationToken = default)
	{
		if (TryResolveName(address, out string? result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return string.Empty;
	}

	public bool TryRegisterSymbol(SymbolRegistration registration,
		[NotNullWhen(true)] out ISymbolRegistrationLease? lease,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		lease = null;
		_lifetime.ThrowIfInactive(RegisterOperation);
		if (registration.Name is null)
		{
			// Only the default registration has no name: its constructor refuses an empty one.
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, RegisterOperation,
				"A symbol registration must name the symbol; the default registration has no name.", null,
				CheatEngineHostEffect.NotStarted);
			return false;
		}

		if (!TryReserveSymbolName(registration.Name, out failure))
		{
			return false;
		}

		// The collision pre-check, the registration and the activation ownership run in one dispatched callback (audit
		// A14-25, A14-39): CheatEngine.SDK keeps Cheat Engine's behavior for an existing name, so registerSymbol would
		// shadow or replace a definition that already resolves, and only a name that resolves to nothing is registered.
		// An SDK fault inside the registration leaves it unknown: the Client neither claims it nor retries an
		// unregistration by name. A registration CheatEngine.SDK could not hand over (SymbolRegistrationHandoffException)
		// was compensated once by the SDK, and SdkBoundary reports it CleanupUnconfirmed.
		RegistrationStep step = default;
		bool dispatched;
		try
		{
			dispatched = SdkBoundary.TryInvoke(_dispatcher, RegisterOperation,
				() => step = RegisterOnMainThread(registration), CheatEngineHostEffect.Unknown, _lifetime, out failure,
				cancellationToken);
		}
		catch (Exception)
		{
			// A lifecycle exception of the dispatch or of the callback's admission registered nothing.
			ReleaseSymbolName(registration.Name);
			throw;
		}

		if (!dispatched)
		{
			ReleaseSymbolName(registration.Name);
			return false;
		}

		if (step.Lease is { } created)
		{
			lease = created;
			failure = default;
			return true;
		}

		ReleaseSymbolName(registration.Name);
		failure = CreateRegistrationFailure(step);
		return false;
	}

	public ISymbolRegistrationLease RegisterSymbol(SymbolRegistration registration,
		CancellationToken cancellationToken = default)
	{
		if (TryRegisterSymbol(registration, out ISymbolRegistrationLease? result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		throw new InvalidOperationException("Unreachable failure flow.");
	}

	public bool TryResolveAddress(SymbolExpression expression, AddressResolutionMode mode,
		out Address address, out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		if (!Enum.IsDefined(mode))
		{
			throw new ArgumentOutOfRangeException(nameof(mode), mode,
				"The address resolution mode must be a defined value.");
		}

		Address captured = Address.Zero;
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!SdkBoundary.TryInvoke(_dispatcher, "Inspection.ResolveAddress",
				() => status = _inspection.ResolveAddress(expression, mode, out captured),
				CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
		{
			address = default;
			return false;
		}

		address = captured;
		return TryMap(status, "Inspection.ResolveAddress", out failure);
	}

	public Address ResolveAddress(SymbolExpression expression, AddressResolutionMode mode,
		CancellationToken cancellationToken = default)
	{
		if (TryResolveAddress(expression, mode, out Address result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	/// <summary>
	///     Refuses the default collection request, which allows no item, before dispatch: its constructor refuses a
	///     limit below one, and a copy into no room would otherwise reach Cheat Engine only to exceed it.
	/// </summary>
	private static bool IsDefault(InspectionCollectionRequest request, string operation, out CheatEngineFailure failure)
	{
		if (request.MaximumItems > 0)
		{
			failure = default;
			return false;
		}

		failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
			"An inspection collection request must allow at least one item; the default request allows none.", null,
			CheatEngineHostEffect.NotStarted);
		return true;
	}

	/// <summary>Maps an SDK inspection status by value; internal so the Q48 contract tests can prove it is total.</summary>
	/// <remarks>
	///     An unavailable inspection global is <see cref="CheatEngineFailureKind.CapabilityUnavailable" /> with
	///     <see cref="CheatEngineHostEffect.NotStarted" />: Cheat Engine was not called, as the memory and Address List
	///     lookups report it. A status this Client does not recognize fails closed as
	///     <see cref="CheatEngineFailureKind.IndeterminateHostResult" />, like <see cref="InspectionMapping" />; every
	///     other failure keeps an unknown host effect.
	/// </remarks>
	internal static bool TryMap(InspectionStatus status, string operation, out CheatEngineFailure failure)
	{
		if (status == InspectionStatus.Success)
		{
			failure = default;
			return true;
		}

		CheatEngineFailureKind kind = status switch
		{
			InspectionStatus.NotFound => CheatEngineFailureKind.NotFound,
			InspectionStatus.DestinationTooSmall => CheatEngineFailureKind.ResultLimitExceeded,
			InspectionStatus.GlobalUnavailable => CheatEngineFailureKind.CapabilityUnavailable,
			InspectionStatus.LuaFailure => CheatEngineFailureKind.LuaError,
			InspectionStatus.InvalidResult => CheatEngineFailureKind.InvalidHostResult,
			_ => CheatEngineFailureKind.IndeterminateHostResult
		};
		CheatEngineHostEffect effect = status == InspectionStatus.GlobalUnavailable
			? CheatEngineHostEffect.NotStarted
			: CheatEngineHostEffect.Unknown;
		failure = new CheatEngineFailure(kind, operation, $"Cheat Engine inspection returned '{status}'.", null,
			effect);
		return false;
	}

	/// <summary>The refusal of a registration whose name already resolves or whose collision check failed.</summary>
	private static CheatEngineFailure CreateCollisionFailure(InspectionStatus preflight)
	{
		if (preflight == InspectionStatus.Success)
		{
			return new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, RegisterOperation,
				"The symbol name already resolves in Cheat Engine (a registered symbol, a module or an expression that " +
				"parses as an address); registering it would shadow or replace that definition.", null,
				CheatEngineHostEffect.NotStarted);
		}

		TryMap(preflight, RegisterOperation, out CheatEngineFailure mapped);
		return new CheatEngineFailure(mapped.Kind, mapped.Operation,
			$"The symbol collision check returned '{preflight}', so the ownership of the name cannot be established; " +
			"nothing was registered.", null, CheatEngineHostEffect.NotStarted);
	}

	/// <summary>
	///     Runs on Cheat Engine's main thread: the collision pre-check, the registration through the CheatEngine.SDK
	///     ownership coordinator, and the registration of the lease with the activation.
	/// </summary>
	/// <remarks>
	///     The lease joins the activation before the callback returns, so a registration never outlives an activation
	///     that starts stopping afterwards. When the lease cannot join it (the activation stopped between the dispatch
	///     admission and that step, or its resources were already drained, which closes the registry with an
	///     <see cref="ObjectDisposedException" />), the registration has no owner: it is released once, here, and the
	///     failure reports what the release established.
	/// </remarks>
	private RegistrationStep RegisterOnMainThread(SymbolRegistration registration)
	{
		// No lease can be registered once the activation stops or ends: refuse before Cheat Engine registers a name
		// that no lease could own, as every other lease-creating operation does in its callback.
		_lifetime.ThrowIfInactive(RegisterOperation);
		InspectionStatus preflight = _inspection.ResolveAddress(new SymbolExpression(registration.Name),
			AddressResolutionMode.Default, out _);
		if (preflight != InspectionStatus.NotFound)
		{
			return new RegistrationStep(preflight, default, null, null, default);
		}

		SymbolRegistrationAttempt attempt = _inspection.TryRegisterOwned(new SymbolName(registration.Name),
			registration.Address, new SymbolRegistrationOptions(registration.DoNotSave));
		if (attempt.Handle is not { } handle)
		{
			return new RegistrationStep(preflight, attempt.Status, null, null, default);
		}

		SymbolRegistrationLease lease = new(registration, handle, _dispatcher, ReleaseSymbolName,
			_lifetime.Diagnostics);
		try
		{
			lease.Register(_lifetime);
			return new RegistrationStep(preflight, attempt.Status, lease, null, default);
		}
		catch (Exception exception)
		{
			// Whatever kept the lease out of the activation, nothing owns the registration: release it once.
			return new RegistrationStep(preflight, attempt.Status, null, exception,
				SdkReleaseOutcomes.FromSymbolRegistration(handle.Release()));
		}
	}

	/// <summary>Creates the failure of a registration that produced no lease; emitted after the dispatched work.</summary>
	private CheatEngineFailure CreateRegistrationFailure(RegistrationStep step)
	{
		if (step.TrackingFault is { } fault)
		{
			// The one release made on the main thread removed the registration, or could not confirm it.
			CheatEngineHostEffect effect = step.Compensation.IsComplete
				? CheatEngineHostEffect.Completed
				: CheatEngineHostEffect.CleanupUnconfirmed;
			return SdkBoundary.Translate(RegisterOperation, fault, effect, _lifetime);
		}

		if (step.Preflight != InspectionStatus.NotFound)
		{
			// The reason only: the symbol name is never logged (A24-13).
			_lifetime.Diagnostics.SymbolRegistrationRejected(RegisterOperation,
				step.Preflight == InspectionStatus.Success ? "AlreadyResolves" : "LookupFailed");
			return CreateCollisionFailure(step.Preflight);
		}

		return InspectionMapping.RegistrationFailure(RegisterOperation, step.Status.Kind);
	}

	private bool TryReserveSymbolName(string name, out CheatEngineFailure failure)
	{
		lock (_registeredSymbolNamesLock)
		{
			if (_registeredSymbolNames.Add(name))
			{
				failure = default;
				return true;
			}
		}

		failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, RegisterOperation,
			"This client activation already owns a symbol registration with the requested name.", null,
			CheatEngineHostEffect.NotStarted);
		return false;
	}

	private void ReleaseSymbolName(string name)
	{
		lock (_registeredSymbolNamesLock)
		{
			_registeredSymbolNames.Remove(name);
		}
	}

	/// <summary>What the dispatched registration established.</summary>
	/// <param name="Preflight">The collision pre-check status; only <see cref="InspectionStatus.NotFound" /> registers.</param>
	/// <param name="Status">The registration status CheatEngine.SDK reported, when the registration was attempted.</param>
	/// <param name="Lease">The lease, registered with the activation, when the registration succeeded.</param>
	/// <param name="TrackingFault">
	///     The fault that prevented the activation from owning the lease: a lifecycle exception, or the
	///     <see cref="ObjectDisposedException" /> of a resource registry that was already drained.
	/// </param>
	/// <param name="Compensation">The outcome of the one release made after <paramref name="TrackingFault" />.</param>
	private readonly record struct RegistrationStep(
		InspectionStatus Preflight,
		LuaOperationStatus Status,
		SymbolRegistrationLease? Lease,
		Exception? TrackingFault,
		LeaseReleaseOutcome Compensation);
}
