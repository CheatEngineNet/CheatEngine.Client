using System.Collections.Immutable;
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

	/// <summary>Copies an exact, positive number of target bytes from this address.</summary>
	/// <param name="length">The exact positive number of bytes to copy.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns>An immutable, caller-owned byte snapshot.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public ImmutableArray<byte> ReadBytes(int length, CancellationToken cancellationToken = default)
	{
		return RequireMemory().ReadBytes(new MemoryBytesReadRequest(Address, length), cancellationToken);
	}

	/// <summary>Tries to copy an exact, positive number of target bytes from this address.</summary>
	/// <param name="length">The exact positive number of bytes to copy.</param>
	/// <param name="bytes">The immutable byte snapshot when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when the bytes were copied.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public bool TryReadBytes(int length, out ImmutableArray<byte> bytes, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryReadBytes(new MemoryBytesReadRequest(Address, length), out bytes, out failure,
			cancellationToken);
	}

	/// <summary>Copies the supplied non-empty bytes to this address.</summary>
	/// <param name="bytes">The caller-owned bytes copied into an immutable request before dispatch.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public void WriteBytes(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
	{
		RequireMemory().WriteBytes(new MemoryBytesWriteRequest(Address, bytes), cancellationToken);
	}

	/// <summary>Tries to copy the supplied non-empty bytes to this address.</summary>
	/// <param name="bytes">The caller-owned bytes copied into an immutable request before dispatch.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when Cheat Engine accepted the write.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public bool TryWriteBytes(ReadOnlySpan<byte> bytes, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryWriteBytes(new MemoryBytesWriteRequest(Address, bytes), out failure,
			cancellationToken);
	}

	/// <summary>Reads a bounded UTF-8 string from this address.</summary>
	/// <param name="maximumLength">The positive maximum length passed to Cheat Engine.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns>The copied UTF-8 text.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public string ReadUtf8(int maximumLength, CancellationToken cancellationToken = default)
	{
		return ReadString(maximumLength, MemoryStringEncoding.Utf8, cancellationToken);
	}

	/// <summary>Reads a bounded UTF-16 string from this address.</summary>
	/// <param name="maximumLength">The positive maximum length passed to Cheat Engine.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns>The copied UTF-16 text.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public string ReadUtf16(int maximumLength, CancellationToken cancellationToken = default)
	{
		return ReadString(maximumLength, MemoryStringEncoding.Utf16, cancellationToken);
	}

	/// <summary>Reads a bounded string with an explicit target encoding from this address.</summary>
	/// <param name="maximumLength">The positive maximum length passed to Cheat Engine.</param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns>The copied target text.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public string ReadString(int maximumLength, MemoryStringEncoding encoding,
		CancellationToken cancellationToken = default)
	{
		return RequireMemory().ReadString(MemoryStringReadRequest.Create(Address, maximumLength, encoding),
			cancellationToken);
	}

	/// <summary>Tries to read a bounded string with an explicit target encoding from this address.</summary>
	/// <param name="maximumLength">The positive maximum length passed to Cheat Engine.</param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <param name="value">The copied target text when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when the text was copied.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public bool TryReadString(int maximumLength, MemoryStringEncoding encoding, [NotNullWhen(true)] out string? value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryReadString(MemoryStringReadRequest.Create(Address, maximumLength, encoding),
			out value,
			out failure, cancellationToken);
	}

	/// <summary>Writes UTF-8 text whose encoded length is bounded explicitly at this address.</summary>
	/// <param name="value">The managed text to copy.</param>
	/// <param name="maximumLength">The positive maximum number of UTF-8 bytes accepted.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public void WriteUtf8(string value, int maximumLength, CancellationToken cancellationToken = default)
	{
		WriteString(value, maximumLength, MemoryStringEncoding.Utf8, cancellationToken);
	}

	/// <summary>Writes UTF-16 text whose code-unit length is bounded explicitly at this address.</summary>
	/// <param name="value">The managed text to copy.</param>
	/// <param name="maximumLength">The positive maximum number of UTF-16 code units accepted.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public void WriteUtf16(string value, int maximumLength, CancellationToken cancellationToken = default)
	{
		WriteString(value, maximumLength, MemoryStringEncoding.Utf16, cancellationToken);
	}

	/// <summary>Writes text with an explicit target encoding and maximum encoded length.</summary>
	/// <param name="value">The managed text to copy.</param>
	/// <param name="maximumLength">The positive maximum number of UTF-8 bytes or UTF-16 code units accepted.</param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public void WriteString(string value, int maximumLength, MemoryStringEncoding encoding,
		CancellationToken cancellationToken = default)
	{
		RequireMemory().WriteString(MemoryStringWriteRequest.CreateBounded(Address, value, maximumLength, encoding),
			cancellationToken);
	}

	/// <summary>Tries to write text with an explicit target encoding and maximum encoded length.</summary>
	/// <param name="value">The managed text to copy.</param>
	/// <param name="maximumLength">The positive maximum number of UTF-8 bytes or UTF-16 code units accepted.</param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Cancels before the operation reaches Cheat Engine.</param>
	/// <returns><see langword="true" /> when Cheat Engine accepted the write.</returns>
	/// <exception cref="InvalidOperationException">No memory service has been bound to this builder.</exception>
	public bool TryWriteString(string value, int maximumLength, MemoryStringEncoding encoding,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryWriteString(
			MemoryStringWriteRequest.CreateBounded(Address, value, maximumLength, encoding),
			out failure, cancellationToken);
	}

	/// <summary>Starts a finite, target-aware pointer chain from this address.</summary>
	/// <param name="offsets">The non-empty sequence of at most 64 offsets applied after each dereference.</param>
	/// <returns>An immutable chain builder that remains bound to this builder's memory service.</returns>
	public MemoryPointerChainBuilder Follow(ReadOnlySpan<long> offsets)
	{
		return new MemoryPointerChainBuilder(new PointerChainRequest(Address, offsets), _memory);
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
