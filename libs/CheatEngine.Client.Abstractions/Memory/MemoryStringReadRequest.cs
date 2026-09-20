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
}
