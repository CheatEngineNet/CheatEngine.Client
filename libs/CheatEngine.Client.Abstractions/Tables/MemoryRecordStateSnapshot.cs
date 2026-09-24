using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Tables;

/// <summary>Copied runtime state fields of a Cheat Engine memory record.</summary>
public readonly record struct MemoryRecordStateSnapshot
{
	/// <summary>Creates copied runtime state fields for a memory-record snapshot.</summary>
	/// <param name="currentAddress">The currently resolved target address, or <see langword="null" />.</param>
	/// <param name="isActive">Whether Cheat Engine reports the record as active or frozen.</param>
	/// <param name="childCount">The number of immediate child records.</param>
	/// <param name="isAsync">Whether activating the record runs asynchronously.</param>
	/// <param name="isAsyncProcessing">Whether an asynchronous activation of the record is still being processed.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="childCount" /> is negative.</exception>
	public MemoryRecordStateSnapshot(Address? currentAddress, bool isActive = false, int childCount = 0,
		bool isAsync = false, bool isAsyncProcessing = false)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(childCount);

		CurrentAddress = currentAddress;
		IsActive = isActive;
		ChildCount = childCount;
		IsAsync = isAsync;
		IsAsyncProcessing = isAsyncProcessing;
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

	/// <summary>Gets whether activating this record runs asynchronously (Cheat Engine's <c>Async</c> property).</summary>
	public bool IsAsync
	{
		get;
	}

	/// <summary>
	///     Gets whether an asynchronous activation of this record was still being processed when the snapshot was copied
	///     (Cheat Engine's <c>AsyncProcessing</c> property): <see cref="IsActive" /> is then not yet its final state.
	/// </summary>
	public bool IsAsyncProcessing
	{
		get;
	}
}
