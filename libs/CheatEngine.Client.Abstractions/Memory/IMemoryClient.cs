using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>Executes typed target-memory reads and writes without exposing a Lua state.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         Two typed routes exist, and the Client never resolves a codec implicitly. The primitive members take
///         <c>where T : unmanaged</c> and support exactly the 8-, 16-, 32- and 64-bit signed and unsigned integers,
///         <see cref="float" />, <see cref="double" /> and <see cref="Address" /> (a target pointer, read and written at the
///         observed target bitness). Any other <c>T</c> is refused with
///         <see cref="CheatEngineFailureKind.OperationRejected" /> and <see cref="CheatEngineHostEffect.NotStarted" />
///         before dispatch, without a Cheat Engine call. Every other type goes through <see cref="TryRead{T}" /> and
///         <see cref="TryWrite{T}" />, whose request carries the <see cref="IMemoryCodec{T}" /> to use.
///     </para>
///     <para>
///         Batch writes are sequential and never imply a transaction or a rollback;
///         <see cref="WritePrimitiveBatchDetailed{T}" /> reports how far they got.
///     </para>
///     <para>
///         <b>Exceptions.</b> Every member checks its arguments, then the activation, before any Cheat Engine call:
///         a <see langword="default" /> or tampered request throws an <see cref="ArgumentException" />, an ended
///         activation <see cref="CheatEngineActivationExpiredException" /> and a stopping one
///         <see cref="CheatEngineInvalidStateException" />. A <c>Try</c> or <c>Detailed</c> member returns every other
///         failure; the throwing member with the same inputs throws it through
///         <see cref="CheatEngineFailure.Throw(CancellationToken)" />. The cancellation token is observed before the
///         work is dispatched to Cheat Engine's main thread.
///     </para>
/// </remarks>
public interface IMemoryClient
{
	/// <summary>Tries to read one supported primitive (see the remarks) without a codec.</summary>
	/// <typeparam name="T">The primitive type to read: one of the types the interface remarks list.</typeparam>
	/// <param name="address">The target address to read.</param>
	/// <param name="value">The value read on success; otherwise the default value.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the read is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when the value was read.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryReadPrimitive<T>(Address address, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Reads one supported primitive (see the remarks) or throws when it is refused or fails.</summary>
	/// <typeparam name="T">The primitive type to read: one of the types the interface remarks list.</typeparam>
	/// <param name="address">The target address to read.</param>
	/// <param name="cancellationToken">Observed before the read is dispatched to Cheat Engine's main thread.</param>
	/// <returns>The value read.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the read failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The read observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The read failed with any other failure kind.</exception>
	public T ReadPrimitive<T>(Address address, CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Tries to write one supported primitive (see the remarks) without a codec.</summary>
	/// <typeparam name="T">The primitive type to write: one of the types the interface remarks list.</typeparam>
	/// <param name="address">The target address to write.</param>
	/// <param name="value">The value to write.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the write is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when the value was written.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryWritePrimitive<T>(Address address, T value, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Writes one supported primitive (see the remarks) or throws when it is refused or fails.</summary>
	/// <typeparam name="T">The primitive type to write: one of the types the interface remarks list.</typeparam>
	/// <param name="address">The target address to write.</param>
	/// <param name="value">The value to write.</param>
	/// <param name="cancellationToken">Observed before the write is dispatched to Cheat Engine's main thread.</param>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the write failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The write observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The write failed with any other failure kind.</exception>
	public void WritePrimitive<T>(Address address, T value, CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Tries to read a bounded homogeneous batch of one supported primitive in one dispatch admission.</summary>
	/// <typeparam name="T">The primitive type to read: one of the types the interface remarks list.</typeparam>
	/// <param name="request">The addresses to read, in order.</param>
	/// <param name="values">The values read, in request order, on success; otherwise an empty array.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the batch is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when every address was read.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no address, or a tampered
	///     one.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request, out ImmutableArray<T> values,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Reads a bounded homogeneous batch of one supported primitive or throws on failure.</summary>
	/// <typeparam name="T">The primitive type to read: one of the types the interface remarks list.</typeparam>
	/// <param name="request">The addresses to read, in order.</param>
	/// <param name="cancellationToken">Observed before the batch is dispatched to Cheat Engine's main thread.</param>
	/// <returns>The values read, in request order.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no address, or a tampered
	///     one.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the batch failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The batch observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The batch failed with any other failure kind.</exception>
	public ImmutableArray<T> ReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>
	///     Reads a bounded homogeneous batch of one supported primitive and returns its completed immutable value prefix
	///     and failure details.
	/// </summary>
	/// <typeparam name="T">The primitive type to read: one of the types the interface remarks list.</typeparam>
	/// <param name="request">The addresses to read, in order.</param>
	/// <param name="cancellationToken">Observed before the batch is dispatched to Cheat Engine's main thread.</param>
	/// <returns>
	///     The outcome: the values read before the first failed read, in request order, and the failure when the batch
	///     did not complete.
	/// </returns>
	/// <remarks>Like <see cref="TryReadPrimitiveBatch{T}" />, it returns every failure instead of throwing.</remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no address, or a tampered
	///     one.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public MemoryPrimitiveBatchReadOutcome<T> ReadPrimitiveBatchDetailed<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Tries to write a bounded homogeneous batch of one supported primitive in one dispatch admission.</summary>
	/// <typeparam name="T">The primitive type to write: one of the types the interface remarks list.</typeparam>
	/// <param name="request">The addresses and values to write, in order.</param>
	/// <param name="failure">
	///     The classified failure; the default value on success. The writes that completed before a failure stay in
	///     place; <see cref="WritePrimitiveBatchDetailed{T}" /> reports how many.
	/// </param>
	/// <param name="cancellationToken">Observed before the batch is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when every value was written.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no value, or a tampered
	///     one.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryWritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Writes a bounded homogeneous batch of one supported primitive or throws on failure.</summary>
	/// <typeparam name="T">The primitive type to write: one of the types the interface remarks list.</typeparam>
	/// <param name="request">The addresses and values to write, in order.</param>
	/// <param name="cancellationToken">Observed before the batch is dispatched to Cheat Engine's main thread.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no value, or a tampered
	///     one.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the batch failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The batch observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The batch failed with any other failure kind; the writes that completed before the failure stay in place.
	/// </exception>
	public void WritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>
	///     Writes a bounded homogeneous batch of one supported primitive and returns its completed count and observable
	///     effect state.
	/// </summary>
	/// <typeparam name="T">The primitive type to write: one of the types the interface remarks list.</typeparam>
	/// <param name="request">The addresses and values to write, in order.</param>
	/// <param name="cancellationToken">Observed before the batch is dispatched to Cheat Engine's main thread.</param>
	/// <returns>
	///     The outcome: how many writes completed in order, the effect state, and the failure when the batch did not
	///     complete.
	/// </returns>
	/// <remarks>Like <see cref="TryWritePrimitiveBatch{T}" />, it returns every failure instead of throwing.</remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no value, or a tampered
	///     one.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public MemoryPrimitiveBatchWriteOutcome WritePrimitiveBatchDetailed<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Tries to copy an exact, caller-bounded byte range.</summary>
	/// <param name="request">The first address and the exact number of bytes to copy.</param>
	/// <param name="bytes">
	///     Every requested byte on success; otherwise an empty array, even when Cheat Engine confirmed a prefix
	///     (<see cref="ReadBytesDetailed" /> keeps it).
	/// </param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the read is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when every requested byte was copied.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, whose length is zero, or a tampered
	///     one.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryReadBytes(MemoryBytesReadRequest request, out ImmutableArray<byte> bytes,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Copies an exact byte range or throws on an expected host failure.</summary>
	/// <param name="request">The first address and the exact number of bytes to copy.</param>
	/// <param name="cancellationToken">Observed before the read is dispatched to Cheat Engine's main thread.</param>
	/// <returns>Every requested byte.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, whose length is zero, or a tampered
	///     one.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the read failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The read observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The read failed with any other failure kind, a partial read included.
	/// </exception>
	public ImmutableArray<byte> ReadBytes(MemoryBytesReadRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Copies a caller-bounded byte range and reports the contiguous prefix Cheat Engine confirmed when it returned fewer
	///     bytes than requested.
	/// </summary>
	/// <param name="request">The first address and the exact number of bytes to copy.</param>
	/// <param name="cancellationToken">Observed before the read is dispatched to Cheat Engine's main thread.</param>
	/// <returns>
	///     The outcome: the confirmed prefix, the requested length, and the failure when the read did not complete.
	/// </returns>
	/// <remarks>
	///     Like a <c>Try</c> method, it reports an expected host failure, a budget refusal and a cancellation before dispatch
	///     through <see cref="MemoryBytesReadOutcome.Failure" /> instead of throwing them; <see cref="TryReadBytes" />
	///     reports the same read without its prefix.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, whose length is zero, or a tampered
	///     one.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public MemoryBytesReadOutcome ReadBytesDetailed(MemoryBytesReadRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to write an immutable byte request.</summary>
	/// <param name="request">The first address and the bytes to write.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the write is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when every byte was written.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no byte.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryWriteBytes(MemoryBytesWriteRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Writes bytes or throws on an expected host failure.</summary>
	/// <param name="request">The first address and the bytes to write.</param>
	/// <param name="cancellationToken">Observed before the write is dispatched to Cheat Engine's main thread.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no byte.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the write failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The write observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The write failed with any other failure kind.</exception>
	public void WriteBytes(MemoryBytesWriteRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	///     Tries to read a string bounded by <see cref="MemoryStringReadRequest.MaximumLength" />, which Cheat Engine
	///     receives unchanged as a host-side bound of undocumented unit.
	/// </summary>
	/// <param name="request">The first address, the host-side bound and the target encoding.</param>
	/// <param name="value">The text read on success; otherwise <see langword="null" />.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the read is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when the string was read.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, whose maximum length is zero, or a
	///     tampered one whose encoding is not a defined value.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryReadString(MemoryStringReadRequest request, [NotNullWhen(true)] out string? value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Reads a bounded string or throws on an expected host failure.</summary>
	/// <param name="request">The first address, the host-side bound and the target encoding.</param>
	/// <param name="cancellationToken">Observed before the read is dispatched to Cheat Engine's main thread.</param>
	/// <returns>The text read.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, whose maximum length is zero, or a
	///     tampered one whose encoding is not a defined value.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the read failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The read observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The read failed with any other failure kind.</exception>
	public string ReadString(MemoryStringReadRequest request, CancellationToken cancellationToken = default);

	/// <summary>Tries to write managed text with the explicit <see cref="MemoryStringEncoding" /> of its request.</summary>
	/// <param name="request">The first address, the text, its encoded-length bound and the target encoding.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the write is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when the text was written.</returns>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no text.
	/// </exception>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is a tampered request that its constructor refuses: a bound that is not positive
	///     or an undefined encoding (<see cref="ArgumentOutOfRangeException" />), or a text longer than its bound.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryWriteString(MemoryStringWriteRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Writes managed text or throws on an expected host failure.</summary>
	/// <param name="request">The first address, the text, its encoded-length bound and the target encoding.</param>
	/// <param name="cancellationToken">Observed before the write is dispatched to Cheat Engine's main thread.</param>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no text.
	/// </exception>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is a tampered request that its constructor refuses: a bound that is not positive
	///     or an undefined encoding (<see cref="ArgumentOutOfRangeException" />), or a text longer than its bound.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the write failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The write observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The write failed with any other failure kind.</exception>
	public void WriteString(MemoryStringWriteRequest request, CancellationToken cancellationToken = default);

	/// <summary>Tries to resolve a finite target-aware pointer chain.</summary>
	/// <param name="request">The base address and the offset applied after each pointer read.</param>
	/// <param name="address">The address the last offset designates on success; otherwise the default value.</param>
	/// <param name="failure">
	///     The classified failure, which names the hop of a failed pointer read; the default value on success.
	/// </param>
	/// <param name="cancellationToken">Observed before the chain is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when every pointer of the chain was read.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no offset, or a tampered
	///     one with more than 64 offsets (<see cref="ArgumentOutOfRangeException" />).
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryResolvePointerChain(PointerChainRequest request, out Address address,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Resolves a finite pointer chain or throws on an expected host failure.</summary>
	/// <param name="request">The base address and the offset applied after each pointer read.</param>
	/// <param name="cancellationToken">Observed before the chain is dispatched to Cheat Engine's main thread.</param>
	/// <returns>The address that the last offset designates.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no offset, or a tampered
	///     one with more than 64 offsets (<see cref="ArgumentOutOfRangeException" />).
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the chain failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The chain observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The chain failed with any other failure kind.</exception>
	public Address ResolvePointerChain(PointerChainRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to read one typed value with the codec its request carries.</summary>
	/// <typeparam name="T">The type of the value the codec reads.</typeparam>
	/// <param name="request">The target address and the codec that reads the value.</param>
	/// <param name="value">The value read on success; otherwise the default value.</param>
	/// <param name="failure">
	///     The classified failure: the one the codec returned, or the Client's classification when the codec
	///     returned the <see langword="default" /> one. The default value on success.
	/// </param>
	/// <param name="cancellationToken">Observed before the codec is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when the codec read the value.</returns>
	/// <remarks>An exception the codec throws propagates unchanged, as the same instance.</remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which carries no codec. It is thrown
	///     before the activation check and before any Cheat Engine call.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryRead<T>(MemoryReadRequest<T> request, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Reads one typed value with the codec its request carries, or throws when the operation fails.</summary>
	/// <typeparam name="T">The type of the value the codec reads.</typeparam>
	/// <param name="request">The target address and the codec that reads the value.</param>
	/// <param name="cancellationToken">Observed before the codec is dispatched to Cheat Engine's main thread.</param>
	/// <returns>The value the codec read.</returns>
	/// <remarks>An exception the codec throws propagates unchanged, as the same instance.</remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which carries no codec. It is thrown
	///     before the activation check and before any Cheat Engine call.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the read failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The read observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The read failed with any other failure kind.</exception>
	public T Read<T>(MemoryReadRequest<T> request, CancellationToken cancellationToken = default);

	/// <summary>Tries to write one typed value with the codec its request carries.</summary>
	/// <typeparam name="T">The type of the value the codec writes.</typeparam>
	/// <param name="request">The target address, the value and the codec that writes it.</param>
	/// <param name="failure">
	///     The classified failure: the one the codec returned, or the Client's classification when the codec
	///     returned the <see langword="default" /> one. The default value on success. A codec that wrote several
	///     times before it failed leaves its earlier writes in place.
	/// </param>
	/// <param name="cancellationToken">Observed before the codec is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when the codec wrote the value.</returns>
	/// <remarks>An exception the codec throws propagates unchanged, as the same instance.</remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which carries no codec. It is thrown
	///     before the activation check and before any Cheat Engine call.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryWrite<T>(MemoryWriteRequest<T> request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Writes one typed value with the codec its request carries, or throws when the operation fails.</summary>
	/// <typeparam name="T">The type of the value the codec writes.</typeparam>
	/// <param name="request">The target address, the value and the codec that writes it.</param>
	/// <param name="cancellationToken">Observed before the codec is dispatched to Cheat Engine's main thread.</param>
	/// <remarks>An exception the codec throws propagates unchanged, as the same instance.</remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which carries no codec. It is thrown
	///     before the activation check and before any Cheat Engine call.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the write failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The write observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The write failed with any other failure kind.</exception>
	public void Write<T>(MemoryWriteRequest<T> request, CancellationToken cancellationToken = default);
}
