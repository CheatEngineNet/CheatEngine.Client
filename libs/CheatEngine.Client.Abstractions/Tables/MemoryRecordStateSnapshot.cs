using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Tables;

/// <summary>Copied runtime state fields of a Cheat Engine memory record.</summary>
public readonly record struct MemoryRecordStateSnapshot
{
	/// <summary>Creates copied runtime state fields for a memory-record snapshot.</summary>
	public MemoryRecordStateSnapshot(Address? currentAddress, bool isActive = false, int childCount = 0)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(childCount);

		CurrentAddress = currentAddress;
		IsActive = isActive;
		ChildCount = childCount;
	}

	/// <summary>Gets the currently resolved target address when it could be obtained.</summary>
	public Address? CurrentAddress
	{
		get;
	}

	/// <summary>Gets whether Cheat Engine reports this record as active or frozen.</summary>
	public bool IsActive
	{
		get;
	}

	/// <summary>Gets the number of immediate child records reported by Cheat Engine.</summary>
	public int ChildCount
	{
		get;
	}
}
