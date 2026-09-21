using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Dbvm;

/// <summary>Describes a bounded DBVM watch over one target-memory range.</summary>
public readonly record struct DbvmWatchRequest
{
	/// <summary>Creates a bounded DBVM watch request.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> is not positive.</exception>
	public DbvmWatchRequest(Address address, int length)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
		Address = address;
		Length = length;
	}

	/// <summary>Gets the first target address covered by the watch.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the exact positive watched byte count.</summary>
	public int Length
	{
		get;
	}
}
