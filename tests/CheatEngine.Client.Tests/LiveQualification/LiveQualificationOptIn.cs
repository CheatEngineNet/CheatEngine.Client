using System.Runtime.Versioning;

using CheatEngine.Client.Tests.Infrastructure;
using CheatEngine.Client.Tests.Packaging;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>What an authorized live qualification run consumes.</summary>
/// <param name="CheatEngineDirectory">The Cheat Engine installation the sandbox is copied from; never written.</param>
/// <param name="RunRoot">The folder that receives one directory per run, outside the repository.</param>
/// <param name="PackageSource">The directory of the packed Client packages every plugin is built from.</param>
internal sealed record LiveQualificationInputs(string CheatEngineDirectory, string RunRoot, string PackageSource);

/// <summary>The outcome of <see cref="LiveQualificationOptIn.Evaluate" />: the inputs, or why the run is refused.</summary>
internal sealed record LiveQualificationDecision(LiveQualificationInputs? Inputs, string? Refusal)
{
	/// <summary>Whether the run is authorized.</summary>
	internal bool IsAuthorized => Inputs is not null;
}

/// <summary>
///     The opt-in of the live qualification tests. They start a sandboxed Cheat Engine, so they run only on a maintainer
///     workstation that affirms the exact phrase and names the packed Client packages, and never in CI. Without the
///     opt-in they fail with the instructions below; they are never skipped (<c>--fail-skips on</c>), and CI excludes
///     them by trait instead.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class LiveQualificationOptIn
{
	/// <summary>The opt-in variable.</summary>
	internal const string OptInVariable = "CHEATENGINE_CLIENT_LIVE_QUALIFICATION";

	/// <summary>The exact phrase the opt-in variable must hold, the same phrase the harness gate requires.</summary>
	internal const string Acknowledgement = "I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET";

	/// <summary>Optional: the Cheat Engine installation to copy (read only).</summary>
	internal const string CheatEngineDirectoryVariable = "CHEATENGINE_CLIENT_LIVE_QUALIFICATION_CE_DIRECTORY";

	/// <summary>Optional: the folder that receives the run directories.</summary>
	internal const string RunRootVariable = "CHEATENGINE_CLIENT_LIVE_QUALIFICATION_RUN_ROOT";

	/// <summary>The default Cheat Engine installation.</summary>
	internal const string DefaultCheatEngineDirectory = "C:/Program Files/Cheat Engine";

	/// <summary>The default run root, relative to <c>%LOCALAPPDATA%</c>.</summary>
	internal const string DefaultRunRootBelowLocalApplicationData = "CheatEngine.Client.LiveQualification/runs";

	/// <summary>The commands of a local S0 run, from the repository root, in PowerShell. The README repeats them.</summary>
	internal static readonly string[] LocalCommand =
	[
		"dotnet build CheatEngine.Client.slnx -c Release --no-restore",
		"dotnet pack CheatEngine.Client.slnx -c Release --no-build -o artifacts/nuget",
		"$env:CHEATENGINE_CLIENT_PACKAGE_SOURCE = (Resolve-Path artifacts/nuget).Path",
		$"$env:{OptInVariable} = '{Acknowledgement}'",
		"dotnet test --project tests/CheatEngine.Client.Tests/CheatEngine.Client.Tests.csproj -c Release --no-build --filter-trait Session=S0",
		$"Remove-Item Env:{OptInVariable}"
	];

	/// <summary>How to run the live qualification tests, appended to every refusal.</summary>
	internal static string Instructions
	{
		get;
	} = string.Join(Environment.NewLine,
	[
		"Live qualification copies Cheat Engine 7.7.0.10621 x64 into a sandbox and drives it against a disposable gtutorial",
		"target. It runs only on a maintainer workstation, never in CI, and only when explicitly authorized:",
		"  1. Close Cheat Engine, every gtutorial and DebugView.",
		"  2. From the repository root, in PowerShell:",
		.. LocalCommand.Select(static line => "       " + line),
		$"  Optional: {CheatEngineDirectoryVariable} (default {DefaultCheatEngineDirectory}, read only) and",
		$"  {RunRootVariable} (default %LOCALAPPDATA%/{DefaultRunRootBelowLocalApplicationData}, outside the repository).",
		"  See tests/CheatEngine.Client.Tests/README.md, section 'Live qualification'."
	]);

	/// <summary>Reads the process environment.</summary>
	internal static LiveQualificationDecision ResolveFromEnvironment()
	{
		return Evaluate(Environment.GetEnvironmentVariable,
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), RepositoryLayout.Root,
			Directory.Exists);
	}

	/// <summary>Decides from explicit inputs, so every refusal is testable without an environment.</summary>
	internal static LiveQualificationDecision Evaluate(Func<string, string?> variables, string localApplicationData,
		string repositoryRoot, Func<string, bool> directoryExists)
	{
		ArgumentNullException.ThrowIfNull(variables);
		ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
		ArgumentNullException.ThrowIfNull(directoryExists);

		if (string.Equals(variables("CI"), "true", StringComparison.OrdinalIgnoreCase))
		{
			return Refuse("CI=true: live qualification never runs in continuous integration. Both CI test legs exclude it " +
						  "with --filter-not-trait Category=LiveQualification.");
		}

		if (!string.Equals(variables(OptInVariable), Acknowledgement, StringComparison.Ordinal))
		{
			return Refuse($"{OptInVariable} does not hold the exact phrase {Acknowledgement}, so this run is not authorized.");
		}

		string? packageSource = variables(PackageSourceResolution.PackageSourceVariable);
		if (string.IsNullOrWhiteSpace(packageSource) || !Path.IsPathFullyQualified(packageSource) ||
			!directoryExists(packageSource))
		{
			return Refuse($"{PackageSourceResolution.PackageSourceVariable} must name the existing absolute directory of the " +
						  "packed Client packages: every live plugin is built from them, never from the workspace.");
		}

		string cheatEngine = variables(CheatEngineDirectoryVariable) is { Length: > 0 } configured
			? configured
			: DefaultCheatEngineDirectory;
		if (!Path.IsPathFullyQualified(cheatEngine) || !directoryExists(cheatEngine))
		{
			return Refuse($"The Cheat Engine installation '{cheatEngine}' ({CheatEngineDirectoryVariable}) is not an existing " +
						  "absolute directory.");
		}

		string? runRoot = variables(RunRootVariable) is { Length: > 0 } root
			? root
			: string.IsNullOrWhiteSpace(localApplicationData)
				? null
				: Path.Combine(localApplicationData, DefaultRunRootBelowLocalApplicationData);
		if (runRoot is null || !Path.IsPathFullyQualified(runRoot))
		{
			return Refuse($"The run root '{runRoot}' ({RunRootVariable}) must be an absolute directory.");
		}

		string fullCheatEngine = Path.GetFullPath(cheatEngine);
		string fullRunRoot = Path.GetFullPath(runRoot);
		if (IsSameOrBelow(fullRunRoot, Path.GetFullPath(repositoryRoot)) || IsSameOrBelow(fullRunRoot, fullCheatEngine) ||
			IsSameOrBelow(fullCheatEngine, fullRunRoot))
		{
			return Refuse($"The run root '{fullRunRoot}' must lie outside the repository and apart from the Cheat Engine " +
						  "installation.");
		}

		return new LiveQualificationDecision(
			new LiveQualificationInputs(fullCheatEngine, fullRunRoot, Path.GetFullPath(packageSource)), null);
	}

	/// <summary>Whether <paramref name="path" /> is <paramref name="directory" /> or lies below it.</summary>
	internal static bool IsSameOrBelow(string path, string directory)
	{
		string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
		string normalizedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
		return string.Equals(normalizedPath, normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
			   normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private static LiveQualificationDecision Refuse(string reason)
	{
		return new LiveQualificationDecision(null, reason + Environment.NewLine + Environment.NewLine + Instructions);
	}
}
