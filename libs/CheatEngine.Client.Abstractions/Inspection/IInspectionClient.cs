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
///     <para>
///         After its arguments, every member checks the activation: an ended activation throws
///         <see cref="CheatEngineActivationExpiredException" /> and a stopping one
///         <see cref="CheatEngineInvalidStateException" />, except that a deactivation callback can still call every
///         member but <see cref="TryRegisterSymbol" /> and <see cref="RegisterSymbol" /> on Cheat Engine's main
///         thread (see <see cref="ICheatEngineClient" />). A <c>Try</c> member returns every other failure; the
///         throwing member with the same inputs throws it through
///         <see cref="CheatEngineFailure.Throw(CancellationToken)" />.
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
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryGetModules(
		InspectionCollectionRequest request,
		TargetProcessId? processId,
		out ImmutableArray<ModuleInfo> modules,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies the target modules or throws when inspection fails.</summary>
	/// <param name="request">The bound on the number of copied modules.</param>
	/// <param name="processId">
	///     The process whose modules are copied, or <see langword="null" /> for Cheat Engine's selected target.
	/// </param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns>The copied modules.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no item,
	///     or <paramref name="processId" /> is not positive.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or inspection failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     Inspection observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">Inspection failed with any other failure kind.</exception>
	public ImmutableArray<ModuleInfo> GetModules(InspectionCollectionRequest request,
		TargetProcessId? processId = null, CancellationToken cancellationToken = default);

	/// <summary>Copies the sections belonging to one module.</summary>
	/// <param name="moduleName">The name of the module whose sections are copied.</param>
	/// <param name="request">The bound on the number of copied sections.</param>
	/// <param name="sections">The copied sections on success; otherwise an empty array.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns><see langword="true" /> when the sections were copied.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="moduleName" /> is the <see langword="default" /> name, or <paramref name="request" /> is the
	///     <see langword="default" /> request (an <see cref="ArgumentOutOfRangeException" />).
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryGetModuleSections(
		ModuleName moduleName,
		InspectionCollectionRequest request,
		out ImmutableArray<ModuleSectionInfo> sections,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies one module's sections or throws when inspection fails.</summary>
	/// <param name="moduleName">The name of the module whose sections are copied.</param>
	/// <param name="request">The bound on the number of copied sections.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns>The copied sections.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="moduleName" /> is the <see langword="default" /> name, or <paramref name="request" /> is the
	///     <see langword="default" /> request (an <see cref="ArgumentOutOfRangeException" />).
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or inspection failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     Inspection observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">Inspection failed with any other failure kind.</exception>
	public ImmutableArray<ModuleSectionInfo> GetModuleSections(ModuleName moduleName,
		InspectionCollectionRequest request, CancellationToken cancellationToken = default);

	/// <summary>Copies the target memory-region map.</summary>
	/// <param name="request">The bound on the number of copied regions.</param>
	/// <param name="regions">The copied regions on success; otherwise an empty array.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns><see langword="true" /> when the regions were copied.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no item.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryGetMemoryRegions(
		InspectionCollectionRequest request,
		out ImmutableArray<MemoryRegionInfo> regions,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies the target memory-region map or throws when inspection fails.</summary>
	/// <param name="request">The bound on the number of copied regions.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns>The copied regions.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no item.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or inspection failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     Inspection observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">Inspection failed with any other failure kind.</exception>
	public ImmutableArray<MemoryRegionInfo> GetMemoryRegions(InspectionCollectionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Copies the memory-region metadata containing one target address.</summary>
	/// <param name="address">The target address whose region is copied.</param>
	/// <param name="region">The copied region on success; otherwise the default value.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns><see langword="true" /> when the region was copied.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryGetMemoryRegion(
		Address address,
		out MemoryRegionInfo region,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets one region or throws when inspection fails.</summary>
	/// <param name="address">The target address whose region is copied.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns>The copied region.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or inspection failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     Inspection observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">Inspection failed with any other failure kind.</exception>
	public MemoryRegionInfo GetMemoryRegion(Address address, CancellationToken cancellationToken = default);

	/// <summary>Copies metadata for one Cheat Engine symbol expression.</summary>
	/// <param name="expression">The symbol expression to look up.</param>
	/// <param name="symbol">The copied symbol metadata on success; otherwise the default value.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns><see langword="true" /> when the symbol was found and copied.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="expression" /> is the <see langword="default" /> expression.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryGetSymbol(
		SymbolExpression expression,
		out SymbolInfo symbol,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets symbol information or throws when inspection fails.</summary>
	/// <param name="expression">The symbol expression to look up.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns>The copied symbol metadata.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="expression" /> is the <see langword="default" /> expression.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or inspection failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     Inspection observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">Inspection failed with any other failure kind.</exception>
	public SymbolInfo GetSymbol(SymbolExpression expression, CancellationToken cancellationToken = default);

	/// <summary>Resolves the best Cheat Engine symbol name for one target address.</summary>
	/// <param name="address">The target address to name.</param>
	/// <param name="name">The resolved name on success; otherwise <see langword="null" />.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns><see langword="true" /> when a name was resolved.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryResolveName(
		Address address,
		[NotNullWhen(true)] out string? name,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Resolves a symbol name or throws when no name can be resolved.</summary>
	/// <param name="address">The target address to name.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns>The resolved name.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or resolution failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     Resolution observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">Resolution failed with any other failure kind.</exception>
	public string ResolveName(Address address, CancellationToken cancellationToken = default);

	/// <summary>Registers a Client-owned Cheat Engine symbol and returns the lease that removes it.</summary>
	/// <param name="registration">The symbol name, its address and whether a saved table omits it.</param>
	/// <param name="lease">The lease that owns the symbol on success; release it when done.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the registration is dispatched.</param>
	/// <returns><see langword="true" /> when the symbol was registered and its lease published.</returns>
	/// <remarks>
	///     A name that already resolves, or that this activation already registered, is refused before Cheat Engine
	///     registers anything.
	/// </remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="registration" /> is the <see langword="default" /> registration, which names no symbol.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping: no new lease is created while it stops.
	/// </exception>
	public bool TryRegisterSymbol(
		SymbolRegistration registration,
		[NotNullWhen(true)] out ISymbolRegistrationLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Registers a Client-owned symbol or throws when Cheat Engine rejects the registration.</summary>
	/// <param name="registration">The symbol name, its address and whether a saved table omits it.</param>
	/// <param name="cancellationToken">Observed before the registration is dispatched.</param>
	/// <returns>The lease that owns the symbol; release it when done.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="registration" /> is the <see langword="default" /> registration, which names no symbol.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the registration failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The registration observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The registration failed with any other failure kind.</exception>
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
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryResolveAddress(
		SymbolExpression expression,
		AddressResolutionMode mode,
		out Address address,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Resolves one address or throws when resolution fails.</summary>
	/// <param name="expression">
	///     The address expression, for example <c>game.exe+10</c> or a registered symbol name.
	/// </param>
	/// <param name="mode">How Cheat Engine resolves the expression.</param>
	/// <param name="cancellationToken">Observed before the work is dispatched.</param>
	/// <returns>The resolved address.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="expression" /> is the <see langword="default" /> expression.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="mode" /> is not a defined value.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or resolution failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     Resolution observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     Resolution failed with any other failure kind, <see cref="CheatEngineFailureKind.NotFound" /> included.
	/// </exception>
	public Address ResolveAddress(SymbolExpression expression, AddressResolutionMode mode,
		CancellationToken cancellationToken = default);
}
