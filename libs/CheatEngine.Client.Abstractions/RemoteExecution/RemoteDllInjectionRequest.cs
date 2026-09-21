namespace CheatEngine.Client.RemoteExecution;

/// <summary>Describes a DLL whose existing absolute path is injected into the selected target.</summary>
public readonly record struct RemoteDllInjectionRequest
{
	/// <summary>Creates a remote DLL-injection request.</summary>
	/// <exception cref="ArgumentException"><paramref name="libraryPath" /> is blank, relative, or not a DLL path.</exception>
	public RemoteDllInjectionRequest(string libraryPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
		if (!Path.IsPathFullyQualified(libraryPath))
		{
			throw new ArgumentException("A DLL injection path must be absolute.", nameof(libraryPath));
		}

		if (!string.Equals(Path.GetExtension(libraryPath), ".dll", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("A DLL injection path must have a .dll extension.", nameof(libraryPath));
		}

		LibraryPath = libraryPath;
	}

	/// <summary>Gets the absolute DLL path. The implementation verifies that it exists before injection.</summary>
	public string LibraryPath
	{
		get;
	}
}
