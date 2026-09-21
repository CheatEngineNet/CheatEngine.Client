using CheatEngine.Client.Events;

namespace CheatEngine.Client.Dbvm;

/// <summary>Owns one Client DBVM watch and its bounded copied event stream.</summary>
public interface IDbvmWatchLease : IEventStreamLease<DbvmWatchEvent>
{
	/// <summary>Gets the request used to create this watch.</summary>
	public DbvmWatchRequest Request
	{
		get;
	}

	/// <summary>Gets the target-selection epoch captured when the watch was registered.</summary>
	public long SelectionEpoch
	{
		get;
	}
}
