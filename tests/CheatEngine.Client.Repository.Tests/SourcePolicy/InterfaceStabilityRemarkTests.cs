using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.SourcePolicy;

/// <summary>
///     The 1.x versioning policy depends on one fact per public interface: <b>Call-only</b> interfaces (the Client
///     implements them) may gain members in a minor release, <b>Implementable</b> interfaces (applications implement
///     them) are frozen for 1.x. Every public interface states which it is, and the versioning sections of the root and
///     facade READMEs name exactly the implementable ones.
/// </summary>
public sealed partial class InterfaceStabilityRemarkTests
{
	private const int RegexTimeoutMilliseconds = 1000;
	private const string CallOnlyMarker = "<b>Call-only.</b>";
	private const string ImplementableMarker = "<b>Implementable.</b>";
	private const string FrozenBullet = "- **Frozen for all of 1.x:**";

	private static readonly string[] VersioningReadmes = ["README.md", "src/CheatEngine.Client/README.md"];

	[Fact]
	public void EveryPublicInterfaceStatesWhetherItIsCallOnlyOrImplementable()
	{
		IReadOnlyList<PublicInterface> interfaces = ReadPublicInterfaces();
		string[] offenders =
		[
			.. interfaces
				.Where(static type => type.CallOnly == type.Implementable)
				.Select(static type => $"{type.Path}: {type.Name}")
		];

		Assert.True(interfaces.Count > 0, "No public interface was found; the policy test would pass vacuously.");
		Assert.True(offenders.Length == 0,
			$"Each public interface needs exactly one of '{CallOnlyMarker}' or '{ImplementableMarker}' in its " +
			"documentation remarks:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void TheVersioningSectionsNameExactlyTheImplementableInterfaces()
	{
		string[] implementable =
		[
			.. ReadPublicInterfaces()
				.Where(static type => type.Implementable)
				.Select(static type => type.Name)
				.Order(StringComparer.Ordinal)
		];

		Assert.NotEmpty(implementable);
		foreach (string readme in VersioningReadmes)
		{
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, readme));
			int start = Array.FindIndex(lines, static line => line.StartsWith(FrozenBullet, StringComparison.Ordinal));
			Assert.True(start >= 0, $"{readme} has no '{FrozenBullet}' bullet in its versioning section.");
			int end = Array.FindIndex(lines, start + 1, static line => !line.StartsWith("  ", StringComparison.Ordinal));
			string bullet = string.Join(' ', lines[start..end]);
			string[] named =
			[
				.. InterfaceName().Matches(bullet)
					.Select(static match => match.Groups["name"].Value)
					.Order(StringComparer.Ordinal)
			];

			Assert.Equal(implementable, named);
		}
	}

	private static List<PublicInterface> ReadPublicInterfaces()
	{
		List<PublicInterface> interfaces = [];
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*.cs")
					 .Where(static path => path.StartsWith("libs/", StringComparison.Ordinal) ||
										   path.StartsWith("src/", StringComparison.Ordinal))
					 .Order(StringComparer.Ordinal))
		{
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file));
			for (int index = 0; index < lines.Length; index++)
			{
				Match declaration = PublicInterfaceDeclaration().Match(lines[index]);
				if (!declaration.Success)
				{
					continue;
				}

				int first = index;
				while (first > 0 && (lines[first - 1].StartsWith("///", StringComparison.Ordinal) ||
									 lines[first - 1].StartsWith('[')))
				{
					first--;
				}

				string documentation = string.Join('\n', lines[first..index]);
				interfaces.Add(new PublicInterface(file, declaration.Groups["name"].Value,
					documentation.Contains(CallOnlyMarker, StringComparison.Ordinal),
					documentation.Contains(ImplementableMarker, StringComparison.Ordinal)));
			}
		}

		return interfaces;
	}

	[GeneratedRegex(@"^public (?:partial )?interface (?<name>I\w+)", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex PublicInterfaceDeclaration();

	[GeneratedRegex(@"`(?<name>I[A-Z]\w+)(?:<[^`]*>)?`", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex InterfaceName();

	private sealed record PublicInterface(string Path, string Name, bool CallOnly, bool Implementable);
}
