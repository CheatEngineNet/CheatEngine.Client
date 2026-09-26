using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>A bounded target-string read with an explicit target encoding.</summary>
/// <remarks>
///     <see cref="MaximumLength" /> is passed unchanged as Cheat Engine's <c>readString</c> <c>maxlength</c> argument.
///     Cheat Engine 7.7 does not document whether that argument counts bytes or characters, so treat it as a host-side
///     bound, not as a character or byte count (evidence level: ToQualify, to be confirmed on a C3 host).
/// </remarks>
public readonly record struct MemoryStringReadRequest
{
	/// <summary>Creates a bounded text read with an explicit target encoding.</summary>
	/// <param name="address">The first target address.</param>
	/// <param name="maximumLength">
	///     The positive value passed unchanged as Cheat Engine's <c>readString</c> <c>maxlength</c> argument; see
	///     <see cref="MaximumLength" /> for why its unit is not stated.
	/// </param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="maximumLength" /> is zero or negative, or <paramref name="encoding" /> is not defined.
	/// </exception>
	public MemoryStringReadRequest(Address address, int maximumLength, MemoryStringEncoding encoding)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
		if (!Enum.IsDefined(encoding))
		{
			throw new ArgumentOutOfRangeException(nameof(encoding));
		}

		Address = address;
		MaximumLength = maximumLength;
		Encoding = encoding;
	}

	/// <summary>Gets the first target address.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the value passed unchanged as Cheat Engine's <c>readString</c> <c>maxlength</c> argument.</summary>
	/// <remarks>
	///     <para>
	///         Cheat Engine 7.7 does not document whether <c>maxlength</c> counts bytes or characters, so treat this value
	///         as a host-side bound (evidence level: ToQualify, to be confirmed on a C3 host). The Client neither converts
	///         nor scales it before the call.
	///     </para>
	///     <para>
	///         Admission is conservative: before dispatch, the Client charges this value as bytes for UTF-8 and twice this
	///         value as bytes for UTF-16 against <see cref="MemoryResourceLimits.MaximumStringBytes" />.
	///     </para>
	/// </remarks>
	public int MaximumLength
	{
		get;
	}

	/// <summary>Gets the explicit UTF-8 or UTF-16 target representation.</summary>
	public MemoryStringEncoding Encoding
	{
		get;
	}
}
