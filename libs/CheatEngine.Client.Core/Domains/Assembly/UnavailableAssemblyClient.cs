using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains.Assembly;

/// <summary>Preserves the assembly and patch surface until Auto Assembler ownership passes its live-host gate.</summary>
internal sealed class UnavailableAssemblyClient : IAssemblyClient
{
	public bool TryDisassemble(Address address, out AssemblyInstructionSnapshot instruction,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		instruction = default;
		failure = CreateFailure("Assembly.Disassemble", cancellationToken);
		return false;
	}

	public AssemblyInstructionSnapshot Disassemble(Address address, CancellationToken cancellationToken = default)
	{
		_ = TryDisassemble(address, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<AssemblyInstructionSnapshot>(failure);
	}

	public bool TryGetInstructionSize(Address address, out int size, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		size = default;
		failure = CreateFailure("Assembly.GetInstructionSize", cancellationToken);
		return false;
	}

	public int GetInstructionSize(Address address, CancellationToken cancellationToken = default)
	{
		_ = TryGetInstructionSize(address, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<int>(failure);
	}

	public bool TryGetPreviousInstruction(Address address, out Address previousAddress, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		previousAddress = default;
		failure = CreateFailure("Assembly.GetPreviousInstruction", cancellationToken);
		return false;
	}

	public Address GetPreviousInstruction(Address address, CancellationToken cancellationToken = default)
	{
		_ = TryGetPreviousInstruction(address, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<Address>(failure);
	}

	public bool TryGetComment(Address address, [NotNullWhen(true)] out string? comment, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		comment = null;
		failure = CreateFailure("Assembly.GetComment", cancellationToken);
		return false;
	}

	public string GetComment(Address address, CancellationToken cancellationToken = default)
	{
		_ = TryGetComment(address, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<string>(failure);
	}

	public bool TryAssemble(AssemblyInstructionRequest request, out ImmutableArray<byte> bytes,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		bytes = default;
		failure = CreateFailure("Assembly.Assemble", cancellationToken);
		return false;
	}

	public ImmutableArray<byte> Assemble(AssemblyInstructionRequest request,
		CancellationToken cancellationToken = default)
	{
		_ = TryAssemble(request, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<ImmutableArray<byte>>(failure);
	}

	public bool TryApplyPatch(AutoAssemblerScript script, [NotNullWhen(true)] out IAutoAssemblerPatchLease? lease,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		lease = null;
		failure = CreateFailure("Assembly.ApplyPatch", cancellationToken);
		return false;
	}

	public IAutoAssemblerPatchLease ApplyPatch(AutoAssemblerScript script,
		CancellationToken cancellationToken = default)
	{
		_ = TryApplyPatch(script, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<IAutoAssemblerPatchLease>(failure);
	}

	private static CheatEngineFailure CreateFailure(string operation, CancellationToken cancellationToken)
	{
		return UnavailableCapabilityFailure.Create("Assembly, disassembly, and Auto Assembler patches", operation,
			cancellationToken);
	}
}
