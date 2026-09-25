using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable, handle-free fluent operation for one finite target-aware pointer chain.</summary>
/// <remarks>
///     <para>
///         Start it with <see cref="MemoryAddressBuilder.Follow" />, for example
///         <c>client.Memory.At(address).Follow(offsets)</c>: the chain is bound to that memory service and never rebound.
///         It is a plain value that declares no <c>Equals</c>, <c>GetHashCode</c>, <c>ToString</c> or equality operators
///         (only those inherited from <see cref="ValueType" />): compare <see cref="Request" /> values, not builders.
///     </para>
///     <para>
///         Its only constructor is the implicit parameterless one, which yields the <see langword="default" /> value:
///         that value has no target-memory service, and <see cref="Resolve" /> and <see cref="TryResolve" /> throw
///         <see cref="InvalidOperationException" /> on it.
///     </para>
///     <para>
///         <see cref="Resolve" /> calls the throwing member of the bound memory service, which the Client implements with
///         <see cref="CheatEngineFailure.Throw(CancellationToken)" />: the exception type follows
///         <see cref="CheatEngineFailure.Kind" />, and <see cref="TryResolve" /> returns the same failure instead.
///     </para>
/// </remarks>
public readonly struct MemoryPointerChainBuilder
{
	private readonly IMemoryClient? _memory;

	internal MemoryPointerChainBuilder(PointerChainRequest request, IMemoryClient memory)
	{
		Request = request;
		_memory = memory;
	}

	/// <summary>Gets the copied finite pointer-chain request.</summary>
	public PointerChainRequest Request
	{
		get;
	}

	/// <summary>Resolves every pointer dereference and offset in the chain.</summary>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>The copied final target address.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The bound memory service refused or failed the resolution.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     <paramref name="cancellationToken" /> was observed before dispatch or between Client-managed steps.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping and admits no new work, or the resolution failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	public Address Resolve(CancellationToken cancellationToken = default)
	{
		return RequireMemory().ResolvePointerChain(Request, cancellationToken);
	}

	/// <summary>Tries to resolve every pointer dereference and offset in the chain.</summary>
	/// <param name="address">The copied final target address when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when the chain was resolved.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping and admits no new work.
	/// </exception>
	public bool TryResolve(out Address address, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryResolvePointerChain(Request, out address, out failure, cancellationToken);
	}

	private IMemoryClient RequireMemory()
	{
		return _memory ?? throw new InvalidOperationException(
			"This pointer-chain builder is a default value without a target-memory service. Start the chain with " +
			"memory.At(address).Follow(offsets), for example client.Memory.At(address).Follow(offsets).");
	}
}
