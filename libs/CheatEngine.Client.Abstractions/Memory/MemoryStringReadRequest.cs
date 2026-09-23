using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>A bounded target-string read.</summary>
/// <remarks>
///     <see cref="MaximumLength" /> is passed unchanged as Cheat Engine's <c>readString</c> <c>maxlength</c> argument.
///     Cheat Engine 7.7 does not document whether that argument counts bytes or characters, so treat it as a host-side
///     bound, not as a character or byte count (evidence level: ToQualify, to be confirmed on a C3 host).
/// </remarks>
public readonly record struct MemoryStringReadRequest
{
	/// <summary>Creates a bounded text read.</summary>
	/// <param name="address">The first target address.</param>
	/// <param name="maximumLength">
	///     The positive value passed unchanged as Cheat Engine's <c>readString</c> <c>maxlength</c> argument; see
	///     <see cref="MaximumLength" /> for why its unit is not stated.
	/// </param>
	/// <param name="wideCharacter"><see langword="true" /> to ask Cheat Engine for UTF-16 text; otherwise UTF-8.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumLength" /> is zero or negative.</exception>
	public MemoryStringReadRequest(Address address, int maximumLength, bool wideCharacter = false)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
		Address = address;
		MaximumLength = maximumLength;
		WideCharacter = wideCharacter;
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

	/// <summary>Gets whether Cheat Engine should interpret the target as UTF-16 text.</summary>
	public bool WideCharacter
	{
		get;
	}

	/// <summary>Gets the explicit UTF-8 or UTF-16 target representation.</summary>
	public MemoryStringEncoding Encoding => WideCharacter ? MemoryStringEncoding.Utf16 : MemoryStringEncoding.Utf8;

	/// <summary>Creates a bounded text read with an explicit target encoding.</summary>
	/// <param name="address">The first target address.</param>
	/// <param name="maximumLength">
	///     The positive value passed unchanged as Cheat Engine's <c>readString</c> <c>maxlength</c> argument; see
	///     <see cref="MaximumLength" /> for why its unit is not stated.
	/// </param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <returns>A request that preserves the supplied encoding choice.</returns>
	public static MemoryStringReadRequest Create(Address address, int maximumLength, MemoryStringEncoding encoding)
	{
		return new MemoryStringReadRequest(address, maximumLength, ToWideCharacter(encoding));
	}

	private static bool ToWideCharacter(MemoryStringEncoding encoding)
	{
		return encoding switch
		{
			MemoryStringEncoding.Utf8 => false,
			MemoryStringEncoding.Utf16 => true,
			_ => throw new ArgumentOutOfRangeException(nameof(encoding))
		};
	}
}
