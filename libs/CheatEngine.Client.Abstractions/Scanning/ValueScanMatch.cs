using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>One copied value-scan result: an address and the value text Cheat Engine displays for it.</summary>
/// <remarks>
///     The match owns no Cheat Engine object and stays valid after its session is released. <see cref="ValueText" /> is
///     Cheat Engine's text, formatted by Cheat Engine for the scanned type: to read the typed value, read the address
///     again, for example with <c>IMemoryClient.ReadPrimitive&lt;int&gt;(match.Address)</c>. The address and the text
///     are user data: log them only on an explicit opt-in.
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public readonly record struct ValueScanMatch
{
	private readonly string? _valueText;

	/// <summary>Creates a copied value-scan match.</summary>
	/// <param name="address">The target address of the match.</param>
	/// <param name="valueText">The value text Cheat Engine returned for the match.</param>
	/// <exception cref="ArgumentNullException"><paramref name="valueText" /> is <see langword="null" />.</exception>
	public ValueScanMatch(Address address, string valueText)
	{
		ArgumentNullException.ThrowIfNull(valueText);
		Address = address;
		_valueText = valueText;
	}

	/// <summary>Gets the target address of the match.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the value text Cheat Engine returned for the match, verbatim.</summary>
	/// <remarks><see cref="string.Empty" /> for the <see langword="default" /> value.</remarks>
	public string ValueText => _valueText ?? string.Empty;
}
