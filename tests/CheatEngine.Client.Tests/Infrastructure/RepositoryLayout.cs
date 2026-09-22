namespace CheatEngine.Client.Tests.Infrastructure;

/// <summary>Locates the repository that built the running test assembly.</summary>
internal static class RepositoryLayout
{
	private const string SolutionFileName = "CheatEngine.Client.slnx";

	private static readonly Lazy<string> _root = new(FindRoot, LazyThreadSafetyMode.ExecutionAndPublication);

	/// <summary>The directory that contains <c>CheatEngine.Client.slnx</c>.</summary>
	internal static string Root => _root.Value;

	/// <summary>An absolute path below the repository root, from a forward-slash relative path.</summary>
	internal static string Combine(string relativePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
		return Path.GetFullPath(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
	}

	private static string FindRoot()
	{
		for (DirectoryInfo? candidate = new(AppContext.BaseDirectory);
			 candidate is not null;
			 candidate = candidate.Parent)
		{
			if (File.Exists(Path.Combine(candidate.FullName, SolutionFileName)))
			{
				return candidate.FullName;
			}
		}

		throw new DirectoryNotFoundException(
			$"Could not find the CheatEngine.Client repository root ({SolutionFileName}) above '{AppContext.BaseDirectory}'.");
	}
}
