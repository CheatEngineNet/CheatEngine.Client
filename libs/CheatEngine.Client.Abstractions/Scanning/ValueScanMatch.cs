using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>A copied value-scan match; it owns no FoundList or other CE resource.</summary>
public readonly record struct ValueScanMatch
{
	/// <summary>Creates a copied value-scan match.</summary>
	public ValueScanMatch(int index, Address address, string value)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		ArgumentNullException.ThrowIfNull(value);
		Index = index;
		Address = address;
		Value = value;
	}

	/// <summary>Gets the zero-based index in the CE found list.</summary>
	public int Index
	{
		get;
	}

	/// <summary>Gets the parsed target address.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the verbatim value text returned by Cheat Engine.</summary>
	public string Value
	{
		get;
	}
}
