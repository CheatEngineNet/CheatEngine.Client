using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Assembly;

/// <summary>Assembles, disassembles and measures single target instructions.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         <b>One profile per call.</b> Each call observes the selected target and its instruction profile (x86, x64,
///         ARM32 or ARM64 with its address width) once, in the same dispatched callback as its Cheat Engine calls, and
///         never reselects or reconfigures anything. An address wider than the observed profile is refused with
///         <see cref="CheatEngineFailureKind.OperationRejected" /> and <see cref="CheatEngineHostEffect.NotStarted" />
///         before any instruction function of Cheat Engine is called. A step refused after an earlier instruction call
///         of the same Client call returned (the disassembly and its byte read follow the length query) is
///         <see cref="CheatEngineHostEffect.Completed" />, never <see cref="CheatEngineHostEffect.NotStarted" />.
///         CheatEngine.SDK checks the selected process again before and after every Cheat Engine call; a target that
///         changed meanwhile is <see cref="CheatEngineFailureKind.TargetChanged" />, and nothing is returned. That
///         check is an observation, not a lock.
///     </para>
///     <para>
///         <b>Bounds.</b> Assembled bytes and the bytes of a disassembled instruction are copied up to
///         <c>MemoryResourceLimits.MaximumReadBytes</c>, the disassembler's text up to
///         <c>MemoryResourceLimits.MaximumStringBytes</c> UTF-8 bytes; a larger result is
///         <see cref="CheatEngineFailureKind.ResultLimitExceeded" />. No operation writes target memory.
///     </para>
///     <para>
///         A cancellation token is observed only before the work is dispatched to Cheat Engine's main thread
///         (<see cref="CheatEngineFailureKind.Cancelled" /> with <see cref="CheatEngineHostEffect.NotStarted" />).
///         Auto Assembler patches are not part of this client: they are applied through
///         <see cref="IAutoAssemblerClient" />, which the activation registers only when it opts in.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.Instructions, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public interface IAssemblyClient
{
	/// <summary>Tries to assemble exactly one instruction into copied bytes.</summary>
	/// <param name="request">The instruction, its origin address, its encoding preference and range-check option.</param>
	/// <param name="bytes">The assembled bytes on success; otherwise the default value.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before dispatch only.</param>
	/// <returns><see langword="true" /> when Cheat Engine assembled the instruction.</returns>
	/// <exception cref="ArgumentException"><paramref name="request" /> is the default value.</exception>
	/// <remarks>
	///     Cheat Engine's rejection of the instruction (an unknown mnemonic or a missing symbol) is
	///     <see cref="CheatEngineFailureKind.OperationRejected" /> with <see cref="CheatEngineHostEffect.NotApplied" />.
	///     An empty result is <see cref="CheatEngineFailureKind.InvalidHostResult" />: one instruction is never zero bytes
	///     long.
	/// </remarks>
	public bool TryAssemble(AssemblyInstructionRequest request, out ImmutableArray<byte> bytes,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Assembles exactly one instruction into copied bytes or throws when assembly fails.</summary>
	/// <param name="request">The instruction, its origin address, its encoding preference and range-check option.</param>
	/// <param name="cancellationToken">Observed before dispatch only.</param>
	/// <returns>The assembled bytes.</returns>
	public ImmutableArray<byte> Assemble(AssemblyInstructionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to disassemble the instruction at an address, with its bytes read from target memory.</summary>
	/// <param name="address">The address of the instruction.</param>
	/// <param name="instruction">The copied instruction on success; otherwise the default value.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before dispatch only.</param>
	/// <returns><see langword="true" /> when the instruction, its length and its bytes were copied.</returns>
	public bool TryDisassemble(Address address, out AssemblyInstructionSnapshot instruction,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Disassembles the instruction at an address or throws when disassembly fails.</summary>
	/// <param name="address">The address of the instruction.</param>
	/// <param name="cancellationToken">Observed before dispatch only.</param>
	/// <returns>The copied instruction.</returns>
	public AssemblyInstructionSnapshot Disassemble(Address address, CancellationToken cancellationToken = default);

	/// <summary>Tries to get the exact length of the instruction at an address.</summary>
	/// <param name="address">The address of the instruction.</param>
	/// <param name="length">The positive length on success; otherwise zero.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before dispatch only.</param>
	/// <returns><see langword="true" /> when Cheat Engine reported a positive length.</returns>
	public bool TryGetInstructionLength(Address address, out int length, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets the exact length of the instruction at an address or throws when Cheat Engine cannot decode it.</summary>
	/// <param name="address">The address of the instruction.</param>
	/// <param name="cancellationToken">Observed before dispatch only.</param>
	/// <returns>The positive instruction length.</returns>
	public int GetInstructionLength(Address address, CancellationToken cancellationToken = default);

	/// <summary>Tries to get Cheat Engine's estimate of the start address of the preceding instruction.</summary>
	/// <param name="address">The address of the instruction that follows the one sought.</param>
	/// <param name="previousAddress">The estimated start address on success; otherwise the default value.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before dispatch only.</param>
	/// <returns><see langword="true" /> when Cheat Engine returned an address within the target's address width.</returns>
	/// <remarks>
	///     Variable-length instructions cannot in general be decoded backwards: the result is Cheat Engine's estimate,
	///     not a proof. An estimate wider than the target's address width is
	///     <see cref="CheatEngineFailureKind.OperationRejected" /> with <see cref="CheatEngineHostEffect.Completed" />.
	/// </remarks>
	public bool TryGetPreviousInstructionAddress(Address address, out Address previousAddress,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Gets Cheat Engine's estimate of the preceding instruction address or throws when it has none.</summary>
	/// <param name="address">The address of the instruction that follows the one sought.</param>
	/// <param name="cancellationToken">Observed before dispatch only.</param>
	/// <returns>The estimated start address of the preceding instruction.</returns>
	public Address GetPreviousInstructionAddress(Address address, CancellationToken cancellationToken = default);
}
