namespace CheatEngine.Client.Tests.Packaging;

/// <summary>How the package consumption tests obtain the packages they consume.</summary>
internal enum PackageSourceKind
{
	/// <summary>The exact package directory named by <c>CHEATENGINE_CLIENT_PACKAGE_SOURCE</c>.</summary>
	ConfiguredDirectory,

	/// <summary>A local run without a configured directory: the tests pack the repository into a temporary feed.</summary>
	SelfPack,

	/// <summary>The configuration cannot be used; <see cref="PackageSourceDecision.Error"/> says why and how to fix it.</summary>
	Invalid
}

/// <summary>The outcome of <see cref="PackageSourceResolution.Resolve"/>.</summary>
internal sealed record PackageSourceDecision(PackageSourceKind Kind, string? Directory, string? Error);

/// <summary>
/// Chooses the package directory of the consumption tests. In continuous integration the tests must consume the packages
/// the Release leg packed and uploaded, never a package they build themselves, so a missing directory is a failure there.
/// </summary>
internal static class PackageSourceResolution
{
	/// <summary>The environment variable that names the exact package directory.</summary>
	internal const string PackageSourceVariable = "CHEATENGINE_CLIENT_PACKAGE_SOURCE";

	/// <summary>Reads the process environment and resolves the package source.</summary>
	internal static PackageSourceDecision ResolveFromEnvironment()
	{
		return Resolve(Environment.GetEnvironmentVariable(PackageSourceVariable),
			string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase),
			Directory.Exists);
	}

	/// <summary>Resolves the package source from explicit inputs, so the rules are testable without an environment.</summary>
	internal static PackageSourceDecision Resolve(string? configured, bool continuousIntegration, Func<string, bool> directoryExists)
	{
		ArgumentNullException.ThrowIfNull(directoryExists);

		if (configured is null)
		{
			return continuousIntegration
				? new PackageSourceDecision(PackageSourceKind.Invalid, null,
					$"{PackageSourceVariable} is not set, but CI=true. In continuous integration the package consumption tests " +
					"must consume the exact packages the Release leg packed: the Release leg of .github/workflows/ci.yml sets " +
					$"{PackageSourceVariable} to the absolute artifacts/nuget directory of its Pack step, and the Debug leg " +
					"excludes these tests with --filter-not-trait \"Category=PackageConsumption\". Packing here would test a " +
					"different package from the one CI uploads.")
				: new PackageSourceDecision(PackageSourceKind.SelfPack, null, null);
		}

		if (string.IsNullOrWhiteSpace(configured))
		{
			return new PackageSourceDecision(PackageSourceKind.Invalid, null,
				$"{PackageSourceVariable} is set but empty. Set it to the absolute directory that holds the packed .nupkg " +
				"files (dotnet pack CheatEngine.Client.slnx -c Release --no-build -o artifacts/nuget), or unset it locally.");
		}

		if (!Path.IsPathFullyQualified(configured))
		{
			return new PackageSourceDecision(PackageSourceKind.Invalid, null,
				$"{PackageSourceVariable} must be an absolute directory path, but is '{configured}'.");
		}

		return directoryExists(configured)
			? new PackageSourceDecision(PackageSourceKind.ConfiguredDirectory, Path.GetFullPath(configured), null)
			: new PackageSourceDecision(PackageSourceKind.Invalid, null,
				$"{PackageSourceVariable} points to a missing directory: '{configured}'.");
	}
}
