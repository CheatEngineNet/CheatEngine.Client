using System.Diagnostics;
using System.Runtime.Versioning;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>A disposable gtutorial target started from the sandbox; disposing it closes, then kills, the process.</summary>
[SupportedOSPlatform("windows")]
internal sealed class LaunchedTarget : IDisposable
{
	private readonly Process _process;

	internal LaunchedTarget(Process process, string imageName, string imageSha256, IReadOnlyList<string> modulesAtStart)
	{
		_process = process;
		ProcessId = process.Id;
		StartedUtc = process.StartTime.ToUniversalTime();
		ImageName = imageName;
		ImageSha256 = imageSha256;
		ModulesAtStart = modulesAtStart;
	}

	/// <summary>The target process id, which the authorization manifest names.</summary>
	internal int ProcessId
	{
		get;
	}

	/// <summary>When the target started, so a reused process id is detectable.</summary>
	internal DateTime StartedUtc
	{
		get;
	}

	/// <summary>The file name of the target image.</summary>
	internal string ImageName
	{
		get;
	}

	/// <summary>The upper-case SHA-256 of the target image, verified before the start.</summary>
	internal string ImageSha256
	{
		get;
	}

	/// <summary>The lower-case module names loaded right after the start.</summary>
	internal IReadOnlyList<string> ModulesAtStart
	{
		get;
	}

	/// <summary>Whether the target is still running.</summary>
	internal bool IsRunning => !_process.HasExited;

	/// <summary>The lower-case module names loaded now (Q45: compare with <see cref="ModulesAtStart" />).</summary>
	internal IReadOnlyList<string> SnapshotModules()
	{
		return TargetLauncher.ModuleNames(_process);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		HostProcessGuard.Stop(_process);
		_process.Dispose();
	}
}

/// <summary>
///     Starts the sandboxed gtutorial targets and records their identity (process id, start time, image SHA-256) and their
///     modules, the Q45 evidence that the Client injected nothing: no speedhack, allochook, luaclient, vehdebug or dbk
///     module may appear in the target.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class TargetLauncher
{
	/// <summary>Module name prefixes that must never appear in a target (Q45).</summary>
	internal static readonly string[] ForbiddenModulePrefixes = ["speedhack", "allochook", "luaclient", "vehdebug", "dbk"];

	private static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(30);

	/// <summary>Starts <paramref name="executable" /> after checking its SHA-256, and waits until it is idle.</summary>
	internal static LaunchedTarget Start(string executable, string expectedSha256)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(executable);
		ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
		string sha256 = CheatEngineInstallation.Sha256(executable);
		if (!string.Equals(sha256, expectedSha256, StringComparison.Ordinal))
		{
			throw new InvalidOperationException($"'{Path.GetFileName(executable)}' has SHA-256 {sha256}, expected {expectedSha256}.");
		}

		ProcessStartInfo startInfo = new(executable)
		{
			WorkingDirectory = Path.GetDirectoryName(executable)!,
			UseShellExecute = false
		};
		CheatEngineEnvironment.Apply(startInfo.Environment, new Dictionary<string, string>(StringComparer.Ordinal), false);
		startInfo.Environment.Remove(CheatEngineEnvironment.IdentifyOnEnableVariable);
		Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"'{executable}' did not start.");
		try
		{
			process.WaitForInputIdle(StartupWait);
			return new LaunchedTarget(process, Path.GetFileName(executable), sha256, ModuleNames(process));
		}
		catch
		{
			HostProcessGuard.Stop(process);
			process.Dispose();
			throw;
		}
	}

	/// <summary>The forbidden modules among <paramref name="moduleNames" />, lower-case and ordinally sorted.</summary>
	internal static IReadOnlyList<string> FindForbiddenModules(IEnumerable<string> moduleNames)
	{
		ArgumentNullException.ThrowIfNull(moduleNames);
		return moduleNames
			.Select(static name => name.ToLowerInvariant())
			.Where(static name => ForbiddenModulePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();
	}

	internal static IReadOnlyList<string> ModuleNames(Process process)
	{
		List<string> names = [];
		foreach (ProcessModule module in process.Modules)
		{
			using (module)
			{
				names.Add(module.ModuleName.ToLowerInvariant());
			}
		}

		names.Sort(StringComparer.Ordinal);
		return names;
	}
}
