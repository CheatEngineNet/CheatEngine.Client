using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains.Assembly;

/// <summary>The instruction profile CheatEngine.SDK observed for one Client call, with the facts Core reads from it.</summary>
/// <param name="Sdk">The SDK profile that every instruction call of the same Client call receives.</param>
/// <param name="Target">The selected process identifier observed with the profile; not an incarnation.</param>
/// <param name="Architecture">The observed instruction architecture.</param>
/// <param name="AddressWidth">The observed instruction address width.</param>
/// <remarks>
///     Only the production port fills <paramref name="Sdk" />: CheatEngine.SDK creates <c>InstructionTargetProfile</c>
///     only through its own observation, so a test port leaves it default and supplies the copied facts.
/// </remarks>
internal readonly record struct InstructionProfileObservation(
	InstructionTargetProfile Sdk,
	TargetProcessId Target,
	CheatEngineArchitecture Architecture,
	PointerSize AddressWidth)
{
	/// <summary>Gets whether <paramref name="address" /> fits the observed address width.</summary>
	/// <param name="address">The address to check.</param>
	/// <returns>
	///     <see langword="false" /> only for an address above 4 GiB on a 32-bit profile, the rule CheatEngine.SDK applies
	///     before it calls Cheat Engine.
	/// </returns>
	internal bool Accepts(Address address)
	{
		return AddressWidth != PointerSize.Bit32 || address.Value <= uint.MaxValue;
	}
}

/// <summary>Internal port for the Cheat Engine calls of the instruction domain.</summary>
/// <remarks>
///     <para>
///         Every Client call runs on Cheat Engine's main thread inside one dispatched callback:
///         <see cref="TryRunAdmitted" /> asks CheatEngine.SDK for one Lua admission and runs the whole call under it, the
///         profile is observed once with <see cref="ObserveProfile" />, then the operation methods receive that same
///         profile. A refused admission is reported as a classified failure (<c>LuaAdmission</c>) without calling Cheat
///         Engine.
///     </para>
///     <para>
///         The operation methods return CheatEngine.SDK's own <see cref="InstructionOperationStatus" />; CheatEngine.SDK
///         checks the selected process before and after each Cheat Engine call. No method selects a process, changes
///         Cheat Engine's assembler mode or writes target memory.
///     </para>
/// </remarks>
internal interface IInstructionPort
{
	/// <summary>Runs <paramref name="work" /> under one CheatEngine.SDK Lua admission.</summary>
	/// <param name="operation">The public Client operation name, for an admission refusal.</param>
	/// <param name="work">The Client work of one call; it runs only when the SDK admitted the operation.</param>
	/// <param name="admissionFailure">The classified admission refusal when not admitted.</param>
	/// <returns><see langword="true" /> when the SDK admitted the operation and <paramref name="work" /> ran.</returns>
	public bool TryRunAdmitted(string operation, Action work, out CheatEngineFailure admissionFailure);

	/// <summary>Observes the selected target and its instruction profile (<c>InstructionProfiles.TryObserveCurrent</c>).</summary>
	/// <param name="profile">The observed profile when the status is <c>Success</c>; otherwise the default value.</param>
	/// <returns>The SDK status of the observation.</returns>
	public InstructionOperationStatus ObserveProfile(out InstructionProfileObservation profile);

	/// <summary>Assembles one instruction into <paramref name="destination" /> (<c>InstructionAssembler.TryAssemble</c>).</summary>
	/// <param name="profile">The profile observed for this call.</param>
	/// <param name="instruction">The instruction source.</param>
	/// <param name="address">The origin address sent to Cheat Engine.</param>
	/// <param name="preference">The jump-encoding preference sent to Cheat Engine.</param>
	/// <param name="skipRangeCheck">The range-check option sent to Cheat Engine.</param>
	/// <param name="destination">The Client-owned buffer.</param>
	/// <param name="written">The number of bytes copied on success; zero otherwise.</param>
	/// <param name="requiredLength">The exact length of Cheat Engine's valid result; zero when it returned none.</param>
	/// <returns>The SDK status of the call.</returns>
	public InstructionOperationStatus Assemble(InstructionProfileObservation profile, string instruction,
		Address address, AssemblePreference preference, bool skipRangeCheck, Span<byte> destination, out int written,
		out int requiredLength);

	/// <summary>Disassembles one instruction (<c>InstructionDisassembler.TryDisassemble</c>).</summary>
	/// <param name="profile">The profile observed for this call.</param>
	/// <param name="address">The address of the instruction.</param>
	/// <param name="maximumUtf8Bytes">The largest raw display line accepted before any text is decoded.</param>
	/// <param name="disassembly">The copied columns on success; otherwise the default value.</param>
	/// <returns>The SDK status of the call.</returns>
	public InstructionOperationStatus Disassemble(InstructionProfileObservation profile, Address address,
		int maximumUtf8Bytes, out InstructionDisassembly disassembly);

	/// <summary>Gets the length of one instruction (<c>InstructionNavigator.TryGetLength</c>).</summary>
	/// <param name="profile">The profile observed for this call.</param>
	/// <param name="address">The address of the instruction.</param>
	/// <param name="length">The positive length on success; otherwise zero.</param>
	/// <returns>The SDK status of the call.</returns>
	public InstructionOperationStatus GetLength(InstructionProfileObservation profile, Address address,
		out int length);

	/// <summary>Gets Cheat Engine's estimated previous instruction (<c>InstructionNavigator.TryGetPrevious</c>).</summary>
	/// <param name="profile">The profile observed for this call.</param>
	/// <param name="address">The address that follows the instruction sought.</param>
	/// <param name="previous">The estimated address on success; otherwise zero.</param>
	/// <returns>
	///     The SDK status of the call; <c>AddressExceedsProfileWidth</c> for an input the Client already accepted is the
	///     returned estimate.
	/// </returns>
	public InstructionOperationStatus GetPrevious(InstructionProfileObservation profile, Address address,
		out Address previous);

	/// <summary>Reads the bytes of one instruction (<c>TargetMemory.TryReadBytes</c> with a copied count).</summary>
	/// <param name="address">The address of the instruction.</param>
	/// <param name="destination">The Client-owned buffer, exactly the instruction length.</param>
	/// <param name="written">The number of bytes CheatEngine.SDK verified and copied.</param>
	/// <param name="failure">The SDK failure when the read did not complete.</param>
	/// <returns><see langword="true" /> only when every byte was copied.</returns>
	public bool TryReadBytes(Address address, Span<byte> destination, out int written,
		out MemoryAccessFailure failure);
}
