using System.Collections.Immutable;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Assembly;

/// <summary>Disassembles and assembles target instructions.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         Auto Assembler patches are not part of this client: they are applied through
///         <see cref="IAutoAssemblerClient" />, which the activation registers only when it opts in.
///     </para>
/// </remarks>
public interface IAssemblyClient
{
	/// <summary>Tries to copy one instruction from the target.</summary>
	public bool TryDisassemble(Address address, out AssemblyInstructionSnapshot instruction,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Copies one target instruction or throws when disassembly fails.</summary>
	public AssemblyInstructionSnapshot Disassemble(Address address, CancellationToken cancellationToken = default);

	/// <summary>Tries to get the exact size of the instruction at an address.</summary>
	public bool TryGetInstructionSize(Address address, out int size, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets the instruction size or throws when Cheat Engine cannot decode it.</summary>
	public int GetInstructionSize(Address address, CancellationToken cancellationToken = default);

	/// <summary>Tries to resolve the start address of the preceding target instruction.</summary>
	public bool TryGetPreviousInstruction(Address address, out Address previousAddress,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Gets the preceding instruction address or throws when it cannot be resolved.</summary>
	public Address GetPreviousInstruction(Address address, CancellationToken cancellationToken = default);

	/// <summary>Tries to assemble exactly one instruction into copied bytes.</summary>
	public bool TryAssemble(AssemblyInstructionRequest request, out ImmutableArray<byte> bytes,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Assembles exactly one instruction into copied bytes or throws when assembly fails.</summary>
	public ImmutableArray<byte> Assemble(AssemblyInstructionRequest request,
		CancellationToken cancellationToken = default);
}
