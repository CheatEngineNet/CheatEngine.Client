using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable, handle-free builder for one target-memory address.</summary>
/// <remarks>
///     <para>
///         Start it with <see cref="CheatEngineMemoryFluentExtensions.At" />, for example
///         <c>client.Memory.At(address)</c>: the builder is bound to that memory service and never rebound. It is a plain
///         value that declares no <c>Equals</c>, <c>GetHashCode</c>, <c>ToString</c> or equality operators (only those
///         inherited from <see cref="ValueType" />): compare <see cref="Address" /> values, not builders.
///     </para>
///     <para>
///         Its only constructor is the implicit parameterless one, which yields the <see langword="default" /> value:
///         that value has no target-memory service, and every terminal operation and <see cref="Follow" /> throw
///         <see cref="InvalidOperationException" /> on it.
///     </para>
///     <para>
///         Each throwing terminal calls the throwing member of the bound memory service, which the Client implements
///         with <see cref="CheatEngineFailure.Throw(CancellationToken)" />: the exception type follows
///         <see cref="CheatEngineFailure.Kind" />, and the matching <c>Try</c> form returns the same failure instead.
///     </para>
/// </remarks>
public readonly struct MemoryAddressBuilder
{
	private readonly IMemoryClient? _memory;

	internal MemoryAddressBuilder(Address address, IMemoryClient memory)
	{
		Address = address;
		_memory = memory;
	}

	/// <summary>Gets the target address used by terminal operations.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Reads one built-in scalar or pointer type through the bound memory service.</summary>
	/// <typeparam name="T">The built-in scalar or pointer type to read.</typeparam>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>The value read from <see cref="Address" />.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
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
	///     The Client activation is stopping, outside a deactivation callback, or the read failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	public T Read<T>(CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		return RequireMemory().ReadPrimitive<T>(Address, cancellationToken);
	}

	/// <summary>Tries to read one built-in scalar or pointer type through the bound memory service.</summary>
	/// <typeparam name="T">The built-in scalar or pointer type to read.</typeparam>
	/// <param name="value">The value read when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when a value was read.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryRead<T>([MaybeNullWhen(false)] out T value, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		return RequireMemory().TryReadPrimitive(Address, out value, out failure, cancellationToken);
	}

	/// <summary>Writes one built-in scalar or pointer type through the bound memory service.</summary>
	/// <typeparam name="T">The built-in scalar or pointer type to write.</typeparam>
	/// <param name="value">The value to write to <see cref="Address" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
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
	///     The Client activation is stopping, outside a deactivation callback, or the write failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	public void Write<T>(T value, CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		RequireMemory().WritePrimitive(Address, value, cancellationToken);
	}

	/// <summary>Tries to write one built-in scalar or pointer type through the bound memory service.</summary>
	/// <typeparam name="T">The built-in scalar or pointer type to write.</typeparam>
	/// <param name="value">The value to write to <see cref="Address" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when Cheat Engine accepted the write.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryWrite<T>(T value, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		return RequireMemory().TryWritePrimitive(Address, value, out failure, cancellationToken);
	}

	/// <summary>Copies an exact, positive number of target bytes from this address.</summary>
	/// <param name="length">The exact positive number of bytes to copy.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>An immutable, caller-owned byte snapshot.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> is zero or negative.</exception>
	/// <exception cref="CheatEngineOperationException">
	///     The bound memory service refused or failed the copy.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     <paramref name="cancellationToken" /> was observed before dispatch or between Client-managed steps.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping, outside a deactivation callback, or the copy failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	public ImmutableArray<byte> ReadBytes(int length, CancellationToken cancellationToken = default)
	{
		return RequireMemory().ReadBytes(new MemoryBytesReadRequest(Address, length), cancellationToken);
	}

	/// <summary>Tries to copy an exact, positive number of target bytes from this address.</summary>
	/// <param name="length">The exact positive number of bytes to copy.</param>
	/// <param name="bytes">The immutable byte snapshot when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when the bytes were copied.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> is zero or negative.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryReadBytes(int length, out ImmutableArray<byte> bytes, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryReadBytes(new MemoryBytesReadRequest(Address, length), out bytes, out failure,
			cancellationToken);
	}

	/// <summary>Copies the supplied non-empty bytes to this address.</summary>
	/// <param name="bytes">The caller-owned bytes copied into an immutable request before dispatch.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="bytes" /> is empty.</exception>
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
	///     The Client activation is stopping, outside a deactivation callback, or the write failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	public void WriteBytes(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
	{
		RequireMemory().WriteBytes(new MemoryBytesWriteRequest(Address, bytes), cancellationToken);
	}

	/// <summary>Tries to copy the supplied non-empty bytes to this address.</summary>
	/// <param name="bytes">The caller-owned bytes copied into an immutable request before dispatch.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when Cheat Engine accepted the write.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="bytes" /> is empty.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryWriteBytes(ReadOnlySpan<byte> bytes, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryWriteBytes(new MemoryBytesWriteRequest(Address, bytes), out failure,
			cancellationToken);
	}

	/// <summary>Reads a bounded string with an explicit target encoding from this address.</summary>
	/// <param name="maximumLength">
	///     The positive value passed unchanged as Cheat Engine's <c>readString</c> <c>maxlength</c> argument, a host-side
	///     bound whose unit is not documented (see <see cref="MemoryStringReadRequest.MaximumLength" />).
	/// </param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>The copied target text.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="maximumLength" /> is zero or negative, or <paramref name="encoding" /> is not defined.
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
	///     The Client activation is stopping, outside a deactivation callback, or the read failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	public string ReadString(int maximumLength, MemoryStringEncoding encoding,
		CancellationToken cancellationToken = default)
	{
		return RequireMemory().ReadString(new MemoryStringReadRequest(Address, maximumLength, encoding),
			cancellationToken);
	}

	/// <summary>Tries to read a bounded string with an explicit target encoding from this address.</summary>
	/// <param name="maximumLength">
	///     The positive value passed unchanged as Cheat Engine's <c>readString</c> <c>maxlength</c> argument, a host-side
	///     bound whose unit is not documented (see <see cref="MemoryStringReadRequest.MaximumLength" />).
	/// </param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <param name="value">The copied target text when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when the text was copied.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="maximumLength" /> is zero or negative, or <paramref name="encoding" /> is not defined.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryReadString(int maximumLength, MemoryStringEncoding encoding, [NotNullWhen(true)] out string? value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryReadString(new MemoryStringReadRequest(Address, maximumLength, encoding),
			out value,
			out failure, cancellationToken);
	}

	/// <summary>Writes text with an explicit target encoding and maximum encoded length.</summary>
	/// <param name="value">The managed text to copy.</param>
	/// <param name="maximumLength">The positive maximum number of UTF-8 bytes or UTF-16 code units accepted.</param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="maximumLength" /> is zero or negative, or <paramref name="encoding" /> is not defined.
	/// </exception>
	/// <exception cref="ArgumentException">The encoded text exceeds <paramref name="maximumLength" />.</exception>
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
	///     The Client activation is stopping, outside a deactivation callback, or the write failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	public void WriteString(string value, int maximumLength, MemoryStringEncoding encoding,
		CancellationToken cancellationToken = default)
	{
		RequireMemory().WriteString(new MemoryStringWriteRequest(Address, value, maximumLength, encoding),
			cancellationToken);
	}

	/// <summary>Tries to write text with an explicit target encoding and maximum encoded length.</summary>
	/// <param name="value">The managed text to copy.</param>
	/// <param name="maximumLength">The positive maximum number of UTF-8 bytes or UTF-16 code units accepted.</param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when Cheat Engine accepted the write.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="maximumLength" /> is zero or negative, or <paramref name="encoding" /> is not defined.
	/// </exception>
	/// <exception cref="ArgumentException">The encoded text exceeds <paramref name="maximumLength" />.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryWriteString(string value, int maximumLength, MemoryStringEncoding encoding,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		return RequireMemory().TryWriteString(
			new MemoryStringWriteRequest(Address, value, maximumLength, encoding),
			out failure, cancellationToken);
	}

	/// <summary>Starts a finite, target-aware pointer chain from this address.</summary>
	/// <param name="offsets">The non-empty sequence of at most 64 offsets applied after each dereference.</param>
	/// <returns>An immutable chain builder bound to this builder's memory service.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="offsets" /> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offsets" /> holds more than 64 offsets.</exception>
	public MemoryPointerChainBuilder Follow(ReadOnlySpan<long> offsets)
	{
		IMemoryClient memory = RequireMemory();
		return new MemoryPointerChainBuilder(new PointerChainRequest(Address, offsets), memory);
	}

	/// <summary>Reads one typed value through the service bound to this builder.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns>The managed value returned by Cheat Engine.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="codec" /> is <see langword="null" />.</exception>
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
	///     The Client activation is stopping (outside a deactivation callback, or inside one when the codec uses its
	///     context), or the read failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	public T ReadWith<T>(IMemoryCodec<T> codec, CancellationToken cancellationToken = default)
	{
		IMemoryClient memory = RequireMemory();
		ArgumentNullException.ThrowIfNull(codec);
		return memory.Read(new MemoryReadRequest<T>(Address, codec), cancellationToken);
	}

	/// <summary>Tries to read one typed value through the service bound to this builder.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="value">The managed value when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when a value was read.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="codec" /> is <see langword="null" />.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping (outside a deactivation callback, or inside one when the codec uses its
	///     context).
	/// </exception>
	public bool TryReadWith<T>(IMemoryCodec<T> codec, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		IMemoryClient memory = RequireMemory();
		ArgumentNullException.ThrowIfNull(codec);
		return memory.TryRead(new MemoryReadRequest<T>(Address, codec), out value, out failure, cancellationToken);
	}

	/// <summary>Writes one typed value through the service bound to this builder.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="value">The managed value to write.</param>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="codec" /> is <see langword="null" />.</exception>
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
	///     The Client activation is stopping (outside a deactivation callback, or inside one when the codec uses its
	///     context), or the write failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	public void WriteWith<T>(T value, IMemoryCodec<T> codec, CancellationToken cancellationToken = default)
	{
		IMemoryClient memory = RequireMemory();
		ArgumentNullException.ThrowIfNull(codec);
		memory.Write(new MemoryWriteRequest<T>(Address, value, codec), cancellationToken);
	}

	/// <summary>Tries to write one typed value through the service bound to this builder.</summary>
	/// <typeparam name="T">The managed value type represented by <paramref name="codec" />.</typeparam>
	/// <param name="value">The managed value to write.</param>
	/// <param name="codec">The deterministic codec that maps <typeparamref name="T" /> to Cheat Engine memory.</param>
	/// <param name="failure">The classified operation failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">
	///     Observed before dispatch and between Client-managed steps; it never interrupts a Cheat Engine call that has
	///     already started (see <see cref="CheatEngine.Client.Results.CheatEngineFailure.HostEffect" />).
	/// </param>
	/// <returns><see langword="true" /> when Cheat Engine accepted the write.</returns>
	/// <exception cref="InvalidOperationException">
	///     This builder is the <see langword="default" /> value, which has no target-memory service.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="codec" /> is <see langword="null" />.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The Client activation that owns the memory service has ended.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The Client activation is stopping (outside a deactivation callback, or inside one when the codec uses its
	///     context).
	/// </exception>
	public bool TryWriteWith<T>(T value, IMemoryCodec<T> codec, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		IMemoryClient memory = RequireMemory();
		ArgumentNullException.ThrowIfNull(codec);
		return memory.TryWrite(new MemoryWriteRequest<T>(Address, value, codec), out failure, cancellationToken);
	}

	private IMemoryClient RequireMemory()
	{
		return _memory ?? throw new InvalidOperationException(
			"This memory builder is a default value without a target-memory service. Start it with " +
			"memory.At(address), for example client.Memory.At(address).");
	}
}
