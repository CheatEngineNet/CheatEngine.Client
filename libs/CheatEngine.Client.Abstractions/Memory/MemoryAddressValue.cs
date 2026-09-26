using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>Associates one copied managed value with its target address.</summary>
/// <typeparam name="T">
///     The homogeneous primitive type written by a batch, one of the types <see cref="IMemoryClient" /> supports.
/// </typeparam>
public readonly struct MemoryAddressValue<T>
	where T : unmanaged
{
	/// <summary>Creates one copied address/value pair.</summary>
	/// <param name="address">The target address.</param>
	/// <param name="value">The managed value to write.</param>
	public MemoryAddressValue(Address address, T value)
	{
		Address = address;
		Value = value;
	}

	/// <summary>Gets the target address.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the managed value to write.</summary>
	public T Value
	{
		get;
	}
}
