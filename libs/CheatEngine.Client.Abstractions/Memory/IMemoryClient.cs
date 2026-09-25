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
/// </remarks>
public interface IMemoryClient
{
	/// <summary>Tries to read one supported primitive (see the remarks) without a codec.</summary>
	public bool TryReadPrimitive<T>(Address address, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Reads one supported primitive (see the remarks) or throws when it is refused or fails.</summary>
	public T ReadPrimitive<T>(Address address, CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Tries to write one supported primitive (see the remarks) without a codec.</summary>
	public bool TryWritePrimitive<T>(Address address, T value, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Writes one supported primitive (see the remarks) or throws when it is refused or fails.</summary>
	public void WritePrimitive<T>(Address address, T value, CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Tries to read a bounded homogeneous batch of one supported primitive in one dispatch admission.</summary>
	public bool TryReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request, out ImmutableArray<T> values,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Reads a bounded homogeneous batch of one supported primitive or throws on failure.</summary>
	public ImmutableArray<T> ReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>
	///     Reads a bounded homogeneous batch of one supported primitive and returns its completed immutable value prefix
	///     and failure details.
	/// </summary>
	public MemoryPrimitiveBatchReadOutcome<T> ReadPrimitiveBatchDetailed<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Tries to write a bounded homogeneous batch of one supported primitive in one dispatch admission.</summary>
	public bool TryWritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Writes a bounded homogeneous batch of one supported primitive or throws on failure.</summary>
	public void WritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>
	///     Writes a bounded homogeneous batch of one supported primitive and returns its completed count and observable
	///     effect state.
	/// </summary>
	public MemoryPrimitiveBatchWriteOutcome WritePrimitiveBatchDetailed<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged;

	/// <summary>Tries to copy an exact, caller-bounded byte range.</summary>
	public bool TryReadBytes(MemoryBytesReadRequest request, out ImmutableArray<byte> bytes,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Copies an exact byte range or throws on an expected host failure.</summary>
	public ImmutableArray<byte> ReadBytes(MemoryBytesReadRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Copies a caller-bounded byte range and reports the contiguous prefix Cheat Engine confirmed when it returned fewer
	///     bytes than requested.
	/// </summary>
	/// <remarks>
	///     Like a <c>Try</c> method, it reports an expected host failure, a budget refusal and a cancellation before dispatch
	///     through <see cref="MemoryBytesReadOutcome.Failure" /> instead of throwing them; <see cref="TryReadBytes" />
	///     reports the same read without its prefix.
	/// </remarks>
	public MemoryBytesReadOutcome ReadBytesDetailed(MemoryBytesReadRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to write an immutable byte request.</summary>
	public bool TryWriteBytes(MemoryBytesWriteRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Writes bytes or throws on an expected host failure.</summary>
	public void WriteBytes(MemoryBytesWriteRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	///     Tries to read a string bounded by <see cref="MemoryStringReadRequest.MaximumLength" />, which Cheat Engine
	///     receives unchanged as a host-side bound of undocumented unit.
	/// </summary>
	public bool TryReadString(MemoryStringReadRequest request, [NotNullWhen(true)] out string? value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Reads a bounded string or throws on an expected host failure.</summary>
	public string ReadString(MemoryStringReadRequest request, CancellationToken cancellationToken = default);

	/// <summary>Tries to write managed text with the explicit <see cref="MemoryStringEncoding" /> of its request.</summary>
	public bool TryWriteString(MemoryStringWriteRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Writes managed text or throws on an expected host failure.</summary>
	public void WriteString(MemoryStringWriteRequest request, CancellationToken cancellationToken = default);

	/// <summary>Tries to resolve a finite target-aware pointer chain.</summary>
	public bool TryResolvePointerChain(PointerChainRequest request, out Address address,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Resolves a finite pointer chain or throws on an expected host failure.</summary>
	public Address ResolvePointerChain(PointerChainRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to read one typed value with the codec its request carries.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which carries no codec. It is thrown
	///     before the activation check and before any Cheat Engine call.
	/// </exception>
	public bool TryRead<T>(MemoryReadRequest<T> request, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Reads one typed value with the codec its request carries, or throws when the operation fails.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which carries no codec. It is thrown
	///     before the activation check and before any Cheat Engine call.
	/// </exception>
	public T Read<T>(MemoryReadRequest<T> request, CancellationToken cancellationToken = default);

	/// <summary>Tries to write one typed value with the codec its request carries.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which carries no codec. It is thrown
	///     before the activation check and before any Cheat Engine call.
	/// </exception>
	public bool TryWrite<T>(MemoryWriteRequest<T> request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Writes one typed value with the codec its request carries, or throws when the operation fails.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which carries no codec. It is thrown
	///     before the activation check and before any Cheat Engine call.
	/// </exception>
	public void Write<T>(MemoryWriteRequest<T> request, CancellationToken cancellationToken = default);
}
