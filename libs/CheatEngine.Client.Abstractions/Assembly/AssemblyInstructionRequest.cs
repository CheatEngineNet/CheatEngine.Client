using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Assembly;

/// <summary>Describes a single instruction to assemble at one target address.</summary>
public readonly record struct AssemblyInstructionRequest
{
	/// <summary>Creates an instruction-assembly request.</summary>
	/// <exception cref="ArgumentException"><paramref name="instruction" /> is blank.</exception>
	public AssemblyInstructionRequest(Address address, string instruction)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
		Address = address;
		Instruction = instruction;
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
}
