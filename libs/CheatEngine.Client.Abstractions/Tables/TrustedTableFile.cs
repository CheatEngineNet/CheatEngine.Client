namespace CheatEngine.Client.Tables;

/// <summary>An explicitly trusted, normalized Cheat Engine table path.</summary>
public readonly record struct TrustedTableFile
{
	/// <summary>Creates a trusted table path. Policy validation is still performed by the configured table client.</summary>
	/// <param name="path">The absolute path of the table file; it is normalized.</param>
	/// <exception cref="ArgumentNullException"><paramref name="path" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException">
	///     <paramref name="path" /> is empty, white space or not fully qualified.
	/// </exception>
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
