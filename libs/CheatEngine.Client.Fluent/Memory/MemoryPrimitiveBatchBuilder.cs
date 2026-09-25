using System.Collections.Immutable;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable, handle-free fluent terminal for one bounded homogeneous scalar batch.</summary>
/// <typeparam name="T">
///     The built-in scalar or target-aware pointer type, one of the primitives <see cref="IMemoryClient" /> supports.
/// </typeparam>
/// <remarks>
///     <para>
///         Start it with <see cref="CheatEngineMemoryFluentExtensions.Batch{T}" />, for example
///         <c>client.Memory.Batch&lt;int&gt;()</c>: the batch is bound to that memory service and never rebound. It is a
///         plain value that declares no <c>Equals</c>, <c>GetHashCode</c>, <c>ToString</c> or equality operators (only
///         those inherited from <see cref="ValueType" />).
///     </para>
///     <para>
///         Its only constructor is the implicit parameterless one, which yields the <see langword="default" /> value:
///         that value has no target-memory service, and every terminal operation throws
///         <see cref="InvalidOperationException" /> on it, before it builds or dispatches a request.
///     </para>
///     <para>
///         Each throwing terminal calls the throwing member of the bound memory service, which the Client implements
///         with <see cref="CheatEngineFailure.Throw(CancellationToken)" />: the exception type follows
///         <see cref="CheatEngineFailure.Kind" />, and the matching <c>Try</c> form returns the same failure instead.
///     </para>
/// </remarks>
public readonly struct MemoryPrimitiveBatchBuilder<T>
	where T : unmanaged
{
	private readonly IMemoryClient? _memory;

	internal MemoryPrimitiveBatchBuilder(IMemoryClient memory)
	{
		_memory = memory;
	}

	/// <summary>Copies one homogeneous scalar from every supplied target address in one dispatch admission.</summary>
	/// <param name="addresses">The non-empty target addresses to read in result order.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>The immutable scalar snapshot in the same order as <paramref name="addresses" />.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="addresses" /> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="addresses" /> holds more than <see cref="MemoryBatchLimits.MaximumOperationCount" />
	///     addresses.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The bound memory service refused or failed the read.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     <paramref name="cancellationToken" /> was observed before dispatch or between Client-managed steps.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping and admits no new work, or the read failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
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
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="addresses" /> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="addresses" /> holds more than <see cref="MemoryBatchLimits.MaximumOperationCount" />
	///     addresses.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping and admits no new work.
	/// </exception>
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
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="values" /> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="values" /> holds more than <see cref="MemoryBatchLimits.MaximumOperationCount" /> values.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The bound memory service refused or failed the write.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     <paramref name="cancellationToken" /> was observed before dispatch or between Client-managed steps.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping and admits no new work, or the write failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
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
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="values" /> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="values" /> holds more than <see cref="MemoryBatchLimits.MaximumOperationCount" /> values.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping and admits no new work.
	/// </exception>
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
