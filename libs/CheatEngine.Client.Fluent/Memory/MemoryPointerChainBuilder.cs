using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable, handle-free fluent operation for one finite target-aware pointer chain.</summary>
public readonly struct MemoryPointerChainBuilder
{
	private readonly IMemoryClient? _memory;

	internal MemoryPointerChainBuilder(PointerChainRequest request, IMemoryClient? memory)
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
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
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
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
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
