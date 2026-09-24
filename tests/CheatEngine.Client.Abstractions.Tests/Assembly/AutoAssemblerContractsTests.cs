#pragma warning disable CECLIENT5004 // The contract tests exercise the experimental Auto Assembler surface.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using CheatEngine.Client.Assembly;

namespace CheatEngine.Client.Abstractions.Tests.Assembly;

/// <summary>The experimental Auto Assembler contracts (CECLIENT5004) and their redaction rules.</summary>
public sealed class AutoAssemblerContractsTests
{
	private const string DiagnosticId = "CECLIENT5004";

	private const string UrlFormat =
		"https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Abstractions/README.md#{0}";

	[Fact]
	public void ACheckResultKeepsTheVerdictAndNeverFormatsTheHostMessages()
	{
		AutoAssemblerCheckResult rejected = new(false, "Error in line 2: player_health", true);
		AutoAssemblerCheckResult accepted = new(true, null, false);

		Assert.False(rejected.IsAccepted);
		Assert.Equal("Error in line 2: player_health", rejected.HostMessages);
		Assert.True(rejected.HostMessagesTruncated);
		Assert.Equal("Rejected", rejected.ToString());
		Assert.Equal("Accepted", accepted.ToString());
		Assert.Throws<ArgumentException>(() => new AutoAssemblerCheckResult(false, null, true));
	}

	[Fact]
	public void ThePatchLeaseIsAClientLeaseAndPatchesAreNotPartOfTheAssemblyClient()
	{
		Assert.True(typeof(ICheatEngineLease).IsAssignableFrom(typeof(IAutoAssemblerPatchLease)));
		Assert.DoesNotContain(typeof(IAssemblyClient).GetMethods(),
			static method => method.Name.Contains("Patch", StringComparison.Ordinal));
		Assert.DoesNotContain(typeof(ICheatEngineClient).GetProperties(),
			static property => property.PropertyType == typeof(IAutoAssemblerClient));
	}

	[Theory]
	[InlineData(typeof(IAutoAssemblerClient))]
	[InlineData(typeof(IAutoAssemblerPatchLease))]
	[InlineData(typeof(AutoAssemblerCheckResult))]
	[InlineData(typeof(AutoAssemblerScript))]
	public void EveryAutoAssemblerTypeIsExperimentalUnderItsDocumentedId(Type type)
	{
		ExperimentalAttribute experimental = Assert.Single(type.GetCustomAttributes<ExperimentalAttribute>());

		Assert.Equal(DiagnosticId, experimental.DiagnosticId);
		Assert.Equal(UrlFormat, experimental.UrlFormat);
	}
}
