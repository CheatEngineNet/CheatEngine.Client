namespace CheatEngine.Client.Tables;

/// <summary>An explicitly trusted, normalized Cheat Engine table path.</summary>
public readonly record struct TrustedTableFile
{
	/// <summary>Creates a trusted table path. Policy validation is still performed by the configured table client.</summary>
	public TrustedTableFile(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		if (!Path.IsPathFullyQualified(path))
		{
			throw new ArgumentException("A trusted Cheat Engine table path must be absolute.", nameof(path));
		}

		FullPath = Path.GetFullPath(path);
	}

	/// <summary>Gets the normalized absolute path.</summary>
	public string FullPath
	{
		get;
	}
}
