using System.Collections.Immutable;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable, handle-free fluent terminal for one bounded homogeneous scalar batch.</summary>
/// <typeparam name="T">The built-in scalar or target-aware pointer type.</typeparam>
public readonly struct MemoryPrimitiveBatchBuilder<T>
{
	private readonly IMemoryClient _memory;

	internal MemoryPrimitiveBatchBuilder(IMemoryClient memory)
	{
		_memory = memory ?? throw new ArgumentNullException(nameof(memory));
	}

	/// <summary>Copies one homogeneous scalar from every supplied target address in one dispatch admission.</summary>
	/// <param name="addresses">The non-empty target addresses to read in result order.</param>
	/// <param name="cancellationToken">Cancels before the batch reaches Cheat Engine.</param>
	/// <returns>The immutable scalar snapshot in the same order as <paramref name="addresses" />.</returns>
	public ImmutableArray<T> Read(ReadOnlySpan<Address> addresses, CancellationToken cancellationToken = default)
	{
		return _memory.ReadPrimitiveBatch(new MemoryPrimitiveBatchReadRequest<T>(addresses), cancellationToken);
	}

	/// <summary>Tries to copy one homogeneous scalar from every supplied target address in one dispatch admission.</summary>
	/// <param name="addresses">The non-empty target addresses to read in result order.</param>
	/// <param name="values">The immutable scalar snapshot when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the batch reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when all scalar reads succeeded.</returns>
	public bool TryRead(ReadOnlySpan<Address> addresses, out ImmutableArray<T> values, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return _memory.TryReadPrimitiveBatch(new MemoryPrimitiveBatchReadRequest<T>(addresses), out values,
			out failure, cancellationToken);
	}

	/// <summary>Writes every copied homogeneous scalar in one dispatch admission.</summary>
	/// <param name="values">The non-empty address/value pairs to write in execution order.</param>
	/// <param name="cancellationToken">Cancels before the batch reaches Cheat Engine.</param>
	public void Write(ReadOnlySpan<MemoryAddressValue<T>> values, CancellationToken cancellationToken = default)
	{
		_memory.WritePrimitiveBatch(new MemoryPrimitiveBatchWriteRequest<T>(values), cancellationToken);
	}

	/// <summary>Tries to write every copied homogeneous scalar in one dispatch admission.</summary>
	/// <param name="values">The non-empty address/value pairs to write in execution order.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the batch reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when all scalar writes succeeded.</returns>
	public bool TryWrite(ReadOnlySpan<MemoryAddressValue<T>> values, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return _memory.TryWritePrimitiveBatch(new MemoryPrimitiveBatchWriteRequest<T>(values), out failure,
			cancellationToken);
	}
}
