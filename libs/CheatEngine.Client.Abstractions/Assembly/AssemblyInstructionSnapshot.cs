using System.Collections.Immutable;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Assembly;

/// <summary>A copied disassembly instruction and its exact target byte representation.</summary>
public readonly record struct AssemblyInstructionSnapshot
{
	/// <summary>Creates a copied assembly-instruction snapshot.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> is not positive.</exception>
	/// <exception cref="ArgumentException"><paramref name="text" /> is blank or <paramref name="bytes" /> is empty.</exception>
	public AssemblyInstructionSnapshot(Address address, int length, string text, ReadOnlySpan<byte> bytes)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
		ArgumentException.ThrowIfNullOrWhiteSpace(text);
		if (bytes.IsEmpty)
		{
			throw new ArgumentException("An instruction snapshot requires at least one byte.", nameof(bytes));
		}

		if (bytes.Length != length)
		{
			throw new ArgumentException("The byte count must match the instruction length.", nameof(bytes));
		}

		Address = address;
		Length = length;
		Text = text;
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

	/// <summary>Gets the copied disassembly text.</summary>
	public string Text
	{
		get;
	}

	/// <summary>Gets the immutable copy of the target instruction bytes.</summary>
	public ImmutableArray<byte> Bytes
	{
		get;
	}
}
