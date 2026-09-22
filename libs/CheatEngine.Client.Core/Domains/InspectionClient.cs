using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

internal sealed class InspectionClient(
	SdkMainThreadDispatcher dispatcher,
	CoreLifetime lifetime,
	IInspectionPort? inspection = null) : IInspectionClient
{
	private readonly SdkMainThreadDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	private readonly IInspectionPort _inspection = inspection ?? new SdkInspectionPort();

	private readonly CoreLifetime _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
	private readonly HashSet<string> _registeredSymbolNames = new(StringComparer.Ordinal);
	private readonly Lock _registeredSymbolNamesLock = new();

	public bool TryGetModules(InspectionCollectionRequest request, out ImmutableArray<ModuleInfo> modules,
		out CheatEngineFailure failure, TargetProcessId? processId = null,
		CancellationToken cancellationToken = default)
	{
		ImmutableArray<ModuleInfo> result = ImmutableArray<ModuleInfo>.Empty;
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!_dispatcher.TryInvoke(() =>
			{
				ModuleInfo[] buffer = new ModuleInfo[request.MaximumItems];
				status = processId.HasValue
					? _inspection.EnumerateModules(processId.Value, buffer, out int written)
					: _inspection.EnumerateModules(buffer, out written);
				if (status == InspectionStatus.Success)
				{
					result = ImmutableArray.Create(buffer, 0, written);
				}
			}, out failure, cancellationToken))
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
		if (TryGetModules(request, out ImmutableArray<ModuleInfo> result, out CheatEngineFailure failure, processId,
				cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return [];
	}

	public bool TryGetModuleSections(ModuleName moduleName, InspectionCollectionRequest request,
		out ImmutableArray<ModuleSectionInfo> sections, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ImmutableArray<ModuleSectionInfo> result = ImmutableArray<ModuleSectionInfo>.Empty;
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!_dispatcher.TryInvoke(() =>
			{
				ModuleSectionInfo[] buffer = new ModuleSectionInfo[request.MaximumItems];
				status = _inspection.EnumerateSections(moduleName, buffer, out int written);
				if (status == InspectionStatus.Success)
				{
					result = ImmutableArray.Create(buffer, 0, written);
				}
			}, out failure, cancellationToken))
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

		failure.Throw();
		return [];
	}

	public bool TryGetMemoryRegions(InspectionCollectionRequest request,
		out ImmutableArray<MemoryRegionInfo> regions, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ImmutableArray<MemoryRegionInfo> result = [];
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!_dispatcher.TryInvoke(() =>
			{
				MemoryRegionInfo[] buffer = new MemoryRegionInfo[request.MaximumItems];
				status = _inspection.EnumerateMemoryRegions(buffer, out int written);
				if (status == InspectionStatus.Success)
				{
					result = ImmutableArray.Create(buffer, 0, written);
				}
			}, out failure, cancellationToken))
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

		failure.Throw();
		return [];
	}

	public bool TryGetMemoryRegion(Address address, out MemoryRegionInfo region,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		MemoryRegionInfo captured = default;
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!_dispatcher.TryInvoke(() => status = _inspection.GetMemoryRegion(address, out captured),
				out failure, cancellationToken))
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

		failure.Throw();
		return default;
	}

	public bool TryGetSymbol(SymbolExpression expression, out SymbolInfo symbol,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		SymbolInfo captured = default;
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!_dispatcher.TryInvoke(() => status = _inspection.GetSymbol(expression, out captured),
				out failure, cancellationToken))
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

		failure.Throw();
		return default;
	}

	public bool TryResolveName(Address address, [NotNullWhen(true)] out string? name, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		string? captured = null;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() =>
					succeeded = _inspection.TryResolveName(ToNativeAddress(address), out captured),
				out failure,
				cancellationToken))
		{
			name = null;
			return false;
		}

		if (succeeded && captured is not null)
		{
			name = captured;
			return true;
		}

		name = null;
		failure = succeeded
			? new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, "Inspection.ResolveName",
				"Cheat Engine returned an invalid symbol-name result.")
			: new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Inspection.ResolveName",
				"Cheat Engine did not return a symbol name for the requested address.");
		return false;
	}

	public string ResolveName(Address address, CancellationToken cancellationToken = default)
	{
		if (TryResolveName(address, out string? result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return string.Empty;
	}

	public bool TryRegisterSymbol(SymbolRegistration registration,
		[NotNullWhen(true)] out ISymbolRegistrationLease? lease,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		lease = null;
		_lifetime.ThrowIfInactive("Inspection.RegisterSymbol");
		if (!TryReserveSymbolName(registration.Name, out failure))
		{
			return false;
		}

		if (!_dispatcher.TryInvoke(() => _inspection.RegisterSymbol(registration.Name,
				ToNativeAddress(registration.Address), registration.DoNotSave), out failure, cancellationToken))
		{
			ReleaseSymbolName(registration.Name);
			return false;
		}

		SymbolRegistrationLease created = new(
			registration,
			_dispatcher,
			lease => _lifetime.Untrack(lease),
			_inspection.UnregisterSymbol,
			ReleaseSymbolName);
		try
		{
			_lifetime.Track(created);
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
				// The original lifecycle failure is the meaningful result. Release the name below only if disposal did not.
			}

			if (!created.IsReleased)
			{
				ReleaseSymbolName(registration.Name);
			}

			failure = CoreFailureFactory.FromException("Inspection.RegisterSymbol", exception);
			return false;
		}
	}

	public ISymbolRegistrationLease RegisterSymbol(SymbolRegistration registration,
		CancellationToken cancellationToken = default)
	{
		if (TryRegisterSymbol(registration, out ISymbolRegistrationLease? result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		failure.Throw();
		throw new InvalidOperationException("Unreachable failure flow.");
	}

	public bool TryResolveAddress(SymbolExpression expression, AddressResolutionOptions options,
		out Address address, out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		Address captured = Address.Zero;
		InspectionStatus status = InspectionStatus.InvalidResult;
		if (!_dispatcher.TryInvoke(() => status = _inspection.ResolveAddress(expression, options, out captured),
				out failure, cancellationToken))
		{
			address = default;
			return false;
		}

		address = captured;
		return TryMap(status, "Inspection.ResolveAddress", out failure);
	}

	public Address ResolveAddress(SymbolExpression expression, AddressResolutionOptions options,
		CancellationToken cancellationToken = default)
	{
		if (TryResolveAddress(expression, options, out Address result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	private static bool TryMap(InspectionStatus status, string operation, out CheatEngineFailure failure)
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
			_ => CheatEngineFailureKind.Unknown
		};
		failure = new CheatEngineFailure(kind, operation, $"Cheat Engine inspection returned '{status}'.");
		return false;
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

		failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, "Inspection.RegisterSymbol",
			"This client activation already owns a symbol registration with the requested name.");
		return false;
	}

	private void ReleaseSymbolName(string name)
	{
		lock (_registeredSymbolNamesLock)
		{
			_registeredSymbolNames.Remove(name);
		}
	}

	private static nuint ToNativeAddress(Address address)
	{
		return checked((nuint) address.Value);
	}
}
