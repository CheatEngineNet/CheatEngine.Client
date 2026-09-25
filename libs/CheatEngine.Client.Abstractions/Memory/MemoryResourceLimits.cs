namespace CheatEngine.Client.Memory;

/// <summary>Defines the memory-work budgets captured for one Client activation.</summary>
/// <remarks>
///     <para>
///         Configuration can populate this mutable value before activation. The Core client copies and validates it
///         when it is created, so a later configuration mutation cannot change an active client's admission policy.
///     </para>
///     <para>
///         Byte, string and batch requests are admitted before dispatch: a request over a budget fails with
///         <see cref="Results.CheatEngineFailureKind.OperationRejected" /> and
///         <see cref="Results.CheatEngineHostEffect.NotStarted" />, and Cheat Engine is not called. The reads and writes
///         a custom codec makes through its context are charged cumulatively against the read and write budgets during
///         one codec call; the first one over budget fails that codec call before it reaches Cheat Engine.
///     </para>
///     <para>The Client documentation uses four terms for these limits:</para>
///     <list type="table">
///         <listheader>
///             <term>Term</term>
///             <description>Where it is enforced</description>
///         </listheader>
///         <item>
///             <term>Maximum block size</term>
///             <description>
///                 <see cref="MaximumReadBytes" />, <see cref="MaximumWriteBytes" /> and
///                 <see cref="MaximumStringBytes" />: the largest contiguous block that one byte, codec or string
///                 operation may copy.
///             </description>
///         </item>
///         <item>
///             <term>Request count per batch</term>
///             <description>
///                 <see cref="MaximumBatchOperationCount" />, which can tighten but never raise
///                 <see cref="MemoryBatchLimits.MaximumOperationCount" />.
///             </description>
///         </item>
///         <item>
///             <term>Maximum scratch allocation</term>
///             <description>
///                 The largest managed buffer the Client allocates for one operation: the byte array of a byte read
///                 (at most <see cref="MaximumReadBytes" />) and the value array of a primitive batch read (at most
///                 <see cref="MaximumBatchPayloadBytes" />). These operations allocate no memory in the target process.
///             </description>
///         </item>
///         <item>
///             <term>Partial-effect state</term>
///             <description>
///                 Not a budget: a batch write runs its operations in order and is never rolled back, so its outcome
///                 reports <see cref="MemoryBatchWriteEffectState.NotStarted" />,
///                 <see cref="MemoryBatchWriteEffectState.Partial" /> (with the completed prefix length),
///                 <see cref="MemoryBatchWriteEffectState.Completed" /> or
///                 <see cref="MemoryBatchWriteEffectState.Unknown" />.
///             </description>
///         </item>
///     </list>
/// </remarks>
public sealed class MemoryResourceLimits
{
	/// <summary>Gets the default maximum number of bytes copied by one read.</summary>
	public const int DefaultMaximumReadBytes = 1_048_576;

	/// <summary>Gets the default maximum number of bytes copied by one write.</summary>
	public const int DefaultMaximumWriteBytes = 1_048_576;

	/// <summary>Gets the default maximum encoded byte length of one string operation.</summary>
	public const int DefaultMaximumStringBytes = 65_536;

	/// <summary>Gets the default maximum payload bytes represented by one primitive batch.</summary>
	public const int DefaultMaximumBatchPayloadBytes = 65_536;

	/// <summary>Gets the default maximum number of operations represented by one primitive batch.</summary>
	public const int DefaultMaximumBatchOperationCount = MemoryBatchLimits.MaximumOperationCount;

	/// <summary>Initializes the default activation memory budgets.</summary>
	public MemoryResourceLimits()
	{
	}

	/// <summary>Initializes explicitly bounded activation memory budgets.</summary>
	/// <param name="maximumReadBytes">The positive maximum number of bytes one read may copy.</param>
	/// <param name="maximumWriteBytes">The positive maximum number of bytes one write may copy.</param>
	/// <param name="maximumStringBytes">The positive maximum encoded bytes of one string operation.</param>
	/// <param name="maximumBatchPayloadBytes">The positive maximum payload bytes of one primitive batch.</param>
	/// <param name="maximumBatchOperationCount">
	///     The positive maximum number of operations of one primitive batch, at most
	///     <see cref="MemoryBatchLimits.MaximumOperationCount" />.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     A value is zero or negative, or <paramref name="maximumBatchOperationCount" /> exceeds
	///     <see cref="MemoryBatchLimits.MaximumOperationCount" />.
	/// </exception>
	public MemoryResourceLimits(int maximumReadBytes, int maximumWriteBytes, int maximumStringBytes,
		int maximumBatchPayloadBytes, int maximumBatchOperationCount)
	{
		Validate(maximumReadBytes, nameof(maximumReadBytes));
		Validate(maximumWriteBytes, nameof(maximumWriteBytes));
		Validate(maximumStringBytes, nameof(maximumStringBytes));
		Validate(maximumBatchPayloadBytes, nameof(maximumBatchPayloadBytes));
		ValidateBatchOperationCount(maximumBatchOperationCount, nameof(maximumBatchOperationCount));

		MaximumReadBytes = maximumReadBytes;
		MaximumWriteBytes = maximumWriteBytes;
		MaximumStringBytes = maximumStringBytes;
		MaximumBatchPayloadBytes = maximumBatchPayloadBytes;
		MaximumBatchOperationCount = maximumBatchOperationCount;
	}

	/// <summary>Gets or sets the maximum bytes that one target-memory read may materialize.</summary>
	public int MaximumReadBytes
	{
		get;
		set;
	} = DefaultMaximumReadBytes;

	/// <summary>Gets or sets the maximum bytes that one target-memory write may copy.</summary>
	public int MaximumWriteBytes
	{
		get;
		set;
	} = DefaultMaximumWriteBytes;

	/// <summary>Gets or sets the maximum encoded bytes admitted for one target-string operation.</summary>
	/// <remarks>
	///     A write is charged its encoded byte length. A read is charged conservatively from
	///     <see cref="MemoryStringReadRequest.MaximumLength" />: that value as bytes for UTF-8, twice it for UTF-16,
	///     because Cheat Engine does not document the unit of its <c>readString</c> limit.
	/// </remarks>
	public int MaximumStringBytes
	{
		get;
		set;
	} = DefaultMaximumStringBytes;

	/// <summary>Gets or sets the maximum scalar payload bytes admitted for one primitive batch.</summary>
	/// <remarks>The payload is the operation count multiplied by the size of the primitive element type.</remarks>
	public int MaximumBatchPayloadBytes
	{
		get;
		set;
	} = DefaultMaximumBatchPayloadBytes;

	/// <summary>Gets or sets the maximum primitive operations admitted for one batch.</summary>
	/// <remarks>The value can tighten, but never raise, <see cref="MemoryBatchLimits.MaximumOperationCount" />.</remarks>
	public int MaximumBatchOperationCount
	{
		get;
		set;
	} = DefaultMaximumBatchOperationCount;

	private static void Validate(int value, string parameterName)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);
	}

	private static void ValidateBatchOperationCount(int value, string parameterName)
	{
		Validate(value, parameterName);
		if (value > MemoryBatchLimits.MaximumOperationCount)
		{
			throw new ArgumentOutOfRangeException(parameterName,
				$"A memory batch is limited to {MemoryBatchLimits.MaximumOperationCount} operations.");
		}
	}
}
