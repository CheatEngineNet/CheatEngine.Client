using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Solution;

/// <summary>The solution is the single inventory CI builds and tests; a project outside it is never compiled.</summary>
public sealed class SolutionInventoryTests
{
	/// <summary>Projects deliberately kept out of the solution, with the reason. Adding one is a review decision.</summary>
	private static readonly Dictionary<string, string> _outOfSolution = new(StringComparer.Ordinal)
	{
		["templates/CheatEngine.Client.Templates/content/CheatEngine.Plugin/CheatEngine.Plugin.csproj"] =
			"Template content: it is packed as text and built only after 'dotnet new' instantiates it in the package smoke test."
	};

	[Fact]
	public void EveryProjectOnDiskIsInTheSolutionOrExplicitlyExcluded()
	{
		HashSet<string> listed = ReadSolutionProjects();
		List<string> missing = [];
		foreach (string project in RepositoryRoot.EnumerateSourceFiles("*.csproj"))
		{
			if (!listed.Contains(project) && !_outOfSolution.ContainsKey(project))
			{
				missing.Add(project);
			}
		}

		Assert.True(missing.Count == 0,
			$"Add these projects to CheatEngine.Client.slnx (dotnet sln add) or justify them in {nameof(_outOfSolution)}: {string.Join(", ", missing)}");
	}

	[Fact]
	public void EveryProjectInTheSolutionExistsAndNoExclusionIsStale()
	{
		HashSet<string> listed = ReadSolutionProjects();
		foreach (string project in listed)
		{
			Assert.True(File.Exists(Path.Combine(RepositoryRoot.Path, project)),
				$"CheatEngine.Client.slnx lists '{project}', which does not exist.");
		}

		foreach (string excluded in _outOfSolution.Keys)
		{
			Assert.True(File.Exists(Path.Combine(RepositoryRoot.Path, excluded)),
				$"The exclusion '{excluded}' names a project that no longer exists.");
			Assert.False(listed.Contains(excluded),
				$"'{excluded}' is in the solution now; remove it from {nameof(_outOfSolution)}.");
		}
	}

	private static HashSet<string> ReadSolutionProjects()
	{
		XDocument solution = XDocument.Load(RepositoryRoot.SolutionPath);
		HashSet<string> projects = new(StringComparer.Ordinal);
		foreach (XElement project in solution.Descendants("Project"))
		{
			string? path = (string?) project.Attribute("Path");
			if (path is not null)
			{
				projects.Add(path.Replace('\\', '/'));
			}
		}

		return projects;
	}
}
