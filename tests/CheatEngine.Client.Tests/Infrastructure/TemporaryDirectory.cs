namespace CheatEngine.Client.Tests.Infrastructure;

/// <summary>Creates and removes a uniquely owned directory below the operating system temporary directory.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
	private readonly string _root;

	internal TemporaryDirectory(string purpose)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

		_root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
			"CheatEngine.Client.Tests"));
		Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, purpose, Guid.NewGuid().ToString("N")));
		if (!Path.StartsWith(_root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Temporary directory '{Path}' is outside '{_root}'.");
		}

		Directory.CreateDirectory(Path);
	}

	internal string Path
	{
		get;
	}

	public void Dispose()
	{
		if (Directory.Exists(Path))
		{
			Directory.Delete(Path, true);
		}
	}

	internal string CreateDirectory(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		string directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, name));
		if (!directory.StartsWith(Path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Temporary child directory '{directory}' is outside '{Path}'.");
		}

		Directory.CreateDirectory(directory);
		return directory;
	}
}
