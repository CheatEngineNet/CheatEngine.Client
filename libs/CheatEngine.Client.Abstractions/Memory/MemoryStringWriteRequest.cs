using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>A copied string to write to target memory, with an explicit target encoding and encoded-length bound.</summary>
/// <remarks>Create a request with <see cref="CreateBounded" />.</remarks>
public readonly record struct MemoryStringWriteRequest
{
	private MemoryStringWriteRequest(Address address, string value, int maximumLength, MemoryStringEncoding encoding)
	{
		ArgumentNullException.ThrowIfNull(value);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
		if (!Enum.IsDefined(encoding))
		{
			throw new ArgumentOutOfRangeException(nameof(encoding));
		}

		if (GetEncodedLength(value, encoding) > maximumLength)
		{
			throw new ArgumentException("The encoded text exceeds the explicit maximum length.", nameof(value));
		}

		Address = address;
		Value = value;
		MaximumLength = maximumLength;
		Encoding = encoding;
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

	/// <summary>Gets the positive maximum number of UTF-8 bytes or UTF-16 code units the encoded text may use.</summary>
	public int MaximumLength
	{
		get;
	}

	/// <summary>Gets the explicit UTF-8 or UTF-16 target representation.</summary>
	public MemoryStringEncoding Encoding
	{
		get;
	}

	/// <summary>Creates a text write with an explicit maximum encoded length and target encoding.</summary>
	/// <param name="address">The first target address.</param>
	/// <param name="value">The managed text to copy.</param>
	/// <param name="maximumLength">The positive maximum number of UTF-8 bytes or UTF-16 code units accepted.</param>
	/// <param name="encoding">The UTF-8 or UTF-16 target representation.</param>
	/// <returns>A request whose encoded text is bounded before it reaches Cheat Engine.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="maximumLength" /> is zero or negative, or <paramref name="encoding" /> is not defined.
	/// </exception>
	/// <exception cref="ArgumentException">The encoded text exceeds <paramref name="maximumLength" />.</exception>
	public static MemoryStringWriteRequest CreateBounded(Address address, string value, int maximumLength,
		MemoryStringEncoding encoding)
	{
		return new MemoryStringWriteRequest(address, value, maximumLength, encoding);
	}

	private static int GetEncodedLength(string value, MemoryStringEncoding encoding)
	{
		return encoding == MemoryStringEncoding.Utf16 ? value.Length : System.Text.Encoding.UTF8.GetByteCount(value);
	}
}
