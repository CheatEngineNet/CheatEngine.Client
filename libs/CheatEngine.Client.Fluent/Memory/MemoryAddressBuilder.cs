using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable, handle-free builder for one target-memory address.</summary>
public readonly record struct MemoryAddressBuilder
{
	private readonly IMemoryClient? _memory;

	internal MemoryAddressBuilder(Address address, IMemoryClient? memory)
	{
		Address = address;
		_memory = memory;
	}

	/// <summary>Gets the target address used by terminal operations.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Returns an equivalent builder bound to a scoped target-memory service.</summary>
	/// <param name="memory">The scoped target-memory service used by terminal operations.</param>
	/// <returns>A new immutable builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="memory" /> is <see langword="null" />.</exception>
	public MemoryAddressBuilder Using(IMemoryClient memory)
	{
		ArgumentNullException.ThrowIfNull(memory);
		return new MemoryAddressBuilder(Address, memory);
	}

	/// <summary>Reads one built-in scalar or pointer type through the bound memory service.</summary>
	/// <typeparam name="T">The built-in scalar or pointer type to read.</typeparam>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns>The value read from <see cref="Address" />.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public T Read<T>(CancellationToken cancellationToken = default)
	{
		return RequireMemory().ReadPrimitive<T>(Address, cancellationToken);
	}

	/// <summary>Tries to read one built-in scalar or pointer type through the bound memory service.</summary>
	/// <typeparam name="T">The built-in scalar or pointer type to read.</typeparam>
	/// <param name="value">The value read when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when a value was read.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public bool TryRead<T>([MaybeNullWhen(false)] out T value, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryReadPrimitive(Address, out value, out failure, cancellationToken);
	}

	/// <summary>Writes one built-in scalar or pointer type through the bound memory service.</summary>
	/// <typeparam name="T">The built-in scalar or pointer type to write.</typeparam>
	/// <param name="value">The value to write to <see cref="Address" />.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public void Write<T>(T value, CancellationToken cancellationToken = default)
	{
		RequireMemory().WritePrimitive(Address, value, cancellationToken);
	}

	/// <summary>Tries to write one built-in scalar or pointer type through the bound memory service.</summary>
	/// <typeparam name="T">The built-in scalar or pointer type to write.</typeparam>
	/// <param name="value">The value to write to <see cref="Address" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when Cheat Engine accepted the write.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public bool TryWrite<T>(T value, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryWritePrimitive(Address, value, out failure, cancellationToken);
	}

	/// <summary>Reads one typed value through the service bound to this builder.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns>The managed value returned by Cheat Engine.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="codec" /> is <see langword="null" />.</exception>
	public T ReadWith<T>(IMemoryCodec<T> codec, CancellationToken cancellationToken = default)
	{
		return ReadWith(RequireMemory(), codec, cancellationToken);
	}

	/// <summary>Reads one typed value through an explicit target-memory service.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="memory">The scoped target-memory service used for this operation.</param>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns>The managed value returned by Cheat Engine.</returns>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="memory" /> or <paramref name="codec" /> is
	///     <see langword="null" />.
	/// </exception>
	public T ReadWith<T>(IMemoryClient memory, IMemoryCodec<T> codec,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(memory);
		ArgumentNullException.ThrowIfNull(codec);
		return memory.Read(new MemoryReadRequest<T>(Address, codec), cancellationToken);
	}

	/// <summary>Tries to read one typed value through the service bound to this builder.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="value">The managed value when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when a value was read.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="codec" /> is <see langword="null" />.</exception>
	public bool TryReadWith<T>(IMemoryCodec<T> codec, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryReadWith(RequireMemory(), codec, out value, out failure, cancellationToken);
	}

	/// <summary>Tries to read one typed value through an explicit target-memory service.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="memory">The scoped target-memory service used for this operation.</param>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="value">The managed value when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when a value was read.</returns>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="memory" /> or <paramref name="codec" /> is
	///     <see langword="null" />.
	/// </exception>
	public bool TryReadWith<T>(IMemoryClient memory, IMemoryCodec<T> codec, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(memory);
		ArgumentNullException.ThrowIfNull(codec);
		return memory.TryRead(new MemoryReadRequest<T>(Address, codec), out value, out failure, cancellationToken);
	}

	/// <summary>Writes one typed value through the service bound to this builder.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="value">The managed value to write.</param>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns>Nothing when Cheat Engine accepted the write.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="codec" /> is <see langword="null" />.</exception>
	public void WriteWith<T>(T value, IMemoryCodec<T> codec, CancellationToken cancellationToken = default)
	{
		WriteWith(RequireMemory(), value, codec, cancellationToken);
	}

	/// <summary>Writes one typed value through an explicit target-memory service.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="memory">The scoped target-memory service used for this operation.</param>
	/// <param name="value">The managed value to write.</param>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns>Nothing when Cheat Engine accepted the write.</returns>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="memory" /> or <paramref name="codec" /> is
	///     <see langword="null" />.
	/// </exception>
	public void WriteWith<T>(IMemoryClient memory, T value, IMemoryCodec<T> codec,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(memory);
		ArgumentNullException.ThrowIfNull(codec);
		memory.Write(new MemoryWriteRequest<T>(Address, value, codec), cancellationToken);
	}

	/// <summary>Tries to write one typed value through the service bound to this builder.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="value">The managed value to write.</param>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when Cheat Engine accepted the write.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="codec" /> is <see langword="null" />.</exception>
	public bool TryWriteWith<T>(T value, IMemoryCodec<T> codec, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryWriteWith(RequireMemory(), value, codec, out failure, cancellationToken);
	}

	/// <summary>Tries to write one typed value through an explicit target-memory service.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="memory">The scoped target-memory service used for this operation.</param>
	/// <param name="value">The managed value to write.</param>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when Cheat Engine accepted the write.</returns>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="memory" /> or <paramref name="codec" /> is
	///     <see langword="null" />.
	/// </exception>
	public bool TryWriteWith<T>(IMemoryClient memory, T value, IMemoryCodec<T> codec,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(memory);
		ArgumentNullException.ThrowIfNull(codec);
		return memory.TryWrite(new MemoryWriteRequest<T>(Address, value, codec), out failure, cancellationToken);
	}

	private IMemoryClient RequireMemory()
	{
		return _memory ?? throw new InvalidOperationException(
			"This memory builder has no bound target-memory service. Use Memory.At(memory, address), " +
			"memory.At(address), or bind the builder with Using(memory) before a terminal operation.");
	}
}
