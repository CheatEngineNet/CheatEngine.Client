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

	/// <summary>
	/// Deletes the directory. NuGet extracts some files read-only, and a child process that has just exited can still hold
	/// a handle, so read-only attributes are cleared and the deletion is retried; a directory that still cannot be deleted
	/// is left for the operating system's temporary-file cleanup rather than failing the test run.
	/// </summary>
	public void Dispose()
	{
		for (int attempt = 0; attempt < 3 && Directory.Exists(Path); attempt++)
		{
			try
			{
				foreach (string file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
				{
					File.SetAttributes(file, FileAttributes.Normal);
				}

				Directory.Delete(Path, true);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				Thread.Sleep(TimeSpan.FromMilliseconds(500 * (attempt + 1)));
			}
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
