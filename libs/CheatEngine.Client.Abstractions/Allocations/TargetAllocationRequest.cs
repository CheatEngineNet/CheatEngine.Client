namespace CheatEngine.Client.Allocations;

/// <summary>Describes one bounded allocation requested in the currently selected target.</summary>
public readonly record struct TargetAllocationRequest
{
	/// <summary>Creates a target allocation request.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="size" /> is not positive.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="access" /> is not a defined value.</exception>
	public TargetAllocationRequest(long size, TargetAllocationAccess access = TargetAllocationAccess.ReadWrite)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
		if (!Enum.IsDefined(access))
		{
			throw new ArgumentOutOfRangeException(nameof(access));
		}

		Size = size;
		Access = access;
	}

	/// <summary>Gets the exact positive byte count requested from the target process.</summary>
	public long Size
	{
		get;
	}

	/// <summary>Gets the requested target-memory access.</summary>
	public TargetAllocationAccess Access
	{
		get;
	}
}
