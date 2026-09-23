using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;
using CheatEngine.Client.Repository.Tests.Release;

namespace CheatEngine.Client.Repository.Tests.Packaging;

/// <summary>
/// The Client consumes exactly one reviewed CheatEngine.SDK package (audit ADR-10, A21-01, A21-02): one source for the
/// pin, one identity file, and lock files, project files and documentation that agree with both.
/// </summary>
public sealed partial class SdkPinTests
{
	private const int RegexTimeoutMilliseconds = 1000;

	/// <summary>The only values a CheatEngine.SDK version attribute may take outside the pin file.</summary>
	private static readonly HashSet<string> _derivedVersionExpressions = new(StringComparer.Ordinal)
	{
		"$(CheatEngineSdkVersion)",
		"$(CheatEngineSdkVersionRange)",
		"$(CoexistenceSdkPackageVersion)"
	};

	/// <summary>Properties that only <c>eng/CheatEngineSdk.props</c> may assign.</summary>
	private static readonly string[] _pinProperties =
		["CheatEngineSdkVersion", "CheatEngineSdkUpperBound", "CheatEngineSdkVersionRange", "_CheatEngineClientSupportedSdkMajor"];

	/// <summary>
	/// Files that name the consumed SDK version in prose or in a sample. Each must name it at least once, and only as the
	/// pin: "CheatEngine.SDK 1.0.0" stays true exactly as long as the pin is 1.0.0.
	/// </summary>
	private static readonly string[] _proseLocations =
	[
		"README.md",
		"libs/CheatEngine.Client.Core/Domains/RuntimeClient.cs",
		"libs/CheatEngine.Client.Core/Domains/UnavailableValueScanner.cs",
		"libs/CheatEngine.Client.Core/README.md",
		"libs/CheatEngine.Client.Hosting/README.md",
		"src/CheatEngine.Client/README.md",
		"templates/CheatEngine.Client.Templates/README.md",
		"tests/CheatEngine.Client.LivePlugin.Coexistence/README.md"
	];

	/// <summary>The identity fields; the schema's <c>required</c> list must stay equal to them.</summary>
	private static readonly string[] _identityFields =
	[
		"schema", "id", "version", "range", "contentHashSha512", "attestedAssetSha256", "nugetOrgSignedSha256",
		"nugetOrgSignedSha512", "sourceCommit", "sourceTreeHash", "nativeBridge", "lockFile"
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
				if (Array.IndexOf(_pinProperties, name) >= 0 && element.Parent?.Name.LocalName == "PropertyGroup")
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
					if (value is not null && !_derivedVersionExpressions.Contains(value.Trim()))
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
		HashSet<string> files = new(_proseLocations, StringComparer.Ordinal);
		foreach (string pattern in (string[]) ["*.md", "*.cs"])
		{
			foreach (string file in RepositoryRoot.EnumerateSourceFiles(pattern))
			{
				if (file.StartsWith("src/", StringComparison.Ordinal) || file.StartsWith("libs/", StringComparison.Ordinal)
					|| file.StartsWith("templates/", StringComparison.Ordinal))
				{
					files.Add(file);
				}
			}
		}

		List<string> offenders = [];
		foreach (string file in files.Order(StringComparer.Ordinal))
		{
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file));
			int mentions = 0;
			for (int index = 0; index < lines.Length; index++)
			{
				foreach (Match match in SdkVersionMention().Matches(lines[index]))
				{
					mentions++;
					string version = match.Groups["version"].Value;
					if (version != pin)
					{
						offenders.Add($"{file}:{index + 1} → names CheatEngine.SDK {version}, but the pin is {pin}");
					}
				}
			}

			if (mentions == 0 && Array.IndexOf(_proseLocations, file) >= 0)
			{
				offenders.Add($"{file} → no longer names the consumed SDK version; remove it from {nameof(_proseLocations)}");
			}
		}

		AssertNoOffenders(offenders,
			"Documentation and diagnostics name the SDK the Client consumes, never another version (ADR-10: a feature exists for the Client only in the consumed package)");
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
	public void ConsumedSdkIdentityMatchesThePinAndTheLockFiles()
	{
		using JsonDocument identity = SdkPin.ReadJson(SdkPin.IdentityPath);
		JsonElement root = identity.RootElement;
		string contentHash = root.GetProperty("contentHashSha512").GetString()!;
		string lockFile = root.GetProperty("lockFile").GetString()!;

		Assert.Equal(SdkPin.PackageId, root.GetProperty("id").GetString());
		Assert.Equal(SdkPin.Version, root.GetProperty("version").GetString());
		Assert.Equal(SdkPin.Range, SdkPin.NormalizeRange(root.GetProperty("range").GetString()!));

		List<string> offenders = [];
		(_, JsonElement declared) = Assert.Single(SdkLockEntries(lockFile));
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("packages.lock.json"))
		{
			foreach ((string framework, JsonElement entry) in SdkLockEntries(file))
			{
				if (entry.GetProperty("contentHash").GetString() != contentHash)
				{
					offenders.Add($"{file} ({framework}) → contentHash {entry.GetProperty("contentHash").GetString()}");
				}
			}
		}

		Assert.Equal(contentHash, declared.GetProperty("contentHash").GetString());
		AssertNoOffenders(offenders,
			$"{SdkPin.IdentityPath} records the NuGet SHA-512 content hash read from the lock files ({contentHash}); a lock that differs consumes another package");
	}

	[Fact]
	public void ConsumedSdkIdentityFollowsTheEncodingRules()
	{
		using JsonDocument schema = SdkPin.ReadJson(SdkPin.IdentitySchemaPath);
		using JsonDocument identity = SdkPin.ReadJson(SdkPin.IdentityPath);

		Assert.Equal(_identityFields, JsonSchemaSubset.RequiredNames(schema.RootElement));
		Assert.Equal(
			$"https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/{SdkPin.IdentitySchemaPath}",
			schema.RootElement.GetProperty("$id").GetString());

		IReadOnlyList<string> errors = JsonSchemaSubset.Validate(schema.RootElement, identity.RootElement);
		Assert.True(errors.Count == 0,
			$"{SdkPin.IdentityPath} does not satisfy {SdkPin.IdentitySchemaPath}:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
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
}
