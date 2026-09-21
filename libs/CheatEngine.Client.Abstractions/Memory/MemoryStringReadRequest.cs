using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>A bounded target-string read.</summary>
public readonly record struct MemoryStringReadRequest
{
	/// <summary>Creates a bounded text read.</summary>
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

	/// <summary>Gets the maximum character count passed to Cheat Engine.</summary>
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
	/// <param name="maximumLength">The positive maximum length accepted by Cheat Engine.</param>
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
