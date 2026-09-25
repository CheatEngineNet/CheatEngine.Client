using System.Collections.Immutable;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable, handle-free fluent terminal for one bounded homogeneous scalar batch.</summary>
/// <typeparam name="T">
///     The built-in scalar or target-aware pointer type, one of the primitives <see cref="IMemoryClient" /> supports.
/// </typeparam>
public readonly struct MemoryPrimitiveBatchBuilder<T>
	where T : unmanaged
{
	private readonly IMemoryClient? _memory;

	internal MemoryPrimitiveBatchBuilder(IMemoryClient memory)
	{
		_memory = memory ?? throw new ArgumentNullException(nameof(memory));
	}

	/// <summary>Copies one homogeneous scalar from every supplied target address in one dispatch admission.</summary>
	/// <param name="addresses">The non-empty target addresses to read in result order.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>The immutable scalar snapshot in the same order as <paramref name="addresses" />.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public ImmutableArray<T> Read(ReadOnlySpan<Address> addresses, CancellationToken cancellationToken = default)
	{
		return RequireMemory().ReadPrimitiveBatch(new MemoryPrimitiveBatchReadRequest<T>(addresses), cancellationToken);
	}

	/// <summary>Tries to copy one homogeneous scalar from every supplied target address in one dispatch admission.</summary>
	/// <param name="addresses">The non-empty target addresses to read in result order.</param>
	/// <param name="values">The immutable scalar snapshot when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when all scalar reads succeeded.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public bool TryRead(ReadOnlySpan<Address> addresses, out ImmutableArray<T> values, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryReadPrimitiveBatch(new MemoryPrimitiveBatchReadRequest<T>(addresses), out values,
			out failure, cancellationToken);
	}

	/// <summary>Writes every copied homogeneous scalar in one dispatch admission.</summary>
	/// <param name="values">The non-empty address/value pairs to write in execution order.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public void Write(ReadOnlySpan<MemoryAddressValue<T>> values, CancellationToken cancellationToken = default)
	{
		RequireMemory().WritePrimitiveBatch(new MemoryPrimitiveBatchWriteRequest<T>(values), cancellationToken);
	}

	/// <summary>Tries to write every copied homogeneous scalar in one dispatch admission.</summary>
	/// <param name="values">The non-empty address/value pairs to write in execution order.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when all scalar writes succeeded.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public bool TryWrite(ReadOnlySpan<MemoryAddressValue<T>> values, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryWritePrimitiveBatch(new MemoryPrimitiveBatchWriteRequest<T>(values), out failure,
			cancellationToken);
	}

	private IMemoryClient RequireMemory()
	{
		return _memory ?? throw new InvalidOperationException(
			"This primitive-batch builder is a default value without a target-memory service. Start it with " +
			"memory.Batch<T>(), for example client.Memory.Batch<T>().");
	}
}
