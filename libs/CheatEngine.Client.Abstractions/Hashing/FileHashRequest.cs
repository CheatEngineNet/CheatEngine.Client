namespace CheatEngine.Client.Hashing;

/// <summary>Describes a file hash request that is intentionally separate from target-memory hashing.</summary>
public readonly record struct FileHashRequest
{
	/// <summary>Creates a file hash request.</summary>
	/// <exception cref="ArgumentException"><paramref name="filePath" /> is blank or relative, or the algorithm is undefined.</exception>
	public FileHashRequest(string filePath, TargetHashAlgorithm algorithm = TargetHashAlgorithm.Sha256)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
		if (!Path.IsPathFullyQualified(filePath))
		{
			throw new ArgumentException("A file hash path must be absolute.", nameof(filePath));
		}

		if (!Enum.IsDefined(algorithm))
		{
			throw new ArgumentOutOfRangeException(nameof(algorithm));
		}

		FilePath = filePath;
		Algorithm = algorithm;
	}

	/// <summary>Gets the absolute file path. The implementation verifies existence before hashing.</summary>
	public string FilePath
	{
		get;
	}

	/// <summary>Gets the requested hash algorithm.</summary>
	public TargetHashAlgorithm Algorithm
	{
		get;
	}
}
