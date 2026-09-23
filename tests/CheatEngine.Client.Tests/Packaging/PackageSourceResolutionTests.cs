namespace CheatEngine.Client.Tests.Packaging;

/// <summary>
/// The package source rules, without packing anything. Deliberately not in the PackageConsumption category, so both CI
/// legs run them.
/// </summary>
public sealed class PackageSourceResolutionTests
{
	private static readonly string _existing = Path.GetFullPath(Path.GetTempPath());

	[Fact]
	public void MissingPackageSourceFailsInContinuousIntegration()
	{
		PackageSourceDecision decision = PackageSourceResolution.Resolve(null, true, static _ => true);

		Assert.Equal(PackageSourceKind.Invalid, decision.Kind);
		Assert.Contains(PackageSourceResolution.PackageSourceVariable, decision.Error, StringComparison.Ordinal);
		Assert.Contains("Release leg", decision.Error, StringComparison.Ordinal);
		Assert.Contains("--filter-not-trait \"Category=PackageConsumption\"", decision.Error, StringComparison.Ordinal);
	}

	[Fact]
	public void MissingPackageSourceSelfPacksLocally()
	{
		PackageSourceDecision decision = PackageSourceResolution.Resolve(null, false, static _ => true);

		Assert.Equal(PackageSourceKind.SelfPack, decision.Kind);
		Assert.Null(decision.Directory);
		Assert.Null(decision.Error);
	}

	[Fact]
	public void RelativeOrMissingPackageSourceIsRejected()
	{
		PackageSourceDecision empty = PackageSourceResolution.Resolve(" ", false, static _ => true);
		PackageSourceDecision relative = PackageSourceResolution.Resolve("artifacts/nuget", true, static _ => true);
		PackageSourceDecision missing = PackageSourceResolution.Resolve(_existing, true, static _ => false);
		PackageSourceDecision configured = PackageSourceResolution.Resolve(_existing, true, static _ => true);

		Assert.Equal(PackageSourceKind.Invalid, empty.Kind);
		Assert.Contains("empty", empty.Error, StringComparison.Ordinal);
		Assert.Equal(PackageSourceKind.Invalid, relative.Kind);
		Assert.Contains("absolute", relative.Error, StringComparison.Ordinal);
		Assert.Equal(PackageSourceKind.Invalid, missing.Kind);
		Assert.Contains("missing directory", missing.Error, StringComparison.Ordinal);
		Assert.Equal(PackageSourceKind.ConfiguredDirectory, configured.Kind);
		Assert.Equal(_existing, configured.Directory);
	}
}
