using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>Executes typed target-memory reads and writes without exposing a Lua state.</summary>
/// <remarks>
///     <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add members
///     to it, so implement it only in a test double.
/// </remarks>
public interface IMemoryClient
{
	/// <summary>Tries to read a built-in scalar or pointer type without requiring a custom codec.</summary>
	public bool TryReadPrimitive<T>(Address address, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Reads a built-in scalar or pointer type or throws when unsupported or rejected.</summary>
	public T ReadPrimitive<T>(Address address, CancellationToken cancellationToken = default);

	/// <summary>Tries to write a built-in scalar or pointer type without requiring a custom codec.</summary>
	public bool TryWritePrimitive<T>(Address address, T value, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Writes a built-in scalar or pointer type or throws when unsupported or rejected.</summary>
	public void WritePrimitive<T>(Address address, T value, CancellationToken cancellationToken = default);

	/// <summary>Tries to read a bounded homogeneous batch of built-in scalars or pointers in one dispatch admission.</summary>
	public bool TryReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request, out ImmutableArray<T> values,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Reads a bounded homogeneous batch of built-in scalars or pointers or throws on failure.</summary>
	public ImmutableArray<T> ReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to write a bounded homogeneous batch of built-in scalars or pointers in one dispatch admission.</summary>
	public bool TryWritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Writes a bounded homogeneous batch of built-in scalars or pointers or throws on failure.</summary>
	public void WritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default);

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

	/// <summary>Tries to write managed text with an explicit narrow/wide target representation.</summary>
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

	/// <summary>Tries to read one typed value from target memory.</summary>
	public bool TryRead<T>(MemoryReadRequest<T> request, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Reads one typed value or throws when the operation fails.</summary>
	public T Read<T>(MemoryReadRequest<T> request, CancellationToken cancellationToken = default);

	/// <summary>Tries to write one typed value to target memory.</summary>
	public bool TryWrite<T>(MemoryWriteRequest<T> request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Writes one typed value or throws when the operation fails.</summary>
	public void Write<T>(MemoryWriteRequest<T> request, CancellationToken cancellationToken = default);
}
