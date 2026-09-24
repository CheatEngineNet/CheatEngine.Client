using System.Reflection;
using System.Runtime.Versioning;

using CheatEngine.Client.Tests.Infrastructure;
using CheatEngine.Client.Tests.Packaging;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The live qualification opt-in, without starting anything: an unauthorized run fails with instructions (never a
///     skip), CI always refuses, the packed packages are required, and every live fact is serial, traited and unskippable.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LiveQualificationOptInTests
{
	private const string LocalApplicationData = @"C:\Users\operator\AppData\Local";
	private const string Repository = @"D:\work\CheatEngine.Client";
	private const string Packages = @"D:\work\CheatEngine.Client\artifacts\nuget";

	[Fact]
	public void WithoutTheOptInTheRunFailsWithTheLocalCommand()
	{
		LiveQualificationDecision decision = Evaluate(new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			[PackageSourceResolution.PackageSourceVariable] = Packages
		});

		Assert.False(decision.IsAuthorized);
		Assert.Null(decision.Inputs);
		Assert.Contains(LiveQualificationOptIn.OptInVariable, decision.Refusal, StringComparison.Ordinal);
		Assert.All(LiveQualificationOptIn.LocalCommand, line => Assert.Contains(line, decision.Refusal, StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("")]
	[InlineData("yes")]
	[InlineData("i_authorize_ce77_live_probes_on_a_disposable_target")]
	[InlineData(" I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET")]
	public void OnlyTheExactPhraseIsAnOptIn(string phrase)
	{
		LiveQualificationDecision decision = Evaluate(Authorized(LiveQualificationOptIn.OptInVariable, phrase));

		Assert.False(decision.IsAuthorized);
		Assert.Contains("exact phrase", decision.Refusal, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("true")]
	[InlineData("TRUE")]
	public void ContinuousIntegrationAlwaysRefuses(string value)
	{
		LiveQualificationDecision decision = Evaluate(Authorized("CI", value));

		Assert.False(decision.IsAuthorized);
		Assert.StartsWith("CI=true", decision.Refusal, StringComparison.Ordinal);
		Assert.Contains("--filter-not-trait Category=LiveQualification", decision.Refusal, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(" ")]
	[InlineData("artifacts/nuget")]
	[InlineData(@"D:\missing")]
	public void ThePackedPackagesAreRequired(string? packageSource)
	{
		LiveQualificationDecision decision = Evaluate(Authorized(PackageSourceResolution.PackageSourceVariable, packageSource));

		Assert.False(decision.IsAuthorized);
		Assert.Contains(PackageSourceResolution.PackageSourceVariable, decision.Refusal, StringComparison.Ordinal);
	}

	[Fact]
	public void DefaultsAreTheProgramFilesInstallationAndALocalAppDataRunRoot()
	{
		LiveQualificationDecision decision = Evaluate(Authorized());

		Assert.True(decision.IsAuthorized, decision.Refusal);
		Assert.Equal(Path.GetFullPath(LiveQualificationOptIn.DefaultCheatEngineDirectory), decision.Inputs!.CheatEngineDirectory);
		Assert.Equal(Path.GetFullPath(Path.Combine(LocalApplicationData, "CheatEngine.Client.LiveQualification", "runs")),
			decision.Inputs.RunRoot);
		Assert.Equal(Packages, decision.Inputs.PackageSource);
	}

	[Fact]
	public void ConfiguredDirectoriesAreUsedWhenValid()
	{
		Dictionary<string, string?> variables = Authorized(LiveQualificationOptIn.CheatEngineDirectoryVariable, @"E:\CE77");
		variables[LiveQualificationOptIn.RunRootVariable] = @"E:\runs";

		LiveQualificationDecision decision = Evaluate(variables);

		Assert.True(decision.IsAuthorized, decision.Refusal);
		Assert.Equal(@"E:\CE77", decision.Inputs!.CheatEngineDirectory);
		Assert.Equal(@"E:\runs", decision.Inputs.RunRoot);
	}

	[Theory]
	[InlineData(LiveQualificationOptIn.RunRootVariable, @"D:\work\CheatEngine.Client\artifacts\runs")]
	[InlineData(LiveQualificationOptIn.RunRootVariable, @"C:\Program Files\Cheat Engine\runs")]
	[InlineData(LiveQualificationOptIn.RunRootVariable, "runs")]
	[InlineData(LiveQualificationOptIn.CheatEngineDirectoryVariable, "Cheat Engine")]
	[InlineData(LiveQualificationOptIn.CheatEngineDirectoryVariable, @"E:\missing")]
	public void InvalidDirectoriesAreRefused(string variable, string value)
	{
		LiveQualificationDecision decision = Evaluate(Authorized(variable, value));

		Assert.False(decision.IsAuthorized);
		Assert.Contains(variable == LiveQualificationOptIn.RunRootVariable ? "run root" : "Cheat Engine installation",
			decision.Refusal, StringComparison.Ordinal);
	}

	[Fact]
	public void TheReadmeStatesTheExactLocalCommand()
	{
		string readme = File.ReadAllText(RepositoryLayout.Combine("tests/CheatEngine.Client.Tests/README.md"));

		Assert.Contains("## Live qualification", readme, StringComparison.Ordinal);
		Assert.All(LiveQualificationOptIn.LocalCommand, line => Assert.Contains(line, readme, StringComparison.Ordinal));
	}

	[Fact]
	public void LiveFactsAreSerialTraitedAndNeverSkipped()
	{
		Type[] live = typeof(LiveQualificationOptInTests).Assembly.GetTypes()
			.Where(static type => Traits(type).Contains(("Category", "LiveQualification")) ||
								  Collection(type) == LiveQualificationSerialGroup.Name)
			.ToArray();

		Assert.Contains(typeof(LiveSandboxSpikeTests), live);
		foreach (Type type in live)
		{
			List<(string Name, string Value)> traits = Traits(type);
			Assert.Equal(LiveQualificationSerialGroup.Name, Collection(type));
			Assert.Contains(("Category", "LiveQualification"), traits);
			Assert.Single(traits, static trait => trait.Name == "Session" &&
												 trait.Value is ['S', >= '0' and <= '6']);
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
			{
				foreach (FactAttribute fact in method.GetCustomAttributes<FactAttribute>())
				{
					Assert.Null(fact.Skip);
					Assert.Null(fact.SkipUnless);
					Assert.Null(fact.SkipWhen);
					Assert.False(fact.Explicit, $"{type.Name}.{method.Name} is explicit: a live fact fails without the opt-in instead.");
				}
			}
		}
	}

	private static LiveQualificationDecision Evaluate(IReadOnlyDictionary<string, string?> variables)
	{
		HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase)
		{
			Packages,
			Path.GetFullPath(LiveQualificationOptIn.DefaultCheatEngineDirectory),
			LiveQualificationOptIn.DefaultCheatEngineDirectory,
			@"E:\CE77"
		};
		return LiveQualificationOptIn.Evaluate(name => variables.GetValueOrDefault(name), LocalApplicationData, Repository,
			existing.Contains);
	}

	private static Dictionary<string, string?> Authorized(string? variable = null, string? value = null)
	{
		Dictionary<string, string?> variables = new(StringComparer.Ordinal)
		{
			[LiveQualificationOptIn.OptInVariable] = LiveQualificationOptIn.Acknowledgement,
			[PackageSourceResolution.PackageSourceVariable] = Packages
		};
		if (variable is not null)
		{
			variables[variable] = value;
		}

		return variables;
	}

	private static List<(string Name, string Value)> Traits(Type type)
	{
		return type.GetCustomAttributesData()
			.Where(static attribute => attribute.AttributeType == typeof(TraitAttribute))
			.Select(static attribute => ((string) attribute.ConstructorArguments[0].Value!, (string) attribute.ConstructorArguments[1].Value!))
			.ToList();
	}

	private static string? Collection(Type type)
	{
		return type.GetCustomAttributesData()
			.Where(static attribute => attribute.AttributeType == typeof(CollectionAttribute))
			.Select(static attribute => attribute.ConstructorArguments[0].Value as string)
			.FirstOrDefault();
	}
}
