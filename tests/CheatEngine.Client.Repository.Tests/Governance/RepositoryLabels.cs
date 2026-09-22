namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Labels that exist in the GitHub repository (read with <c>gh label list</c> on 2026-09-23). Dependabot and issue
/// forms silently ignore an unknown label, so every label they name must be in this list; creating a label is a
/// maintainer action that updates this list in the same pull request.
/// </summary>
internal static class RepositoryLabels
{
	/// <summary>The known labels, case-sensitive.</summary>
	public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
	{
		"bug",
		"documentation",
		"duplicate",
		"enhancement",
		"good first issue",
		"help wanted",
		"invalid",
		"question",
		"wontfix",
		"abstractions",
		"binding",
		"fluent-api",
		"aob-scanning",
		"memory",
		"processes",
		"symbols",
		"address-list",
		"tests",
		"ci",
		"packaging",
		"dependencies",
		"github_actions"
	};
}
