namespace CheatEngine.Client.Memory;

/// <summary>Defines the memory-work budgets captured for one Client activation.</summary>
/// <remarks>
///     Configuration can populate this mutable value before activation. The Core client copies and validates it when it
///     is created, so a later configuration mutation cannot change an active client's admission policy.
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
	public const int DefaultMaximumBatchOperationCount = MemoryBatchLimits.MaximumOperations;

	/// <summary>Initializes the default activation memory budgets.</summary>
	public MemoryResourceLimits()
	{
	}

	/// <summary>Initializes explicitly bounded activation memory budgets.</summary>
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
	public int MaximumStringBytes
	{
		get;
		set;
	} = DefaultMaximumStringBytes;

	/// <summary>Gets or sets the maximum scalar payload bytes admitted for one primitive batch.</summary>
	public int MaximumBatchPayloadBytes
	{
		get;
		set;
	} = DefaultMaximumBatchPayloadBytes;

	/// <summary>Gets or sets the maximum primitive operations admitted for one batch.</summary>
	/// <remarks>The value can tighten, but never raise, <see cref="MemoryBatchLimits.MaximumOperations" />.</remarks>
	public int MaximumBatchOperationCount
	{
		get;
		set;
	} = DefaultMaximumBatchOperationCount;

	/// <summary>Creates an independently validated copy for an activation-bound client.</summary>
	public MemoryResourceLimits CreateSnapshot()
	{
		return new MemoryResourceLimits(MaximumReadBytes, MaximumWriteBytes, MaximumStringBytes,
			MaximumBatchPayloadBytes, MaximumBatchOperationCount);
	}

	private static void Validate(int value, string parameterName)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);
	}

	private static void ValidateBatchOperationCount(int value, string parameterName)
	{
		Validate(value, parameterName);
		if (value > MemoryBatchLimits.MaximumOperations)
		{
			throw new ArgumentOutOfRangeException(parameterName,
				$"A memory batch is limited to {MemoryBatchLimits.MaximumOperations} operations.");
		}
	}
}
