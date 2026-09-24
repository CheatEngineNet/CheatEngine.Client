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
[Experimental("CECLIENT5003", UrlFormat = "https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Abstractions/README.md#{0}")]
public readonly record struct AssemblyInstructionSnapshot
{
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
		AddressText = addressText;
		Opcode = opcode;
		Extra = extra;
		Text = string.IsNullOrWhiteSpace(extra) ? opcode : opcode + " " + extra;
		Bytes = ImmutableArray.Create(bytes);
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
	public string AddressText
	{
		get;
	}

	/// <summary>Gets the copied mnemonic and operands column of Cheat Engine's disassembler.</summary>
	public string Opcode
	{
		get;
	}

	/// <summary>Gets the copied annotation column of Cheat Engine's disassembler; empty when there is none.</summary>
	public string Extra
	{
		get;
	}

	/// <summary>
	///     Gets the instruction as one line: <see cref="Opcode" />, followed by a space and <see cref="Extra" /> when
	///     <see cref="Extra" /> is not blank.
	/// </summary>
	public string Text
	{
		get;
	}

	/// <summary>Gets the immutable copy of the target instruction bytes, read from target memory.</summary>
	public ImmutableArray<byte> Bytes
	{
		get;
	}
}
