using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Hashing;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;

namespace CheatEngine.Client.Core.Domains.Hashing;

/// <summary>Preserves the separate file and target-memory hash contracts until their live-host gates are complete.</summary>
internal sealed class UnavailableHashingClient : IHashingClient
{
	private readonly CoreLifetime? _lifetime;

	internal UnavailableHashingClient(CoreLifetime? lifetime = null)
	{
		_lifetime = lifetime;
	}

	public bool TryHashMemory(MemoryHashRequest request, out HashDigest digest, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		digest = default;
		failure = CreateFailure("Hashing.HashMemory", cancellationToken);
		return false;
	}

	public HashDigest HashMemory(MemoryHashRequest request, CancellationToken cancellationToken = default)
	{
		_ = TryHashMemory(request, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<HashDigest>(failure);
	}

	public bool TryHashFile(FileHashRequest request, out HashDigest digest, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		digest = default;
		failure = CreateFailure("Hashing.HashFile", cancellationToken);
		return false;
	}

	public HashDigest HashFile(FileHashRequest request, CancellationToken cancellationToken = default)
	{
		_ = TryHashFile(request, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<HashDigest>(failure);
	}

	private CheatEngineFailure CreateFailure(string operation, CancellationToken cancellationToken)
	{
		return UnavailableCapabilityFailure.Create(_lifetime, ClientCapabilityId.Hashing,
			"Target-memory and file hashing", operation,
			cancellationToken);
	}
}
