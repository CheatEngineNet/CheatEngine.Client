using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Toolchain;

/// <summary>
/// The toolchain is pinned so a build is reproducible from the commit alone: the exact .NET SDK (lock files record the
/// SDK-implicit packages), the analysis level (a newer SDK band must not add CA/IDE errors) and the NuGet audit policy.
/// The MSBuild guards CHEATENGINECLIENT9030-9032 catch overrides at build time; these tests catch edits of the pins.
/// </summary>
public sealed partial class ToolchainPinTests
{
	private const string SupplyChainTargetName = "CheatEngineClientValidateSupplyChainSettings";

	private static readonly JsonDocumentOptions JsonOptions = new()
	{
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	private static readonly string[] BlockingAuditCodes = ["NU1903", "NU1904"];

	private static readonly string[] AuditCodes = ["NU1900", "NU1901", "NU1902", "NU1903", "NU1904", "NU1905"];

	[Fact]
	public void GlobalJsonRequiresTheExactSdkWithRollForwardDisabled()
	{
		using JsonDocument globalJson = ReadGlobalJson();
		JsonElement sdk = globalJson.RootElement.GetProperty("sdk");

		string version = sdk.GetProperty("version").GetString() ?? string.Empty;
		Assert.Matches(SdkVersion(), version);
		Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());
		Assert.Equal(JsonValueKind.False, sdk.GetProperty("allowPrerelease").ValueKind);
		Assert.Equal("Microsoft.Testing.Platform",
			globalJson.RootElement.GetProperty("test").GetProperty("runner").GetString());
	}

	[Fact]
	public void GlobalJsonErrorMessageNamesThePinnedSdkVersion()
	{
		using JsonDocument globalJson = ReadGlobalJson();
		JsonElement sdk = globalJson.RootElement.GetProperty("sdk");
		string version = sdk.GetProperty("version").GetString() ?? string.Empty;

		Assert.True(sdk.TryGetProperty("errorMessage", out JsonElement errorMessage),
			"global.json must set sdk.errorMessage so a missing SDK fails with the install command.");
		string message = errorMessage.GetString() ?? string.Empty;
		Assert.Contains(version, message, StringComparison.Ordinal);
		Assert.Contains($"--version {version}", message, StringComparison.Ordinal);
	}

	[Fact]
	public void AnalysisLevelIsPinnedToAReleaseNotLatest()
	{
		XDocument props = LoadXml("Directory.Build.props");
		string pin = SingleUnconditionalProperty(props, "_CheatEngineClientPinnedAnalysisLevel");

		Assert.Matches(RecommendedAnalysisLevel(), pin);
		Assert.Equal("$(_CheatEngineClientPinnedAnalysisLevel)", SingleUnconditionalProperty(props, "AnalysisLevel"));

		using JsonDocument globalJson = ReadGlobalJson();
		string sdkVersion = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString() ?? string.Empty;
		string sdkMajorMinor = string.Join('.', sdkVersion.Split('.').Take(2));
		Assert.True(pin.StartsWith(sdkMajorMinor + "-", StringComparison.Ordinal),
			$"AnalysisLevel pin '{pin}' must follow the pinned SDK {sdkVersion}: raise both together.");
	}

	[Fact]
	public void NuGetAuditBlocksHighAndCriticalAdvisoriesInEveryBuild()
	{
		XDocument props = LoadXml("Directory.Build.props");
		Assert.Equal("true", SingleUnconditionalProperty(props, "NuGetAudit"));
		Assert.Equal("all", SingleUnconditionalProperty(props, "NuGetAuditMode"));
		Assert.Equal("low", SingleUnconditionalProperty(props, "NuGetAuditLevel"));

		XElement auditPipeline = Assert.Single(props.Descendants("WarningsAsErrors"),
			static element => ((string?) element.Attribute("Condition") ?? string.Empty).Contains("AuditPipeline",
				StringComparison.Ordinal));
		string auditCodes = SingleUnconditionalProperty(props, "_CheatEngineClientNuGetAuditCodes");
		Assert.Contains("$(_CheatEngineClientNuGetAuditCodes)", auditPipeline.Value, StringComparison.Ordinal);
		Assert.Equal(AuditCodes, SplitCodes(auditCodes));

		List<string> violations = [];
		foreach (string file in EnumerateMsBuildFiles())
		{
			XDocument document = LoadXml(file);
			foreach (XElement element in document.Descendants())
			{
				if (element.Name.LocalName is not ("NoWarn" or "WarningsNotAsErrors"))
				{
					continue;
				}

				foreach (string code in SplitCodes(element.Value))
				{
					if (BlockingAuditCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
					{
						violations.Add($"{file}: <{element.Name.LocalName}> lists {code}");
					}
				}
			}
		}

		Assert.True(violations.Count == 0,
			"High and critical advisories must fail every build; use NuGetAuditSuppress for a single advisory instead: " +
			string.Join("; ", violations));
	}

	[Fact]
	public void SupplyChainGuardsUseTheAllocatedDiagnosticIds()
	{
		XDocument targets = LoadXml("Directory.Build.targets");
		XElement guard = Assert.Single(targets.Root!.Elements("Target"),
			static target => (string?) target.Attribute("Name") == SupplyChainTargetName);
		Assert.Equal("BeforeBuild", (string?) guard.Attribute("BeforeTargets"));

		HashSet<string> guardCodes = new(StringComparer.Ordinal);
		foreach (XElement error in guard.Elements("Error"))
		{
			string code = (string?) error.Attribute("Code") ?? string.Empty;
			Assert.Matches(ToolchainGuardCode(), code);
			guardCodes.Add(code);
		}

		string[] expectedCodes = ["CHEATENGINECLIENT9030", "CHEATENGINECLIENT9031", "CHEATENGINECLIENT9032"];
		Assert.Equal(expectedCodes, guardCodes.Order(StringComparer.Ordinal).ToArray());

		foreach (XElement error in targets.Descendants("Error"))
		{
			string code = (string?) error.Attribute("Code") ?? string.Empty;
			if (code.StartsWith("CHEATENGINECLIENT903", StringComparison.Ordinal))
			{
				Assert.Same(guard, error.Parent);
			}
		}
	}

	private static JsonDocument ReadGlobalJson()
	{
		return JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "global.json")), JsonOptions);
	}

	private static XDocument LoadXml(string relativePath)
	{
		return XDocument.Load(Path.Combine(RepositoryRoot.Path, relativePath));
	}

	private static string SingleUnconditionalProperty(XDocument document, string name)
	{
		XElement property = Assert.Single(document.Descendants(name),
			static element => element.Attribute("Condition") is null);
		return property.Value.Trim();
	}

	private static string[] SplitCodes(string value)
	{
		return value.Split([';', ',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Where(static code => !code.StartsWith("$(", StringComparison.Ordinal))
			.ToArray();
	}

	private static IEnumerable<string> EnumerateMsBuildFiles()
	{
		foreach (string pattern in new[] { "*.props", "*.targets", "*.csproj" })
		{
			foreach (string file in RepositoryRoot.EnumerateSourceFiles(pattern))
			{
				yield return file;
			}
		}
	}

	[GeneratedRegex(@"^\d+\.\d+\.\d{3}$")]
	private static partial Regex SdkVersion();

	[GeneratedRegex(@"^\d+\.\d+-recommended$")]
	private static partial Regex RecommendedAnalysisLevel();

	[GeneratedRegex("^CHEATENGINECLIENT903[0-9]$")]
	private static partial Regex ToolchainGuardCode();
}
