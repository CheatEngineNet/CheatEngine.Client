using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Inspection;

/// <summary>Reads copied modules, sections, symbols, and memory regions from the selected Cheat Engine target.</summary>
public interface IInspectionClient
{
	/// <summary>Tries to copy the target modules, optionally for an explicit process identifier.</summary>
	public bool TryGetModules(
		InspectionCollectionRequest request,
		out ImmutableArray<ModuleInfo> modules,
		out CheatEngineFailure failure,
		TargetProcessId? processId = null,
		CancellationToken cancellationToken = default);

	/// <summary>Copies the target modules or throws when inspection fails.</summary>
	public ImmutableArray<ModuleInfo> GetModules(InspectionCollectionRequest request,
		TargetProcessId? processId = null, CancellationToken cancellationToken = default);

	/// <summary>Copies the sections belonging to one module.</summary>
	public bool TryGetModuleSections(
		ModuleName moduleName,
		InspectionCollectionRequest request,
		out ImmutableArray<ModuleSectionInfo> sections,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies one module's sections or throws when inspection fails.</summary>
	public ImmutableArray<ModuleSectionInfo> GetModuleSections(ModuleName moduleName,
		InspectionCollectionRequest request, CancellationToken cancellationToken = default);

	/// <summary>Copies the target memory-region map.</summary>
	public bool TryGetMemoryRegions(
		InspectionCollectionRequest request,
		out ImmutableArray<MemoryRegionInfo> regions,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies the target memory-region map or throws when inspection fails.</summary>
	public ImmutableArray<MemoryRegionInfo> GetMemoryRegions(InspectionCollectionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Copies the memory-region metadata containing one target address.</summary>
	public bool TryGetMemoryRegion(
		Address address,
		out MemoryRegionInfo region,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets one region or throws when inspection fails.</summary>
	public MemoryRegionInfo GetMemoryRegion(Address address, CancellationToken cancellationToken = default);

	/// <summary>Copies metadata for one Cheat Engine symbol expression.</summary>
	public bool TryGetSymbol(
		SymbolExpression expression,
		out SymbolInfo symbol,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets symbol information or throws when inspection fails.</summary>
	public SymbolInfo GetSymbol(SymbolExpression expression, CancellationToken cancellationToken = default);

	/// <summary>Resolves the best Cheat Engine symbol name for one target address.</summary>
	public bool TryResolveName(
		Address address,
		[NotNullWhen(true)] out string? name,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Resolves a symbol name or throws when no name can be resolved.</summary>
	public string ResolveName(Address address, CancellationToken cancellationToken = default);

	/// <summary>Registers a Client-owned Cheat Engine symbol and returns the lease that removes it.</summary>
	public bool TryRegisterSymbol(
		SymbolRegistration registration,
		[NotNullWhen(true)] out ISymbolRegistrationLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Registers a Client-owned symbol or throws when Cheat Engine rejects the registration.</summary>
	public ISymbolRegistrationLease RegisterSymbol(SymbolRegistration registration,
		CancellationToken cancellationToken = default);

	/// <summary>Resolves one Cheat Engine address expression with the documented SDK options.</summary>
	public bool TryResolveAddress(
		SymbolExpression expression,
		AddressResolutionOptions options,
		out Address address,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Resolves one address or throws when resolution fails.</summary>
	public Address ResolveAddress(SymbolExpression expression, AddressResolutionOptions options,
		CancellationToken cancellationToken = default);
}
