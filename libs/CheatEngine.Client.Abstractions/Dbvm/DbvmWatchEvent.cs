using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Dbvm;

/// <summary>Contains a copied DBVM watch observation.</summary>
public readonly record struct DbvmWatchEvent
{
	/// <summary>Creates a copied DBVM watch observation.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> is not positive.</exception>
	public DbvmWatchEvent(Address address, int length, DateTimeOffset occurredAt)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
		Address = address;
		Length = length;
		OccurredAt = occurredAt;
	}

	/// <summary>Gets the copied target address that triggered the watch.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the copied positive byte count associated with the event.</summary>
	public int Length
	{
		get;
	}

	/// <summary>Gets the copied observation timestamp.</summary>
	public DateTimeOffset OccurredAt
	{
		get;
	}
}
