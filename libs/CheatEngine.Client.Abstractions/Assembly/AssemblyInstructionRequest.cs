using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Assembly;

/// <summary>Describes a single instruction to assemble at one target address.</summary>
/// <remarks>
///     The address is the explicit origin Cheat Engine receives: the same text assembles to different bytes for another
///     origin, preference or range-check option, so reuse the bytes only at the address they were assembled for.
/// </remarks>
[Experimental("CECLIENT5003", UrlFormat = "https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Abstractions/README.md#{0}")]
public readonly record struct AssemblyInstructionRequest
{
	/// <summary>Creates an instruction-assembly request.</summary>
	/// <param name="address">The target address used as the origin of relative operands.</param>
	/// <param name="instruction">The source of exactly one instruction, sent to Cheat Engine without normalization.</param>
	/// <param name="preference">The jump and call encoding Cheat Engine should prefer.</param>
	/// <param name="skipRangeCheck">
	///     Whether Cheat Engine skips its check that a relative operand is reachable from <paramref name="address" />.
	///     When <see langword="true" />, Cheat Engine emits bytes even when they cannot encode the intended target, and
	///     the caller owns that risk.
	/// </param>
	/// <exception cref="ArgumentException"><paramref name="instruction" /> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="preference" /> is not a defined <see cref="InstructionEncodingPreference" /> value.
	/// </exception>
	public AssemblyInstructionRequest(Address address, string instruction,
		InstructionEncodingPreference preference = InstructionEncodingPreference.None, bool skipRangeCheck = false)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
		if (preference is < InstructionEncodingPreference.None or > InstructionEncodingPreference.Far)
		{
			throw new ArgumentOutOfRangeException(nameof(preference), preference,
				"The encoding preference must be None, Short, Long or Far.");
		}

		Address = address;
		Instruction = instruction;
		Preference = preference;
		SkipRangeCheck = skipRangeCheck;
	}

	/// <summary>Gets the address used as the instruction's assembly origin.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the assembly source for exactly one instruction.</summary>
	public string Instruction
	{
		get;
	}

	/// <summary>Gets the jump and call encoding Cheat Engine should prefer.</summary>
	public InstructionEncodingPreference Preference
	{
		get;
	}

	/// <summary>Gets whether Cheat Engine skips its reachability check of relative operands.</summary>
	public bool SkipRangeCheck
	{
		get;
	}
}
