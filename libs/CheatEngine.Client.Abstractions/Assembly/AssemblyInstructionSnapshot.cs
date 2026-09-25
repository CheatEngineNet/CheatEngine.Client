using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Assembly;

/// <summary>A copied disassembled instruction and the exact target bytes it was decoded from.</summary>
/// <remarks>
///     <para>
///         <see cref="AddressText" />, <see cref="Opcode" /> and <see cref="Extra" /> are the columns Cheat Engine's
///         disassembler returns, copied as text; they are display text and the Client never parses them.
///         <see cref="Text" /> joins <see cref="Opcode" /> and <see cref="Extra" />.
///     </para>
///     <para>
///         <see cref="Bytes" /> are read from target memory for <see cref="Length" /> bytes, the instruction size Cheat
///         Engine reports; they are never parsed from the disassembler's byte column.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.Instructions, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public readonly record struct AssemblyInstructionSnapshot
{
	private readonly string? _addressText;
	private readonly ImmutableArray<byte> _bytes;
	private readonly string? _extra;
	private readonly string? _opcode;
	private readonly string? _text;

	/// <summary>Creates a copied assembly-instruction snapshot.</summary>
	/// <param name="address">The address of the instruction.</param>
	/// <param name="length">The exact positive instruction length.</param>
	/// <param name="addressText">The address column of Cheat Engine's disassembler.</param>
	/// <param name="opcode">The mnemonic and operands column of Cheat Engine's disassembler.</param>
	/// <param name="extra">The annotation column of Cheat Engine's disassembler; empty when there is none.</param>
	/// <param name="bytes">The target bytes of the instruction; exactly <paramref name="length" /> bytes.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> is not positive.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="addressText" /> or <paramref name="extra" /> is null.</exception>
	/// <exception cref="ArgumentException">
	///     <paramref name="opcode" /> is blank, or <paramref name="bytes" /> does not hold exactly <paramref name="length" />
	///     bytes.
	/// </exception>
	public AssemblyInstructionSnapshot(Address address, int length, string addressText, string opcode, string extra,
		ReadOnlySpan<byte> bytes)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
		ArgumentNullException.ThrowIfNull(addressText);
		ArgumentException.ThrowIfNullOrWhiteSpace(opcode);
		ArgumentNullException.ThrowIfNull(extra);
		if (bytes.Length != length)
		{
			throw new ArgumentException("The byte count must match the instruction length.", nameof(bytes));
		}

		Address = address;
		Length = length;
		_addressText = addressText;
		_opcode = opcode;
		_extra = extra;
		_text = string.IsNullOrWhiteSpace(extra) ? opcode : opcode + " " + extra;
		_bytes = ImmutableArray.Create(bytes);
	}

	/// <summary>Gets the address of the instruction.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the exact positive instruction length.</summary>
	public int Length
	{
		get;
	}

	/// <summary>Gets the copied address column of Cheat Engine's disassembler.</summary>
	/// <remarks><see cref="string.Empty" /> for the <see langword="default" /> value.</remarks>
	public string AddressText => _addressText ?? string.Empty;

	/// <summary>Gets the copied mnemonic and operands column of Cheat Engine's disassembler.</summary>
	/// <remarks><see cref="string.Empty" /> for the <see langword="default" /> value.</remarks>
	public string Opcode => _opcode ?? string.Empty;

	/// <summary>Gets the copied annotation column of Cheat Engine's disassembler; empty when there is none.</summary>
	/// <remarks><see cref="string.Empty" /> for the <see langword="default" /> value.</remarks>
	public string Extra => _extra ?? string.Empty;

	/// <summary>
	///     Gets the instruction as one line: <see cref="Opcode" />, followed by a space and <see cref="Extra" /> when
	///     <see cref="Extra" /> is not blank.
	/// </summary>
	/// <remarks><see cref="string.Empty" /> for the <see langword="default" /> value.</remarks>
	public string Text => _text ?? string.Empty;

	/// <summary>Gets the immutable copy of the target instruction bytes, read from target memory.</summary>
	/// <remarks>Empty for the <see langword="default" /> value, never a default array.</remarks>
	public ImmutableArray<byte> Bytes => _bytes.IsDefault ? ImmutableArray<byte>.Empty : _bytes;
}
