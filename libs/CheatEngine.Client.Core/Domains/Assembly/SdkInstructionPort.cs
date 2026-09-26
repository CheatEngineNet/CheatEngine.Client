using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Client.Core.Domains.Assembly;

/// <summary>
///     Production instruction port: CheatEngine.SDK 2.0.0 <c>InstructionProfiles</c>, <c>InstructionAssembler</c>,
///     <c>InstructionDisassembler</c> and <c>InstructionNavigator</c>, plus the counted <c>TargetMemory.TryReadBytes</c>,
///     behind the Client's Lua admission.
/// </summary>
/// <remarks>
///     <para>
///         This type is the only Client code that calls CheatEngine.SDK's instruction APIs. It never calls the SDK's
///         two-argument assembler overload, so the encoding preference and the range-check option always reach Cheat
///         Engine, and it never reads the disassembler's byte column.
///     </para>
///     <para>
///         No hosted test has a Cheat Engine process or Lua state, so its lines are excluded from the coverage metric;
///         <c>TryContractTests</c> proves that a detached runtime is refused before any Cheat Engine call.
///     </para>
/// </remarks>
internal sealed class SdkInstructionPort : IInstructionPort
{
	private SdkInstructionPort()
	{
	}

	/// <summary>Gets the stateless production port.</summary>
	internal static SdkInstructionPort Instance
	{
		get;
	} = new();

	public bool TryRunAdmitted(string operation, Action work, out CheatEngineFailure admissionFailure)
	{
		if (!LuaAdmission.TryAcquire(operation, out LuaRuntimeOperation admitted, out admissionFailure))
		{
			return false;
		}

		using LuaRuntimeOperation admission = admitted;
		work();
		return true;
	}

	public InstructionOperationStatus ObserveProfile(out InstructionProfileObservation profile)
	{
		InstructionOperationStatus status = InstructionProfiles.TryObserveCurrent(out InstructionTargetProfile observed);
		profile = status == InstructionOperationStatus.Success
			? new InstructionProfileObservation(observed, observed.Target, observed.Profile.Architecture,
				observed.Profile.AddressWidth)
			: default;
		return status;
	}

	public InstructionOperationStatus Assemble(InstructionProfileObservation profile, string instruction,
		Address address, AssemblePreference preference, bool skipRangeCheck, Span<byte> destination, out int written,
		out int requiredLength)
	{
		InstructionOperationStatus status = InstructionAssembler.TryAssemble(profile.Sdk, instruction, address,
			preference, skipRangeCheck, destination, out InstructionAssembly assembly);
		written = assembly.Written;
		requiredLength = assembly.RequiredLength;
		return status;
	}

	public InstructionOperationStatus Disassemble(InstructionProfileObservation profile, Address address,
		int maximumUtf8Bytes, out InstructionDisassembly disassembly)
	{
		return InstructionDisassembler.TryDisassemble(profile.Sdk, address, maximumUtf8Bytes, out disassembly, out _);
	}

	public InstructionOperationStatus GetLength(InstructionProfileObservation profile, Address address,
		out int length)
	{
		return InstructionNavigator.TryGetLength(profile.Sdk, address, out length);
	}

	public InstructionOperationStatus GetPrevious(InstructionProfileObservation profile, Address address,
		out Address previous)
	{
		return InstructionNavigator.TryGetPrevious(profile.Sdk, address, out previous);
	}

	public bool TryReadBytes(Address address, Span<byte> destination, out int written,
		out MemoryAccessFailure failure)
	{
		return TargetMemory.TryReadBytes(address, destination, out written, out failure);
	}
}
