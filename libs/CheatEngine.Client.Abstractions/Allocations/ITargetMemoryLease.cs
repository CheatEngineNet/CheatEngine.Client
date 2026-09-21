using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Allocations;

/// <summary>Owns one Client-created allocation in the selected target process.</summary>
/// <remarks>
///     The activation owner releases forgotten leases before disable. A selection-epoch change also invalidates and
///     releases an allocation; implementations must not cache or pool target allocations.
/// </remarks>
public interface ITargetMemoryLease : IDisposable
{
	/// <summary>Gets the allocated target address.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the allocated byte count.</summary>
	public long Size
	{
		get;
	}

	/// <summary>Gets the target-selection epoch captured when this allocation was created.</summary>
	public long SelectionEpoch
	{
		get;
	}

	/// <summary>Gets whether the allocation has been released or invalidated.</summary>
	public bool IsReleased
	{
		get;
	}
}
