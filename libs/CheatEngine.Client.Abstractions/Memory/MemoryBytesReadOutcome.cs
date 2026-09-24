using System.Collections.Immutable;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Memory;

/// <summary>
///     Describes one bounded byte read, including the contiguous prefix that Cheat Engine confirmed when the read did not
///     complete.
/// </summary>
/// <remarks>
///     <para>
///         CheatEngine.SDK verifies every byte it copies. When Cheat Engine returns fewer bytes than requested, the read
///         fails with <see cref="CheatEngineFailureKind.MemoryReadFailed" /> and <see cref="Bytes" /> holds the confirmed
///         prefix, so a partial copy is never confused with a host failure that copied nothing. A failure observed before
///         Cheat Engine returned anything (a budget, a cancellation, an unavailable global) has an empty prefix.
///     </para>
///     <para>
///         The prefix is a copy owned by the caller; it never aliases Cheat Engine or Lua memory.
///     </para>
/// </remarks>
public sealed class MemoryBytesReadOutcome
{
	/// <summary>Creates a byte-read outcome.</summary>
	/// <param name="requestedLength">The positive number of bytes the request asked for.</param>
	/// <param name="bytes">
	///     The confirmed contiguous prefix: every requested byte when <paramref name="failure" /> is
	///     <see langword="null" />, otherwise at most <paramref name="requestedLength" /> minus one bytes. A default array
	///     is an empty prefix.
	/// </param>
	/// <param name="failure">The failure, or <see langword="null" /> when every requested byte was copied.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="requestedLength" /> is zero or negative, or <paramref name="bytes" /> is longer than it.
	/// </exception>
	/// <exception cref="ArgumentException">
	///     A successful outcome is incomplete, or a failed outcome holds every requested byte.
	/// </exception>
	public MemoryBytesReadOutcome(int requestedLength, ImmutableArray<byte> bytes, CheatEngineFailure? failure)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedLength);
		ImmutableArray<byte> prefix = bytes.IsDefault ? [] : bytes;
		ArgumentOutOfRangeException.ThrowIfGreaterThan(prefix.Length, requestedLength, nameof(bytes));
		if (failure is null && prefix.Length != requestedLength)
		{
			throw new ArgumentException("An incomplete byte read requires a failure.", nameof(failure));
		}

		if (failure is not null && prefix.Length == requestedLength)
		{
			throw new ArgumentException("A complete byte read cannot contain a failure.", nameof(failure));
		}

		RequestedLength = requestedLength;
		Bytes = prefix;
		Failure = failure;
	}

	/// <summary>Gets the confirmed contiguous prefix: every requested byte when the read succeeded.</summary>
	public ImmutableArray<byte> Bytes
	{
		get;
	}

	/// <summary>Gets the number of bytes the request asked for.</summary>
	public int RequestedLength
	{
		get;
	}

	/// <summary>Gets the number of bytes Cheat Engine confirmed, the length of <see cref="Bytes" />.</summary>
	public int ConfirmedLength => Bytes.Length;

	/// <summary>Gets whether every requested byte was confirmed.</summary>
	public bool IsComplete => ConfirmedLength == RequestedLength;

	/// <summary>Gets the failure when the read did not complete, or <see langword="null" />.</summary>
	public CheatEngineFailure? Failure
	{
		get;
	}

	/// <summary>Gets whether the read succeeded: no failure, and every requested byte confirmed.</summary>
	public bool IsSuccess => Failure is null;
}
