using System.Collections.Immutable;

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains.Assembly;

/// <summary>Preserves the instruction assembly surface until its adapter passes its live-host gate.</summary>
internal sealed class UnavailableAssemblyClient : IAssemblyClient
{
	private readonly CoreLifetime? _lifetime;

	internal UnavailableAssemblyClient(CoreLifetime? lifetime = null)
	{
		_lifetime = lifetime;
	}

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
		return UnavailableCapabilityFailure.Throw<AssemblyInstructionSnapshot>(failure, cancellationToken);
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
		return UnavailableCapabilityFailure.Throw<int>(failure, cancellationToken);
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
		return UnavailableCapabilityFailure.Throw<Address>(failure, cancellationToken);
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
		return UnavailableCapabilityFailure.Throw<ImmutableArray<byte>>(failure, cancellationToken);
	}

	private CheatEngineFailure CreateFailure(string operation, CancellationToken cancellationToken)
	{
		return UnavailableCapabilityFailure.Create(_lifetime, ClientCapabilityId.Assembly,
			"Assembly and disassembly",
			operation,
			cancellationToken);
	}
}
