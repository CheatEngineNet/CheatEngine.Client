using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Workflows;

/// <summary>
/// Freezes the analysis scope of <c>sonar.yml</c>: every excluded path exists, and shipping code is excluded from the
/// coverage metric file by file, never by a folder pattern, so a new file in <c>libs/</c> or <c>src/</c> keeps full
/// coverage accounting unless a lot deliberately lists it.
/// </summary>
public sealed partial class SonarWorkflowTests
{
	private const string SonarWorkflow = ".github/workflows/sonar.yml";

	/// <summary>Build output, created by the analysis build itself and never tracked.</summary>
	private const string BuildOutputPattern = "artifacts/**";

	[Fact]
	public void EveryAnalysisExclusionNamesAPathThatExists()
	{
		string[] patterns = [.. Exclusions("sonar.exclusions"), .. Exclusions("sonar.coverage.exclusions")];
		string[] missing =
		[
			.. patterns.Where(static pattern => pattern != BuildOutputPattern)
				.Where(static pattern => !Exists(pattern))
		];

		Assert.NotEmpty(patterns);
		Assert.True(missing.Length == 0,
			$"{SonarWorkflow} excludes paths that do not exist; remove them: {string.Join(", ", missing)}");
		Assert.DoesNotContain("docs/**", patterns);
	}

	[Fact]
	public void ShippingCodeIsExcludedFromCoverageFileByFile()
	{
		string[] shipping =
		[
			.. Exclusions("sonar.coverage.exclusions")
				.Where(static pattern => pattern.StartsWith("libs/", StringComparison.Ordinal) ||
										 pattern.StartsWith("src/", StringComparison.Ordinal))
		];

		Assert.NotEmpty(shipping);
		Assert.All(shipping, static pattern =>
		{
			Assert.DoesNotContain('*', pattern);
			Assert.EndsWith(".cs", pattern, StringComparison.Ordinal);
		});
		Assert.DoesNotContain(Exclusions("sonar.exclusions"), static pattern =>
			pattern.StartsWith("libs/", StringComparison.Ordinal) || pattern.StartsWith("src/", StringComparison.Ordinal));
	}

	private static string[] Exclusions(string property)
	{
		WorkflowStep begin = Assert.Single(WorkflowFile.Load(SonarWorkflow).Job("analyze").Steps,
			static step => step.Run.Contains("dotnet-sonarscanner.exe\" begin", StringComparison.Ordinal));
		Match match = Assert.Single(ScannerProperty().Matches(begin.Run),
			candidate => candidate.Groups["name"].Value == property);
		return match.Groups["value"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	private static bool Exists(string pattern)
	{
		string path = Path.Combine(RepositoryRoot.Path,
			(pattern.EndsWith("/**", StringComparison.Ordinal) ? pattern[..^3] : pattern).Replace('/', Path.DirectorySeparatorChar));
		return Directory.Exists(path) || File.Exists(path);
	}

	[GeneratedRegex(@"/d:(?<name>sonar\.[a-z.]+)=(?<value>[^'""\r\n]+)", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ScannerProperty();
}
