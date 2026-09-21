using CheatEngine.Client.Results;

namespace CheatEngine.Client.Hashing;

/// <summary>Hashes bounded target memory and files through explicitly separate operations.</summary>
public interface IHashingClient
{
	/// <summary>Tries to hash an exact target-memory range.</summary>
	public bool TryHashMemory(MemoryHashRequest request, out HashDigest digest, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Hashes an exact target-memory range or throws when unavailable.</summary>
	public HashDigest HashMemory(MemoryHashRequest request, CancellationToken cancellationToken = default);

	/// <summary>Tries to hash an existing file independently from the selected target.</summary>
	public bool TryHashFile(FileHashRequest request, out HashDigest digest, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Hashes an existing file independently from the selected target.</summary>
	public HashDigest HashFile(FileHashRequest request, CancellationToken cancellationToken = default);
}
