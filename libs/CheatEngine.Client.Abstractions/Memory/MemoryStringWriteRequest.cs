using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>A copied string to write to target memory.</summary>
public readonly record struct MemoryStringWriteRequest
{
	/// <summary>Creates a text write.</summary>
	public MemoryStringWriteRequest(Address address, string value, bool wideCharacter = false)
	{
		ArgumentNullException.ThrowIfNull(value);
		Address = address;
		Value = value;
		MaximumLength = 0;
		WideCharacter = wideCharacter;
	}

	private MemoryStringWriteRequest(Address address, string value, int maximumLength, bool wideCharacter)
	{
		ArgumentNullException.ThrowIfNull(value);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
		if (GetEncodedLength(value, wideCharacter) > maximumLength)
		{
			throw new ArgumentException("The encoded text exceeds the explicit maximum length.", nameof(value));
		}

		Address = address;
		Value = value;
		MaximumLength = maximumLength;
		WideCharacter = wideCharacter;
	}

	/// <summary>Gets the first target address.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the managed text copied by the request.</summary>
	public string Value
	{
		get;
	}

	/// <summary>Gets the explicit encoded-length bound, or zero for the legacy unbounded constructor.</summary>
	public int MaximumLength
	{
		get;
	}

	/// <summary>Gets whether Cheat Engine should write UTF-16 target text.</summary>
	public bool WideCharacter
	{
		get;
	}

	/// <summary>Gets the explicit UTF-8 or UTF-16 target representation.</summary>
	public MemoryStringEncoding Encoding => WideCharacter ? MemoryStringEncoding.Utf16 : MemoryStringEncoding.Utf8;

	/// <summary>Creates a text write with an explicit maximum encoded length and target encoding.</summary>
	/// <param name="address">The first target address.</param>
	/// <param name="value">The managed text to copy.</param>
	/// <param name="maximumLength">The positive maximum number of UTF-8 bytes or UTF-16 code units accepted.</param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <returns>A request whose encoded text is bounded before it reaches Cheat Engine.</returns>
	public static MemoryStringWriteRequest CreateBounded(Address address, string value, int maximumLength,
		MemoryStringEncoding encoding)
	{
		return new MemoryStringWriteRequest(address, value, maximumLength, ToWideCharacter(encoding));
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

	private static int GetEncodedLength(string value, bool wideCharacter)
	{
		return wideCharacter ? value.Length : System.Text.Encoding.UTF8.GetByteCount(value);
	}
}
