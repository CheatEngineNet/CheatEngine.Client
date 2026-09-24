using System.Text.Json;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>
/// Runs the canary recipe of <c>eng/CheatEngineSdk.props</c>, which the SDK repository's advisory client-canary job uses
/// to build this Client against an unreleased SDK (audit Q48, A11-30), on a throw-away copy of one SDK-facing library. The
/// candidate is the pinned SDK package re-versioned as a 2.0 prerelease, so the proof needs no SDK branch. It shares the
/// isolated package cache of the package consumption fixture, which the candidate never leaves.
/// </summary>
[Collection(PackageConsumptionSmokeSerialGroup.Name)]
[Trait("Category", "PackageConsumption")]
public sealed class SdkCanaryRecipeTests(PackagedClientFeedFixture fixture)
{
	private const string CandidateVersion = "2.0.0-alpha.0.42";
	private const string LibraryFolder = "libs/CheatEngine.Client.Abstractions";
	private const string LibraryProject = LibraryFolder + "/CheatEngine.Client.Abstractions.csproj";
	private const string LockFile = LibraryFolder + "/packages.lock.json";

	[Fact]
	public async Task CanaryRecipeBuildsAgainstACandidateSdkButNeverPacksAsync()
	{
		string feed = fixture.CreateDirectory("canary-sdk-feed");
		fixture.CreateReversionedSdkPackage(feed, CandidateVersion);
		string checkout = fixture.CreateDirectory("canary-checkout");
		CopyCheckout(checkout);
		PackagedClientFeedFixture.AssertOutsideAnyRepository(checkout);
		string configuration = PackagedClientFeedFixture.WriteNuGetConfiguration(Path.Combine(fixture.Root, "canary-NuGet.Config"),
			fixture.PackageCache, fixture.PackageSource, feed);
		string project = Path.Combine(checkout, LibraryProject);

		// The recipe of eng/CheatEngineSdk.props: lock files stay enabled and the restore is not locked.
		string[] recipe =
		[
			$"-p:RestoreConfigFile={configuration}", $"-p:CheatEngineSdkVersion={CandidateVersion}", "-p:CheatEngineSdkUpperBound=3.0.0",
			"-p:CheatEngineSdkCanary=true", "-p:CheatEngineClientAllowUnsupportedSdk=true"
		];
		string[] buildArguments = ["build", project, "--configuration", "Release", "-p:UseSharedCompilation=false", .. recipe];
		string[] packArguments = ["pack", project, "--configuration", "Release", "--no-build", .. recipe];
		DotNetProcessResult build = await fixture.RunAsync(checkout, buildArguments);
		DotNetProcessResult pack = await fixture.RunAsync(checkout, packArguments);

		Assert.True(build.ExitCode == 0, build.ToString());
		Assert.DoesNotContain("NU1005", build.StandardOutput + build.StandardError, StringComparison.Ordinal);
		Assert.Contains("CHEATENGINECLIENT9016 (canary build, not enforced", build.StandardOutput, StringComparison.Ordinal);
		Assert.Equal(CandidateVersion, LockedSdkVersion(Path.Combine(checkout, LockFile)));
		Assert.Equal(fixture.SdkVersion, LockedSdkVersion(RepositoryLayout.Combine(LockFile)));
		Assert.True(pack.ExitCode != 0, pack.ToString());
		Assert.Contains("error CHEATENGINECLIENT9016", pack.StandardOutput, StringComparison.Ordinal);
		Assert.Empty(Directory.GetFiles(checkout, "*.nupkg", SearchOption.AllDirectories));
		PackagedClientFeedFixture.Evidence(nameof(CanaryRecipeBuildsAgainstACandidateSdkButNeverPacksAsync),
			$"candidate={CandidateVersion} project={LibraryProject} build=0 (9016 reported, copy's lock rewritten) pack=refused (9016)");
	}

	/// <summary>
	/// Copies what a build of the library reads: the root MSBuild, SDK and NuGet files, <c>eng/</c> and the project
	/// folder. A worktree's <c>.git</c> file names the real repository, so it is never copied.
	/// </summary>
	private static void CopyCheckout(string checkout)
	{
		foreach (string file in Directory.GetFiles(RepositoryLayout.Root))
		{
			string name = Path.GetFileName(file);
			if (name != ".git")
			{
				File.Copy(file, Path.Combine(checkout, name));
			}
		}

		CopyDirectory(RepositoryLayout.Combine("eng"), Path.Combine(checkout, "eng"));
		CopyDirectory(RepositoryLayout.Combine(LibraryFolder), Path.Combine(checkout, LibraryFolder));
	}

	private static void CopyDirectory(string source, string destination)
	{
		foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			string relative = Path.GetRelativePath(source, file);
			if (relative.Split(Path.DirectorySeparatorChar).Any(static segment => segment is "bin" or "obj"))
			{
				continue;
			}

			string target = Path.Combine(destination, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target);
		}
	}

	private static string? LockedSdkVersion(string lockFile)
	{
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(lockFile));
		return document.RootElement.GetProperty("dependencies").GetProperty("net10.0")
			.GetProperty(PackagedClientFeedFixture.SdkPackageId).GetProperty("resolved").GetString();
	}
}
