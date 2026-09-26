using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;
using CheatEngine.Client.Repository.Tests.Packaging;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Issue forms (A22-44, audit Checkpoint F: "an issue of compatibility can be tied to a precise tuple"). The
/// compatibility form requires every element of the release tuple; every form follows GitHub's form schema; the version
/// placeholders name the Client line and the pinned CheatEngine.SDK; blank issues are disabled and vulnerabilities are
/// routed to private reporting. Schema:
/// https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/syntax-for-issue-forms
/// </summary>
public sealed class IssueFormTests
{
	private const string TemplateFolder = ".github/ISSUE_TEMPLATE";
	private const string CompatibilityForm = TemplateFolder + "/compatibility.yml";

	/// <summary>
	/// The tuple a compatibility report must identify, from audit ch.21 l.15 (Client version, SDK package and its
	/// fingerprint, bridge, the Lua DLL actually used, the exact Cheat Engine and its runtime policy, the important build
	/// options and the load profile) and the Client tuple of shared-contracts §2.5, plus where the problem was observed
	/// (ch.20 levels C0-C4) and the target.
	/// </summary>
	private static readonly string[] RequiredTuple =
	[
		"client-version",
		"sdk-version",
		"sdk-content-hash",
		"bridge-sha256",
		"lua-dll-sha256",
		"ce-version",
		"ce-exe-sha256",
		"ce-build",
		"runtimeconfig-sha256",
		"runtimeconfig-state",
		"dotnet-runtimes",
		"load-profile",
		"plugin-build",
		"observed-level",
		"target",
		"os"
	];

	private static readonly HashSet<string> ElementTypes = new(StringComparer.Ordinal)
	{
		"markdown",
		"textarea",
		"input",
		"dropdown",
		"checkboxes"
	};

	private static readonly Regex ElementId = new("^[A-Za-z0-9_-]+$", RegexOptions.None, TimeSpan.FromSeconds(1));

	/// <summary>"qualified profile/build/route…", "(qualified", "is/are/was qualified", "host-qualified" (negations pass).</summary>
	private static readonly Regex QualifiedClaim = new(
		@"\bqualified\s+(profile|build|route|setup|configuration)\b|\(\s*qualified\b|\b(is|are|was)\s+(host-)?qualified\b|(?<!never\s)\bhost-qualified\b",
		RegexOptions.IgnoreCase,
		TimeSpan.FromSeconds(1));

	[Fact]
	public void CompatibilityFormRequiresTheFullReleaseTuple()
	{
		Dictionary<string, YamlMappingNode> elements = ElementsById(GovernanceFile.LoadYaml(CompatibilityForm));
		List<string> problems = [];
		foreach (string id in RequiredTuple)
		{
			if (!elements.TryGetValue(id, out YamlMappingNode? element))
			{
				problems.Add($"'{id}' is missing");
			}
			else if (!IsRequired(element))
			{
				problems.Add($"'{id}' is not required");
			}
		}

		Assert.True(problems.Count == 0, $"{CompatibilityForm}: {string.Join("; ", problems)}.");
		Assert.Equal(["(C0)", "(C1)", "(C2)", "(C3)", "(C4)"], Options(elements["observed-level"]).Select(LevelSuffix));
		Assert.True(elements.ContainsKey("bridge-fingerprint") && !IsRequired(elements["bridge-fingerprint"]));
	}

	[Fact]
	public void VersionPlaceholdersNameTheClientLineAndThePinnedSdk()
	{
		// A reporter copies these examples, so each one must be able to exist: a stable Client version of the current
		// major line (MinVerMinimumMajorMinor), exactly the pinned CheatEngine.SDK (eng/CheatEngineSdk.props), and the
		// content hash the lock files record for it, abbreviated as "<start>...<end>".
		string clientMajor = PackageVersioningTests.BuildProperty("MinVerMinimumMajorMinor").Split('.')[0];
		string contentHash = PinnedSdkContentHash();
		List<string> problems = [];
		int placeholders = 0;
		foreach (string form in IssueForms())
		{
			foreach ((string id, YamlMappingNode element) in ElementsById(GovernanceFile.LoadYaml(form)))
			{
				string placeholder = Placeholder(element) ?? string.Empty;
				if (id is "client-version" or "sdk-version" or "sdk-content-hash")
				{
					placeholders++;
				}

				string? problem = id switch
				{
					"client-version" when !IsStableVersionOfMajor(placeholder, clientMajor) =>
						$"is not a stable {clientMajor}.x.y version",
					"sdk-version" when placeholder != SdkPin.Version => $"is not the pinned {SdkPin.Version}",
					"sdk-content-hash" when !Abbreviates(placeholder, contentHash) =>
						"does not abbreviate the pinned content hash",
					_ => null
				};
				if (problem is not null)
				{
					problems.Add($"{form}: the {id} placeholder '{placeholder}' {problem}");
				}
			}
		}

		Assert.True(placeholders >= 5,
			$"Expected the version placeholders of the bug and compatibility forms, found {placeholders}.");
		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Fact]
	public void IssueFormsFollowGitHubFormSyntax()
	{
		List<string> problems = [];
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (string form in IssueForms())
		{
			YamlMappingNode root = GovernanceFile.LoadYaml(form);
			foreach (string key in new[] { "name", "description" })
			{
				if (string.IsNullOrWhiteSpace(GovernanceFile.Scalar(root, key)))
				{
					problems.Add($"{form}: top-level '{key}' is missing");
				}
			}

			if (!names.Add(GovernanceFile.Scalar(root, "name") ?? string.Empty))
			{
				problems.Add($"{form}: the form name is not unique");
			}

			YamlSequenceNode? body = GovernanceFile.Sequence(root, "body");
			if (body is null || body.Children.Count == 0)
			{
				problems.Add($"{form}: 'body' is missing or empty");
				continue;
			}

			HashSet<string> ids = new(StringComparer.Ordinal);
			foreach (YamlMappingNode element in body.Children.Cast<YamlMappingNode>())
			{
				problems.AddRange(ElementProblems(form, element, ids));
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Fact]
	public void BlankIssuesAreDisabledAndVulnerabilitiesGoToPrivateReporting()
	{
		YamlMappingNode config = GovernanceFile.LoadYaml(TemplateFolder + "/config.yml");
		List<string> urls = [.. (GovernanceFile.Sequence(config, "contact_links")?.Children ?? [])
			.Cast<YamlMappingNode>()
			.Select(link => GovernanceFile.Scalar(link, "url") ?? string.Empty)];

		Assert.Equal("false", GovernanceFile.Scalar(config, "blank_issues_enabled"));
		Assert.Contains("https://github.com/CheatEngineNet/CheatEngine.Client/security/advisories/new", urls);
		Assert.Contains("https://github.com/CheatEngineNet/CheatEngine.SDK/issues/new/choose", urls);
		Assert.All(urls, url => Assert.StartsWith("https://github.com/", url, StringComparison.Ordinal));
		Assert.All(IssueForms(), form =>
			Assert.Contains("private vulnerability reporting", GovernanceFile.ReadText(form), StringComparison.Ordinal));
	}

	[Fact]
	public void NoIssueFormPresentsAProfileAsQualified()
	{
		// Evidence discipline (audit ch.22 arbitration "Un test non exécuté reste non exécuté"): the managed hostfxr
		// profile is only qualifiable until host receipts exist, so a form may target it but never call it qualified.
		foreach (string form in Directory.EnumerateFiles(GovernanceFile.FullPath(TemplateFolder), "*.yml").Select(RepositoryRoot.ToRelative))
		{
			Match claim = QualifiedClaim.Match(GovernanceFile.ReadText(form));
			Assert.False(claim.Success, $"{form} presents something as qualified ('{claim.Value}'); say 'targeted for qualification'.");
		}
	}

	[Fact]
	public void IssueFormLabelsAreKnownRepositoryLabels()
	{
		foreach (string form in IssueForms())
		{
			IReadOnlyList<string> labels = GovernanceFile.Strings(GovernanceFile.Child(GovernanceFile.LoadYaml(form), "labels"));

			Assert.NotEmpty(labels);
			Assert.All(labels, label => Assert.Contains(label, RepositoryLabels.Known));
		}
	}

	private static IEnumerable<string> IssueForms()
	{
		return Directory.EnumerateFiles(GovernanceFile.FullPath(TemplateFolder), "*.yml")
			.Select(RepositoryRoot.ToRelative)
			.Where(path => !path.EndsWith("/config.yml", StringComparison.Ordinal))
			.Order(StringComparer.Ordinal);
	}

	private static IEnumerable<string> ElementProblems(string form, YamlMappingNode element, HashSet<string> ids)
	{
		string type = GovernanceFile.Scalar(element, "type") ?? string.Empty;
		string? id = GovernanceFile.Scalar(element, "id");
		YamlMappingNode? attributes = GovernanceFile.Mapping(element, "attributes");
		if (!ElementTypes.Contains(type))
		{
			yield return $"{form}: unknown element type '{type}'";
		}

		if (attributes is null)
		{
			yield return $"{form}: an element of type '{type}' has no attributes";
			yield break;
		}

		if (type == "markdown")
		{
			if (id is not null || GovernanceFile.Has(element, "validations"))
			{
				yield return $"{form}: a markdown element must not have an id or validations";
			}

			yield break;
		}

		if (id is null || !ElementId.IsMatch(id) || !ids.Add(id))
		{
			yield return $"{form}: element id '{id}' is missing, invalid or duplicated";
		}

		if (string.IsNullOrWhiteSpace(GovernanceFile.Scalar(attributes, "label")))
		{
			yield return $"{form}: element '{id}' has no label";
		}

		if (type == "dropdown")
		{
			IReadOnlyList<string> options = Options(element);
			bool unique = options.Distinct(StringComparer.Ordinal).Count() == options.Count;
			if (options.Count == 0 || options.Any(string.IsNullOrWhiteSpace) || !unique)
			{
				yield return $"{form}: dropdown '{id}' needs unique, non-empty options";
			}
		}

		if (type == "checkboxes")
		{
			IEnumerable<YamlNode> checkboxes = GovernanceFile.Sequence(attributes, "options")?.Children ?? [];
			foreach (YamlMappingNode option in checkboxes.Cast<YamlMappingNode>())
			{
				if (string.IsNullOrWhiteSpace(GovernanceFile.Scalar(option, "label"))
					|| GovernanceFile.Scalar(option, "required") is not (null or "true" or "false"))
				{
					yield return $"{form}: checkbox option of '{id}' needs a label and a boolean 'required'";
				}
			}
		}
	}

	private static string LevelSuffix(string option)
	{
		return option[option.LastIndexOf('(')..];
	}

	private static Dictionary<string, YamlMappingNode> ElementsById(YamlMappingNode form)
	{
		Dictionary<string, YamlMappingNode> elements = new(StringComparer.Ordinal);
		foreach (YamlMappingNode element in (GovernanceFile.Sequence(form, "body")?.Children ?? []).Cast<YamlMappingNode>())
		{
			if (GovernanceFile.Scalar(element, "id") is { } id)
			{
				elements.Add(id, element);
			}
		}

		return elements;
	}

	private static bool IsRequired(YamlMappingNode element)
	{
		return GovernanceFile.Mapping(element, "validations") is { } validations
			&& GovernanceFile.Scalar(validations, "required") == "true";
	}

	private static IReadOnlyList<string> Options(YamlMappingNode element)
	{
		return GovernanceFile.Strings(GovernanceFile.Mapping(element, "attributes") is { } attributes
			? GovernanceFile.Child(attributes, "options")
			: null);
	}

	private static string? Placeholder(YamlMappingNode element)
	{
		return GovernanceFile.Mapping(element, "attributes") is { } attributes
			? GovernanceFile.Scalar(attributes, "placeholder")
			: null;
	}

	private static bool IsStableVersionOfMajor(string version, string major)
	{
		string[] segments = version.Split('.');
		return segments.Length == 3 && segments[0] == major
			&& segments.All(static segment => segment.Length > 0 && segment.All(char.IsAsciiDigit));
	}

	/// <summary>Whether <paramref name="abbreviation" /> is "&lt;start&gt;...&lt;end&gt;" of <paramref name="value" />.</summary>
	private static bool Abbreviates(string abbreviation, string value)
	{
		string[] parts = abbreviation.Split("...");
		return parts.Length == 2 && parts[0].Length >= 8 && parts[1].Length > 0
			&& value.StartsWith(parts[0], StringComparison.Ordinal) && value.EndsWith(parts[1], StringComparison.Ordinal);
	}

	/// <summary>The content hash the lock files record for the pinned CheatEngine.SDK (one value, per SdkPinTests).</summary>
	private static string PinnedSdkContentHash()
	{
		using JsonDocument lockFile = SdkPin.ReadJson("libs/CheatEngine.Client.Core/packages.lock.json");
		string[] hashes =
		[
			.. lockFile.RootElement.GetProperty("dependencies").EnumerateObject()
				.Where(static framework => framework.Value.TryGetProperty(SdkPin.PackageId, out _))
				.Select(static framework => framework.Value.GetProperty(SdkPin.PackageId).GetProperty("contentHash"))
				.Select(static contentHash => contentHash.GetString() ?? string.Empty)
				.Distinct(StringComparer.Ordinal)
		];
		return Assert.Single(hashes);
	}
}
