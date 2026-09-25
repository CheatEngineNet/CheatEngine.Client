using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Packaging;

/// <summary>
/// The Client consumes exactly one reviewed CheatEngine.SDK package (audit ADR-10, A21-01, A21-02): one source for the
/// pin, and lock files, project files and documentation that agree with it. The documentation checks (version
/// mentions, version ranges and the hashes of a CheatEngine.SDK tuple line) cover the shipped sources and templates,
/// the template content project, the governance documents, everything under <c>.github/</c> and the CHANGELOG except
/// its released history.
/// </summary>
public sealed partial class SdkPinTests
{
	private const int RegexTimeoutMilliseconds = 1000;

	private const string ChangelogPath = "CHANGELOG.md";

	/// <summary>The test fixture that holds the reviewed identity of the pinned package.</summary>
	private const string IdentityFixturePath = "tests/CheatEngine.Client.Tests/Packaging/PackagedClientFeedFixture.cs";

	/// <summary>
	/// Hashes of the host profile that a tuple line may state next to the CheatEngine.SDK identity: the SHA-256 of
	/// <c>cheatengine-x86_64.exe</c> 7.7.0.10621 and of the qualification host's <c>ce.runtimeconfig.json</c>. They are
	/// not CheatEngine.SDK values and do not move with the pin.
	/// </summary>
	private static readonly string[] HostProfileHashes =
	[
		"9727076da50924e4a097b49a02155e4b34759269c3017ff31375364b8826eb4d",
		"68f5d81c0a17cc5bdac40bb3d5d88a624f4d31b414f7195ad847d57b0126ac2b"
	];

	/// <summary>
	/// Documents outside the shipped folders whose CheatEngine.SDK facts are guarded too: the governance documents and
	/// the template content project, whose comment names the pin and its range. Every file under <c>.github/</c> and
	/// the CHANGELOG (<see cref="GuardedChangelogLines" />) are added to them.
	/// </summary>
	private static readonly string[] GovernanceDocuments =
	[
		".coderabbit.yaml",
		"CODE_OF_CONDUCT.md",
		"CONTRIBUTING.md",
		"RELEASING.md",
		"ROADMAP.md",
		"SECURITY.md",
		"templates/CheatEngine.Client.Templates/content/CheatEngine.Plugin/CheatEngine.Plugin.csproj"
	];

	/// <summary>The only values a CheatEngine.SDK version attribute may take outside the pin file.</summary>
	private static readonly HashSet<string> DerivedVersionExpressions = new(StringComparer.Ordinal)
	{
		"$(CheatEngineSdkVersion)",
		"$(CheatEngineSdkVersionRange)",
		"$(CoexistenceSdkPackageVersion)"
	};

	/// <summary>
	/// The reviewed identity literals of CheatEngine.SDK packages the Client no longer consumes. Each value is written
	/// here once so the fact below can find it; a major migration appends the identity it retires.
	/// </summary>
	private static readonly (string Literal, string Meaning)[] RetiredSdkIdentityLiterals =
	[
		("n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==",
			"the NuGet content hash of the retired 1.0.0 package"),
		("1a2B/E6reX5e636hfdb+Zdj3kT6817DuNES1RWvprhRyuyztE/56Zk2iHOMQIKpGH+O2Va8rYJxXXXTVq5aN9Q==",
			"the nuget.org signed-file SHA-512 of the retired 1.0.0 package"),
		("da08c2ba03019da3a8c432ef061d5d6133fd2169ba3a6a8e9ac903353856d994",
			"the SHA-256 of the native bridge packed in the retired 1.0.0 package"),
		("a6fefb93e9c6f85a1bcedb68bf97e6741175b227", "the source commit of the retired 1.0.0 package")
	];

	/// <summary>Properties that only <c>eng/CheatEngineSdk.props</c> may assign.</summary>
	private static readonly string[] PinProperties =
		["CheatEngineSdkVersion", "CheatEngineSdkUpperBound", "CheatEngineSdkVersionRange", "_CheatEngineClientSupportedSdkMajor"];

	/// <summary>
	/// Files that name the consumed SDK version in prose or in a sample. Each must name it at least once, and only as the
	/// pin: a sentence such as "CheatEngine.SDK X.Y.Z" stays true exactly as long as the pin is X.Y.Z.
	/// </summary>
	private static readonly string[] ProseLocations =
	[
		"README.md",
		"libs/CheatEngine.Client.Core/README.md",
		"libs/CheatEngine.Client.Hosting/README.md",
		"src/CheatEngine.Client/README.md",
		"templates/CheatEngine.Client.Templates/README.md",
		"tests/CheatEngine.Client.LivePlugin.Coexistence/README.md"
	];

	[Fact]
	public void SdkVersionAppearsAsALiteralOnlyInTheSdkPropsFile()
	{
		List<string> offenders = [];
		foreach (string file in EnumerateMsBuildFiles())
		{
			if (file == SdkPin.PropsPath || IsTemplateContent(file))
			{
				continue;
			}

			XDocument document = XDocument.Load(Path.Combine(RepositoryRoot.Path, file), LoadOptions.SetLineInfo);
			foreach (XElement element in document.Descendants())
			{
				string name = element.Name.LocalName;
				if (Array.IndexOf(PinProperties, name) >= 0 && element.Parent?.Name.LocalName == "PropertyGroup")
				{
					offenders.Add($"{file}:{LineOf(element)} → assigns {name} (only {SdkPin.PropsPath} may)");
				}

				string? condition = (string?) element.Attribute("Condition");
				if (condition is not null && ConditionVersionLiteral().IsMatch(condition))
				{
					offenders.Add($"{file}:{LineOf(element)} → Condition=\"{condition}\" compares an SDK version with a literal");
				}

				if (name is not ("PackageReference" or "PackageVersion" or "GlobalPackageReference")
					|| !IsSdk((string?) element.Attribute("Include") ?? (string?) element.Attribute("Update")))
				{
					continue;
				}

				foreach (string metadata in (string[]) ["Version", "VersionOverride"])
				{
					string? value = (string?) element.Attribute(metadata) ?? element.Element(metadata)?.Value;
					if (value is not null && !DerivedVersionExpressions.Contains(value.Trim()))
					{
						offenders.Add($"{file}:{LineOf(element)} → {metadata}=\"{value}\"");
					}
				}
			}

			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file));
			for (int index = 0; index < lines.Length; index++)
			{
				if (RangeLiteral().IsMatch(lines[index]))
				{
					offenders.Add($"{file}:{index + 1} → {lines[index].Trim()} (a literal version range)");
				}
			}
		}

		AssertNoOffenders(offenders,
			$"CheatEngine.SDK versions derive from {SdkPin.PropsPath}: use $(CheatEngineSdkVersion), $(CheatEngineSdkVersionRange) or $(CoexistenceSdkPackageVersion)");
	}

	[Fact]
	public void ProseMentionsOfTheConsumedSdkEqualThePin()
	{
		string pin = SdkPin.Version;
		List<string> offenders = [];
		foreach ((string file, IReadOnlyList<(int Line, string Text)> lines) in GuardedDocuments())
		{
			int mentions = 0;
			foreach ((int line, string text) in lines)
			{
				foreach (Match match in SdkVersionMention().Matches(text))
				{
					mentions++;
					string version = match.Groups["version"].Value;
					if (version != pin)
					{
						offenders.Add($"{file}:{line} → names CheatEngine.SDK {version}, but the pin is {pin}");
					}
				}
			}

			if (mentions == 0 && Array.IndexOf(ProseLocations, file) >= 0)
			{
				offenders.Add($"{file} → no longer names the consumed SDK version; remove it from {nameof(ProseLocations)}");
			}
		}

		AssertNoOffenders(offenders,
			"Documentation and diagnostics name the SDK the Client consumes, never another version (ADR-10: a feature exists for the Client only in the consumed package)");
	}

	[Fact]
	public void VersionRangesInTheDocumentationAreTheDeclaredSdkRange()
	{
		string range = $"[{SdkPin.Version}, {SdkPin.UpperBound})";
		List<string> offenders = [];
		int ranges = 0;
		foreach ((string file, IReadOnlyList<(int Line, string Text)> lines) in GuardedDocuments())
		{
			foreach ((int line, string literal) in FindForeignRanges(lines, range, ref ranges))
			{
				offenders.Add($"{file}:{line} → {literal}");
			}
		}

		Assert.True(ranges > 0, $"No guarded document states the declared range {range}; the READMEs must.");
		AssertNoOffenders(offenders,
			$"A version range in the documentation is the range the Client declares for CheatEngine.SDK, {range} " +
			$"({SdkPin.PropsPath}); state any other dependency's version without a range");
	}

	[Fact]
	public void TupleLineHashesAreTheReviewedIdentityOfThePinnedSdk()
	{
		ReviewedSdkIdentity identity = ReadReviewedIdentity();
		HashSet<string> lockHashes = new(StringComparer.Ordinal);
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("packages.lock.json"))
		{
			foreach ((_, JsonElement entry) in SdkLockEntries(file))
			{
				lockHashes.Add(entry.GetProperty("contentHash").GetString() ?? string.Empty);
			}
		}

		Assert.Equal(SdkPin.Version, identity.Version);
		Assert.True(lockHashes.SetEquals([identity.ContentHash]),
			$"{IdentityFixturePath} records the content hash {identity.ContentHash}, but the lock files record " +
			$"{string.Join(", ", lockHashes)}.");

		string[] allowed = [identity.ContentHash, identity.SignedFileHash, identity.BridgeHash, .. HostProfileHashes];
		List<string> offenders = [];
		HashSet<string> stated = new(StringComparer.OrdinalIgnoreCase);
		foreach ((string file, IReadOnlyList<(int Line, string Text)> lines) in GuardedDocuments())
		{
			foreach ((int line, string literal) in FindForeignTupleHashes(lines, allowed, stated))
			{
				offenders.Add($"{file}:{line} → {literal}");
			}
		}

		Assert.True(stated.Contains(identity.ContentHash) && stated.Contains(identity.BridgeHash),
			"No guarded document states the reviewed content hash and native bridge SHA-256 on a CheatEngine.SDK " +
			"tuple line; the install guides must.");
		AssertNoOffenders(offenders,
			"A content hash or bridge hash on a CheatEngine.SDK tuple line is the reviewed identity of " +
			$"CheatEngine.SDK {SdkPin.Version} ({IdentityFixturePath}) or a host profile hash");
	}

	[Fact]
	public void TheDocumentationChecksSeeTupleHashesRangesAndOnlyTheCurrentChangelog()
	{
		string content = new string('C', 86) + "==";
		string bridge = new('b', 64);
		string foreignHex = new('0', 64);
		string hostHex = HostProfileHashes[0].ToUpperInvariant();
		(int, string)[] lines =
		[
			(1, $"| Consumed SDK package | `CheatEngine.SDK` 2.0.0, NuGet content hash `{content}` |"),
			(2, $"| SDK native bridge | SHA-256 `{bridge}` |"),
			(3, $"Tuple: CheatEngine.SDK 2.0.0 ({content}), Cheat Engine 7.7.0.10621 x64 (`{hostHex}`)"),
			(4, $"| SDK native bridge | SHA-256 `{foreignHex}` |"),
			(5, $"ACTIONLINT_SHA256: {foreignHex} # an unrelated tool checksum"),
			(6, "Client 1.x declares `[2.0.0, 3.0.0)`; a stale guide said [2.0.0,4.0.0) and a pack `[1.0.0]`.")
		];
		HashSet<string> stated = new(StringComparer.OrdinalIgnoreCase);
		int ranges = 0;

		Assert.Equal([(4, foreignHex)], FindForeignTupleHashes(lines, [content, bridge, .. HostProfileHashes], stated));
		Assert.True(stated.SetEquals([content, bridge, hostHex, foreignHex]));
		Assert.Equal([(6, "[2.0.0,4.0.0)")], FindForeignRanges(lines, "[2.0.0, 3.0.0)", ref ranges));
		Assert.Equal(2, ranges);

		string[] changelog =
		[
			"# Changelog", "## [Unreleased]", "- CheatEngine.SDK 3.0.0", "## [1.1.0] - 2027-01-04", "- 1.1 line",
			"## [1.0.0] - 2026-09-25", "- CheatEngine.SDK 2.0.0 (history)", "## [0.9.0-rc.1] - 2026-01-01", "- older"
		];
		Assert.Equal([1, 2, 3, 4, 5],
			GuardedChangelogLines(changelog, new Version(1, 1)).Select(static line => line.Line));
		Assert.Equal(9, GuardedChangelogLines(changelog, new Version(0, 9)).Count);
	}

	[Fact]
	public void EveryLockFileResolvesThePinnedSdkWithOneContentHash()
	{
		string pin = SdkPin.Version;
		HashSet<string> contentHashes = new(StringComparer.Ordinal);
		List<string> offenders = [];
		int locks = 0;
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("packages.lock.json"))
		{
			locks++;
			foreach ((string framework, JsonElement entry) in SdkLockEntries(file))
			{
				string? resolved = entry.GetProperty("resolved").GetString();
				string? contentHash = entry.GetProperty("contentHash").GetString();
				contentHashes.Add(contentHash ?? string.Empty);
				if (resolved != pin)
				{
					offenders.Add($"{file} ({framework}) → resolves {SdkPin.PackageId} {resolved}, but the pin is {pin}");
				}
			}
		}

		Assert.True(locks >= 20, $"Expected the committed lock files, found {locks}.");
		Assert.True(contentHashes.Count == 1,
			$"Every lock must record one content hash for {SdkPin.PackageId} {pin}, found: {string.Join(", ", contentHashes)}.");
		AssertNoOffenders(offenders, "Lock files resolve the pinned CheatEngine.SDK");
	}

	[Fact]
	public void RetiredSdkIdentityLiteralsAppearNowhere()
	{
		const string self = "tests/CheatEngine.Client.Repository.Tests/Packaging/SdkPinTests.cs";
		List<string> offenders = [];
		int scanned = 0;
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*"))
		{
			// .claude/ holds local agent state (for example worktree copies of older commits), never repository content.
			if (file == self || file.StartsWith(".claude/", StringComparison.Ordinal) || !IsTextFile(file))
			{
				continue;
			}

			scanned++;
			string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, file));
			foreach ((string literal, string meaning) in RetiredSdkIdentityLiterals)
			{
				if (text.Contains(literal, StringComparison.Ordinal))
				{
					offenders.Add($"{file} → {meaning}");
				}
			}
		}

		Assert.True(scanned >= 100,
			$"Expected the repository's text files (lock files, workflows, sources, documentation), found {scanned}.");
		AssertNoOffenders(offenders,
			"No file keeps the identity of a CheatEngine.SDK package the Client no longer consumes; state the pinned " +
			$"identity instead ({SdkPin.PropsPath})");
	}

	[Fact]
	public void SdkPinIsAStableVersionOfTheSupportedMajor()
	{
		Match version = StableVersion().Match(SdkPin.Version);

		Assert.True(version.Success, $"The pin '{SdkPin.Version}' must be a stable major.minor.patch version.");
		Assert.Equal(SdkPin.SupportedMajor, version.Groups["major"].Value);
		Assert.Equal($"{int.Parse(SdkPin.SupportedMajor, System.Globalization.CultureInfo.InvariantCulture) + 1}.0.0", SdkPin.UpperBound);
		Assert.Equal("[$(CheatEngineSdkVersion),$(CheatEngineSdkUpperBound))", SdkPin.RangeExpression);
	}

	[Fact]
	public void CoexistenceFixturesDeriveTheirSdkVersionFromThePin()
	{
		const string propsPath = "tests/CheatEngine.Client.LivePlugin.Coexistence/CoexistencePlugin.props";
		XDocument props = XDocument.Load(Path.Combine(RepositoryRoot.Path, propsPath));

		XElement version = Assert.Single(props.Descendants("CoexistenceSdkPackageVersion"));
		XElement candidateLock = Assert.Single(props.Descendants("NuGetLockFilePath"));
		XElement reference = Assert.Single(props.Descendants("PackageReference"),
			static element => (string?) element.Attribute("Include") == SdkPin.PackageId);

		Assert.Equal("$(CheatEngineSdkVersion)", version.Value);
		// An operator-supplied SDK version restores into a lock file under obj/: disabling lock files while the committed
		// one exists fails the restore with NU1005, and rewriting the committed one would record an unreviewed package.
		Assert.Equal("'$(CoexistenceSdkPackageVersion)' != '$(CheatEngineSdkVersion)'", (string?) candidateLock.Attribute("Condition"));
		Assert.StartsWith("$(MSBuildProjectExtensionsPath)", candidateLock.Value, StringComparison.Ordinal);
		Assert.Empty(props.Descendants("RestorePackagesWithLockFile"));
		Assert.Equal("$(CoexistenceSdkPackageVersion)", (string?) reference.Attribute("Version"));
		Assert.Equal("false", Assert.Single(props.Descendants("ManagePackageVersionsCentrally")).Value);
	}

	[Fact]
	public void ConsumerSdkMajorGuardMatchesThePinUpperBound()
	{
		const string consumerTargets = "libs/CheatEngine.Client.Hosting/buildTransitive/CheatEngine.Client.Hosting.targets";
		XDocument targets = XDocument.Load(Path.Combine(RepositoryRoot.Path, consumerTargets));
		XElement upperMajor = Assert.Single(targets.Descendants("_CheatEngineClientSdkUpperMajor"));

		Assert.Equal(SdkPin.UpperBound.Split('.')[0], upperMajor.Value.Trim());
		Assert.Single(targets.Descendants("Error"), static error => (string?) error.Attribute("Code") == "CECLIENT017");
	}

	private static IEnumerable<string> EnumerateMsBuildFiles()
	{
		foreach (string pattern in (string[]) ["*.csproj", "*.props", "*.targets"])
		{
			foreach (string file in RepositoryRoot.EnumerateSourceFiles(pattern))
			{
				yield return file;
			}
		}
	}

	private static bool IsTemplateContent(string file)
	{
		return file.StartsWith("templates/", StringComparison.Ordinal) && file.Contains("/content/", StringComparison.Ordinal);
	}

	/// <summary>
	/// Whether a repository path belongs to a folder that ships: sources, libraries, generators, templates.
	/// </summary>
	internal static bool IsShippedSource(string file)
	{
		return file.StartsWith("src/", StringComparison.Ordinal) || file.StartsWith("libs/", StringComparison.Ordinal)
			|| file.StartsWith("source-generators/", StringComparison.Ordinal)
			|| file.StartsWith("templates/", StringComparison.Ordinal);
	}

	/// <summary>
	/// Every guarded document with the numbered lines the documentation checks read: the Markdown and C# files of the
	/// shipped folders, <see cref="ProseLocations" />, <see cref="GovernanceDocuments" />, every text file under
	/// <c>.github/</c>, and the CHANGELOG lines that are not released history.
	/// </summary>
	private static IEnumerable<(string File, IReadOnlyList<(int Line, string Text)> Lines)> GuardedDocuments()
	{
		SortedSet<string> files = new(ProseLocations, StringComparer.Ordinal);
		files.UnionWith(GovernanceDocuments);
		foreach (string pattern in (string[]) ["*.md", "*.cs"])
		{
			files.UnionWith(RepositoryRoot.EnumerateSourceFiles(pattern).Where(IsShippedSource));
		}

		string github = Path.Combine(RepositoryRoot.Path, ".github");
		files.UnionWith(Directory.EnumerateFiles(github, "*", SearchOption.AllDirectories)
			.Select(RepositoryRoot.ToRelative).Where(IsTextFile));
		foreach (string file in files)
		{
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file));
			List<(int Line, string Text)> numbered = [.. lines.Select(static (text, index) => (index + 1, text))];
			yield return (file, numbered);
		}

		Version floor = Version.Parse(PackageVersioningTests.BuildProperty("MinVerMinimumMajorMinor"));
		yield return (ChangelogPath,
			GuardedChangelogLines(File.ReadAllLines(Path.Combine(RepositoryRoot.Path, ChangelogPath)), floor));
	}

	/// <summary>
	/// The CHANGELOG lines the documentation checks read: all of them except the sections of versions below the MinVer
	/// floor (<c>MinVerMinimumMajorMinor</c>), which were released and keep the CheatEngine.SDK facts of their time.
	/// The floor moves to the next line in the change that follows a release (RELEASING.md).
	/// </summary>
	private static List<(int Line, string Text)> GuardedChangelogLines(string[] changelog, Version floor)
	{
		List<(int Line, string Text)> guarded = [];
		bool history = false;
		for (int index = 0; index < changelog.Length; index++)
		{
			string line = changelog[index];
			if (line.StartsWith("## ", StringComparison.Ordinal))
			{
				Match release = ChangelogRelease().Match(line);
				history = release.Success && Version.Parse(release.Groups["line"].Value) < floor;
			}

			if (!history)
			{
				guarded.Add((index + 1, line));
			}
		}

		return guarded;
	}

	/// <summary>
	/// The version ranges of <paramref name="lines" /> that differ from <paramref name="range" />, whitespace ignored;
	/// <paramref name="count" /> grows by every range found.
	/// </summary>
	private static List<(int Line, string Literal)> FindForeignRanges(IEnumerable<(int Line, string Text)> lines,
		string range, ref int count)
	{
		string expected = string.Concat(range.Where(static character => !char.IsWhiteSpace(character)));
		List<(int Line, string Literal)> foreign = [];
		foreach ((int line, string text) in lines)
		{
			foreach (Match match in RangeLiteral().Matches(text))
			{
				count++;
				if (!string.Equals(string.Concat(match.Value.Where(static character => !char.IsWhiteSpace(character))),
						expected, StringComparison.Ordinal))
				{
					foreign.Add((line, match.Value));
				}
			}
		}

		return foreign;
	}

	/// <summary>
	/// The SHA-512 (base64) and SHA-256 (hex, any case) literals on the CheatEngine.SDK tuple lines of
	/// <paramref name="lines" /> that are not in <paramref name="allowed" />. Every literal a tuple line states is
	/// added to <paramref name="stated" />; a hash on another line (a workflow's tool checksum) is not a tuple value.
	/// </summary>
	private static List<(int Line, string Literal)> FindForeignTupleHashes(IEnumerable<(int Line, string Text)> lines,
		string[] allowed, HashSet<string> stated)
	{
		List<(int Line, string Literal)> foreign = [];
		foreach ((int line, string text) in lines)
		{
			if (!SdkTupleLine().IsMatch(text))
			{
				continue;
			}

			foreach (Match match in HashLiteral().Matches(text))
			{
				string literal = match.Value;
				StringComparison comparison = match.Groups["hex"].Success
					? StringComparison.OrdinalIgnoreCase
					: StringComparison.Ordinal;
				stated.Add(literal);
				if (!allowed.Any(value => string.Equals(value, literal, comparison)))
				{
					foreign.Add((line, literal));
				}
			}
		}

		return foreign;
	}

	/// <summary>
	/// The reviewed identity of the pinned package, as <c>PackagedClientFeedFixture.PinnedSdkIdentity</c> records it
	/// for the package consumption tests.
	/// </summary>
	private static ReviewedSdkIdentity ReadReviewedIdentity()
	{
		string fixture = File.ReadAllText(Path.Combine(RepositoryRoot.Path, IdentityFixturePath));
		Match literal = PinnedIdentityLiteral().Match(fixture);
		Assert.True(literal.Success,
			$"{IdentityFixturePath} no longer declares PinnedSdkIdentity as a JsonDocument.Parse raw string literal.");
		using JsonDocument document = JsonDocument.Parse(literal.Groups["json"].Value);
		JsonElement root = document.RootElement;
		return new ReviewedSdkIdentity(
			root.GetProperty("version").GetString() ?? string.Empty,
			root.GetProperty("contentHashSha512").GetString() ?? string.Empty,
			root.GetProperty("nugetOrgSignedSha512").GetString() ?? string.Empty,
			root.GetProperty("nativeBridge").GetProperty("sha256").GetString() ?? string.Empty);
	}

	/// <summary>A file is text unless its first 8 KiB contain a NUL byte (the heuristic Git uses).</summary>
	internal static bool IsTextFile(string file)
	{
		using FileStream stream = File.OpenRead(Path.Combine(RepositoryRoot.Path, file));
		Span<byte> head = stackalloc byte[8192];
		int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
		return !head[..read].Contains((byte) 0);
	}

	private static bool IsSdk(string? packageId)
	{
		return string.Equals(packageId, SdkPin.PackageId, StringComparison.OrdinalIgnoreCase);
	}

	private static int LineOf(XElement element)
	{
		return ((System.Xml.IXmlLineInfo) element).LineNumber;
	}

	private static List<(string Framework, JsonElement Entry)> SdkLockEntries(string lockFile)
	{
		using JsonDocument document = SdkPin.ReadJson(lockFile);
		List<(string Framework, JsonElement Entry)> entries = [];
		foreach (JsonProperty framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
		{
			foreach (JsonProperty package in framework.Value.EnumerateObject())
			{
				if (IsSdk(package.Name) && package.Value.ValueKind == JsonValueKind.Object)
				{
					entries.Add((framework.Name, package.Value.Clone()));
				}
			}
		}

		return entries;
	}

	private static void AssertNoOffenders(List<string> offenders, string rule)
	{
		offenders.Sort(StringComparer.Ordinal);
		Assert.True(offenders.Count == 0,
			$"{rule}. Offenders ({offenders.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
	}

	[GeneratedRegex(@"\[\s*\d+\.\d+\.\d+[^,\]\)]*\s*,\s*\d+\.\d+\.\d+[^\]\)]*\s*[\]\)]", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex RangeLiteral();

	/// <summary>A condition that compares a property holding the consumed SDK version with a literal version.</summary>
	[GeneratedRegex(@"\$\((?:CheatEngineSdkVersion|CoexistenceSdkPackageVersion)\)'\s*[!=]=\s*'\d+\.\d+|'\d+\.\d+[^']*'\s*[!=]=\s*'\$\((?:CheatEngineSdkVersion|CoexistenceSdkPackageVersion)\)",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ConditionVersionLiteral();

	[GeneratedRegex(@"(?:(?<!\.NET\s)\bSDK|CheatEngine\.SDK`?(?:\s+package)?|Include=""CheatEngine\.SDK""\s+Version=)\s*[""`]?(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex SdkVersionMention();

	[GeneratedRegex(@"^(?<major>0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex StableVersion();

	/// <summary>A CHANGELOG release heading, with its major.minor line.</summary>
	[GeneratedRegex(@"^## \[(?<line>\d+\.\d+)\.\d+", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ChangelogRelease();

	/// <summary>A line that names the CheatEngine.SDK package, its native bridge or its content hash.</summary>
	[GeneratedRegex(@"CheatEngine\.SDK|(?<!\.NET\s)\bSDK\b|\b[Bb]ridge\b|\b[Cc]ontent ?[Hh]ash",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex SdkTupleLine();

	/// <summary>A whole SHA-256 in hex or SHA-512 in base64, not part of a longer token.</summary>
	[GeneratedRegex(@"(?<![A-Za-z0-9+/=])(?:(?<hex>[0-9A-Fa-f]{64})|[A-Za-z0-9+/]{86}==)(?![A-Za-z0-9+/=])",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex HashLiteral();

	[GeneratedRegex(@"PinnedSdkIdentity\s*=\s*JsonDocument\.Parse\(\s*""""""(?<json>.*?)""""""\s*\)",
		RegexOptions.CultureInvariant | RegexOptions.Singleline, RegexTimeoutMilliseconds)]
	private static partial Regex PinnedIdentityLiteral();

	/// <summary>The identity values of the pinned package that a tuple line may state.</summary>
	private sealed record ReviewedSdkIdentity(
		string Version,
		string ContentHash,
		string SignedFileHash,
		string BridgeHash);
}
