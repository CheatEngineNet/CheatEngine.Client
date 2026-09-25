using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Inspection;

/// <summary>Reads copied modules, sections, symbols, and memory regions from the selected Cheat Engine target.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         A <see langword="default" /> or out-of-range argument is a programming error, thrown before the activation
///         check and before any Cheat Engine call: an <see cref="ArgumentOutOfRangeException" /> for a
///         <see langword="default" /> <see cref="InspectionCollectionRequest" />, which allows no item, or a process
///         identifier that is not positive, and an <see cref="ArgumentException" /> for a <see langword="default" />
///         <see cref="ModuleName" />, <see cref="SymbolExpression" /> or <see cref="SymbolRegistration" />, which name
///         nothing.
///     </para>
/// </remarks>
public interface IInspectionClient
{
	/// <summary>Tries to copy the target modules, for the selected target or an explicit process identifier.</summary>
	/// <param name="request">The bound on the number of copied modules.</param>
	/// <param name="processId">
	///     The process whose modules are copied, or <see langword="null" /> for Cheat Engine's selected target.
	/// </param>
	/// <param name="modules">The copied modules when the method returns <see langword="true" />.</param>
	/// <param name="failure">The failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns><see langword="true" /> when the modules were copied.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no item,
	///     or <paramref name="processId" /> is not positive.
	/// </exception>
	public bool TryGetModules(
		InspectionCollectionRequest request,
		TargetProcessId? processId,
		out ImmutableArray<ModuleInfo> modules,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies the target modules or throws when inspection fails.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no item,
	///     or <paramref name="processId" /> is not positive.
	/// </exception>
	public ImmutableArray<ModuleInfo> GetModules(InspectionCollectionRequest request,
		TargetProcessId? processId = null, CancellationToken cancellationToken = default);

	/// <summary>Copies the sections belonging to one module.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="moduleName" /> is the <see langword="default" /> name, or <paramref name="request" /> is the
	///     <see langword="default" /> request (an <see cref="ArgumentOutOfRangeException" />).
	/// </exception>
	public bool TryGetModuleSections(
		ModuleName moduleName,
		InspectionCollectionRequest request,
		out ImmutableArray<ModuleSectionInfo> sections,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies one module's sections or throws when inspection fails.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="moduleName" /> is the <see langword="default" /> name, or <paramref name="request" /> is the
	///     <see langword="default" /> request (an <see cref="ArgumentOutOfRangeException" />).
	/// </exception>
	public ImmutableArray<ModuleSectionInfo> GetModuleSections(ModuleName moduleName,
		InspectionCollectionRequest request, CancellationToken cancellationToken = default);

	/// <summary>Copies the target memory-region map.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no item.
	/// </exception>
	public bool TryGetMemoryRegions(
		InspectionCollectionRequest request,
		out ImmutableArray<MemoryRegionInfo> regions,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies the target memory-region map or throws when inspection fails.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no item.
	/// </exception>
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
	/// <exception cref="ArgumentException">
	///     <paramref name="expression" /> is the <see langword="default" /> expression.
	/// </exception>
	public bool TryGetSymbol(
		SymbolExpression expression,
		out SymbolInfo symbol,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets symbol information or throws when inspection fails.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="expression" /> is the <see langword="default" /> expression.
	/// </exception>
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
	/// <exception cref="ArgumentException">
	///     <paramref name="registration" /> is the <see langword="default" /> registration, which names no symbol.
	/// </exception>
	public bool TryRegisterSymbol(
		SymbolRegistration registration,
		[NotNullWhen(true)] out ISymbolRegistrationLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Registers a Client-owned symbol or throws when Cheat Engine rejects the registration.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="registration" /> is the <see langword="default" /> registration, which names no symbol.
	/// </exception>
	public ISymbolRegistrationLease RegisterSymbol(SymbolRegistration registration,
		CancellationToken cancellationToken = default);

	/// <summary>Resolves one Cheat Engine address expression in the target process's symbol table.</summary>
	/// <param name="expression">The address expression, for example <c>game.exe+10</c> or a registered symbol name.</param>
	/// <param name="mode">How Cheat Engine resolves the expression.</param>
	/// <param name="address">The resolved address when the method returns <see langword="true" />.</param>
	/// <param name="failure">
	///     The failure when the method returns <see langword="false" />; <see cref="CheatEngineFailureKind.NotFound" />
	///     when the expression does not resolve.
	/// </param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns><see langword="true" /> when the expression resolved.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="expression" /> is the <see langword="default" /> expression.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="mode" /> is not a defined value.</exception>
	public bool TryResolveAddress(
		SymbolExpression expression,
		AddressResolutionMode mode,
		out Address address,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Resolves one address or throws when resolution fails.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="expression" /> is the <see langword="default" /> expression.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="mode" /> is not a defined value.</exception>
	public Address ResolveAddress(SymbolExpression expression, AddressResolutionMode mode,
		CancellationToken cancellationToken = default);
}
