using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>
/// Runs the repository's MSBuild guard targets against real projects with overridden global properties. Only the guard
/// target is evaluated and run: nothing is restored, built or packed, so each case takes a few seconds.
/// </summary>
public sealed class BuildGuardTests
{
	private const string SdkFacingLibrary = "libs/CheatEngine.Client.Hosting/CheatEngine.Client.Hosting.csproj";
	private const string SdkPinGuard = "CheatEngineClientValidateSdkPin";
	private const string SdkPackGuard = "CheatEngineClientRefuseUnsupportedSdkPack";
	private const string GeneratorProject =
		"source-generators/CheatEngine.Client.SourceGenerators.Lua/CheatEngine.Client.SourceGenerators.Lua.csproj";
	private const string RoslynPinGuard = "CheatEngineClientCheckRoslynPin";
	private const string PackableLibrary = "libs/CheatEngine.Client.Fluent/CheatEngine.Client.Fluent.csproj";
	private const string LockstepGuard = "CheatEngineClientValidateLockstepVersion";
	private const string SbomGuard = "CheatEngineClientRequireSbom";
	private const string ShippingSettingsGuard = "CheatEngineClientValidateShippingPackageSettings";

	[Fact]
	public async Task CommittedPinPassesTheSdkGuardAsync()
	{
		DotNetProcessResult result = await RunGuardAsync(SdkFacingLibrary, SdkPinGuard);

		Assert.True(result.ExitCode == 0, result.ToString());
		Assert.DoesNotContain("CHEATENGINECLIENT", result.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task NextMajorPinFailsWithCHEATENGINECLIENT9016Async()
	{
		int nextMajor = SdkPin.Major + 1;
		string[] nextMajorPin = [$"-p:CheatEngineSdkVersion={nextMajor}.0.0", $"-p:CheatEngineSdkUpperBound={nextMajor + 1}.0.0"];

		DotNetProcessResult result = await RunGuardAsync(SdkFacingLibrary, SdkPinGuard, nextMajorPin);
		DotNetProcessResult pack = await RunGuardAsync(SdkFacingLibrary, SdkPackGuard, nextMajorPin);

		Assert.True(result.ExitCode != 0, result.ToString());
		Assert.Contains("error CHEATENGINECLIENT9016", result.StandardOutput, StringComparison.Ordinal);
		Assert.Contains($"'{nextMajor}.0.0' has major version {nextMajor}", result.StandardOutput, StringComparison.Ordinal);
		Assert.True(pack.ExitCode != 0, pack.ToString());
		Assert.Contains("error CHEATENGINECLIENT9016", pack.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("cannot be packed", pack.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PrereleaseSdkPinFailsWithCHEATENGINECLIENT9016Async()
	{
		string prerelease = $"{SdkPin.Major}.1.0-beta.1";

		DotNetProcessResult result = await RunGuardAsync(SdkFacingLibrary, SdkPinGuard,
			$"-p:CheatEngineSdkVersion={prerelease}");

		Assert.True(result.ExitCode != 0, result.ToString());
		Assert.Contains("error CHEATENGINECLIENT9016", result.StandardOutput, StringComparison.Ordinal);
		Assert.Contains($"'{prerelease}' is a prerelease", result.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RoslynPinDriftFailsWithCHEATENGINECLIENT9020Async()
	{
		// A -p: switch cannot change a PackageVersion item, so the drift is simulated from the other side: the floor.
		DotNetProcessResult committed = await RunGuardAsync(GeneratorProject, RoslynPinGuard);
		DotNetProcessResult drifted = await RunGuardAsync(GeneratorProject, RoslynPinGuard,
			"-p:CheatEngineClientRoslynComponentFloor=5.8.0");

		Assert.True(committed.ExitCode == 0, committed.ToString());
		Assert.True(drifted.ExitCode != 0, drifted.ToString());
		Assert.Contains("error CHEATENGINECLIENT9020", drifted.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task LockstepGuardAcceptsMinVerAndRefusesEveryOtherVersionSourceAsync()
	{
		string minVerThenGuard = $"MinVer;{LockstepGuard}";

		DotNetProcessResult committed = await RunGuardAsync(PackableLibrary, minVerThenGuard);
		DotNetProcessResult skipped = await RunGuardAsync(PackableLibrary, minVerThenGuard, "-p:MinVerSkip=true");
		DotNetProcessResult overridden = await RunGuardAsync(PackableLibrary, minVerThenGuard, "-p:Version=9.9.9");

		Assert.True(committed.ExitCode == 0, committed.ToString());
		Assert.True(skipped.ExitCode != 0, skipped.ToString());
		Assert.Contains("error CHEATENGINECLIENT9019", skipped.StandardOutput, StringComparison.Ordinal);
		Assert.True(overridden.ExitCode != 0, overridden.ToString());
		Assert.Contains("error CHEATENGINECLIENT9019", overridden.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task SbomGuardRefusesAPackWithoutTheSbomAsync()
	{
		DotNetProcessResult committed = await RunGuardAsync(PackableLibrary, SbomGuard);
		DotNetProcessResult disabled = await RunGuardAsync(PackableLibrary, SbomGuard, "-p:GenerateSBOM=false");

		Assert.True(committed.ExitCode == 0, committed.ToString());
		Assert.True(disabled.ExitCode != 0, disabled.ToString());
		Assert.Contains("error CHEATENGINECLIENT9021", disabled.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ShippingProjectWithoutTrimReferenceVerificationFailsWithCHEATENGINECLIENT9008Async()
	{
		DotNetProcessResult committed = await RunGuardAsync(PackableLibrary, ShippingSettingsGuard);
		DotNetProcessResult disabled = await RunGuardAsync(PackableLibrary, ShippingSettingsGuard,
			"-p:VerifyReferenceTrimCompatibility=false");

		Assert.True(committed.ExitCode == 0, committed.ToString());
		Assert.DoesNotContain("CHEATENGINECLIENT", committed.StandardOutput, StringComparison.Ordinal);
		Assert.True(disabled.ExitCode != 0, disabled.ToString());
		Assert.Contains("error CHEATENGINECLIENT9008", disabled.StandardOutput, StringComparison.Ordinal);
	}

	private static Task<DotNetProcessResult> RunGuardAsync(string project, string target, params string[] properties)
	{
		List<string> arguments =
		[
			"msbuild", RepositoryLayout.Combine(project), $"-t:{target}", "-nologo", "-nodeReuse:false",
			"-verbosity:minimal"
		];
		arguments.AddRange(properties);
		return DotNetProcess.RunAsync(RepositoryLayout.Root, [.. arguments]);
	}
}
