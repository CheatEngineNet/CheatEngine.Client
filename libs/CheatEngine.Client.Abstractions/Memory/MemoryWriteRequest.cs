using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>An immutable typed target-memory write request.</summary>
/// <typeparam name="T">The type of the value the codec writes.</typeparam>
/// <remarks>
///     A <see langword="default" /> request carries no codec: <see cref="IMemoryClient" /> throws an
///     <see cref="ArgumentException" /> for it before the activation check and before any Cheat Engine call.
/// </remarks>
public readonly record struct MemoryWriteRequest<T>
{
	/// <summary>Creates a memory write request.</summary>
	/// <param name="address">The target address to write.</param>
	/// <param name="value">The value to write.</param>
	/// <param name="codec">The codec that writes the value.</param>
	/// <exception cref="ArgumentNullException"><paramref name="codec" /> is <see langword="null" />.</exception>
	public MemoryWriteRequest(Address address, T value, IMemoryCodec<T> codec)
	{
		ArgumentNullException.ThrowIfNull(codec);
		Address = address;
		Value = value;
		Codec = codec;
	}

	/// <summary>Gets the target address to write.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the value to write.</summary>
	public T Value
	{
		get;
	}

	/// <summary>Gets the codec used to translate the value.</summary>
	public IMemoryCodec<T> Codec
	{
		get;
	}
}
