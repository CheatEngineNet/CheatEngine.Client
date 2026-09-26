using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>How a Cheat Engine session process ended.</summary>
internal enum HostExit
{
	/// <summary>The process exited on its own (the driver called <c>closeCE()</c>, or Cheat Engine stopped).</summary>
	Exited,

	/// <summary>The session timeout elapsed: the runner closed, then killed the process.</summary>
	TimedOut,

	/// <summary>The test was cancelled: the runner closed, then killed the process.</summary>
	Cancelled
}

/// <summary>
///     The environment a sandboxed Cheat Engine receives: the test host's variables minus every .NET host, MSBuild and
///     test platform setting, and minus any inherited live-probe or Client input, plus the session's own inputs only.
/// </summary>
internal static class CheatEngineEnvironment
{
	/// <summary>Makes the SDK write its identification line when a plugin is enabled.</summary>
	internal const string IdentifyOnEnableVariable = "CHEATENGINE_SDK_IDENTIFY_ON_ENABLE";

	/// <summary>Variable name prefixes removed from the inherited environment (ordinal, ignoring case).</summary>
	internal static readonly string[] RemovedPrefixes =
		["DOTNET_", "MSBUILD", "TESTINGPLATFORM", "VSTEST", "CE_SDK_LIVE_PROBE_", "CECLIENT_QUALIFICATION_", "CHEATENGINE_"];

	/// <summary>The prefixes a session input may use; <see cref="IdentifyOnEnableVariable" /> is added by the runner.</summary>
	internal static readonly string[] SessionInputPrefixes = ["CE_SDK_LIVE_PROBE_", "CECLIENT_QUALIFICATION_"];

	/// <summary>
	///     Removes the inherited variables of <see cref="RemovedPrefixes" /> from <paramref name="environment" /> (keeping
	///     <c>DOTNET_ROOT*</c> when <paramref name="keepDotNetRoot" /> is set), then adds the session inputs and
	///     <see cref="IdentifyOnEnableVariable" /><c>=1</c>.
	/// </summary>
	internal static void Apply(IDictionary<string, string?> environment, IReadOnlyDictionary<string, string> sessionInputs,
		bool keepDotNetRoot)
	{
		ArgumentNullException.ThrowIfNull(environment);
		ArgumentNullException.ThrowIfNull(sessionInputs);

		foreach (string name in environment.Keys.ToArray())
		{
			bool removed = RemovedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
			bool dotNetRoot = name.StartsWith("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase);
			if (removed && !(keepDotNetRoot && dotNetRoot))
			{
				environment.Remove(name);
			}
		}

		foreach ((string name, string value) in sessionInputs)
		{
			if (!SessionInputPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
			{
				throw new ArgumentException($"'{name}' is not a session input: only {string.Join(", ", SessionInputPrefixes)} variables are.",
					nameof(sessionInputs));
			}

			environment[name] = value;
		}

		environment[IdentifyOnEnableVariable] = "1";
	}
}

/// <summary>
///     Keeps a live session alone on the workstation: it refuses to start while a Cheat Engine, a gtutorial or a debug
///     output listener (DebugView) runs, starts the sandboxed <c>cheatengine-x86_64.exe</c> directly (never the launcher)
///     with the <see cref="CheatEngineEnvironment" />, and closes, then kills, a session that exceeds its timeout.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HostProcessGuard
{
	/// <summary>The longest a session may run.</summary>
	internal static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(10);

	private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(10);

	/// <summary>
	///     Whether a process name belongs to Cheat Engine (<c>cheatengine-x86_64</c>, <c>cheatengine-i386</c>, ...), its
	///     <c>Cheat Engine</c> launcher or a gtutorial target. The test host, <c>CheatEngine.Client.Tests</c>, is not one.
	/// </summary>
	internal static bool IsHostOrTargetName(string processName)
	{
		ArgumentNullException.ThrowIfNull(processName);
		return string.Equals(processName, "cheatengine", StringComparison.OrdinalIgnoreCase) ||
			   processName.StartsWith("cheatengine-", StringComparison.OrdinalIgnoreCase) ||
			   processName.StartsWith("gtutorial", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(processName, "Cheat Engine", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>What prevents a session from starting now; empty when the workstation is quiet.</summary>
	internal static IReadOnlyList<string> FindBlockers()
	{
		List<string> blockers = [];
		foreach (Process process in Process.GetProcesses())
		{
			using (process)
			{
				if (process.Id != Environment.ProcessId && IsHostOrTargetName(process.ProcessName))
				{
					blockers.Add(string.Create(CultureInfo.InvariantCulture, $"process '{process.ProcessName}' (id {process.Id}) is running"));
				}
			}
		}

		if (DebugOutputListenerExists())
		{
			blockers.Add($"another debug output listener (for example DebugView) owns {DebugOutputCapture.BufferName}");
		}

		return blockers;
	}

	/// <summary>Whether a debug output listener already owns the DBWIN objects.</summary>
	internal static bool DebugOutputListenerExists()
	{
		try
		{
			using MemoryMappedFile buffer = MemoryMappedFile.OpenExisting(DebugOutputCapture.BufferName, MemoryMappedFileRights.Read);
			return true;
		}
		catch (UnauthorizedAccessException)
		{
			return true;
		}
		catch (FileNotFoundException)
		{
			if (!EventWaitHandle.TryOpenExisting(DebugOutputCapture.BufferReadyName, out EventWaitHandle? ready))
			{
				return false;
			}

			ready.Dispose();
			return true;
		}
	}

	/// <summary>Starts the sandboxed host executable directly, in its own folder, with the sanitized environment.</summary>
	internal static Process StartCheatEngine(string executable, IReadOnlyDictionary<string, string> sessionInputs,
		bool keepDotNetRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(executable);
		ProcessStartInfo startInfo = new(executable)
		{
			WorkingDirectory = Path.GetDirectoryName(executable)!,
			UseShellExecute = false
		};
		CheatEngineEnvironment.Apply(startInfo.Environment, sessionInputs, keepDotNetRoot);
		return Process.Start(startInfo) ?? throw new InvalidOperationException($"'{executable}' did not start.");
	}

	/// <summary>Waits for the session to exit; past the timeout or on cancellation, closes, then kills it.</summary>
	internal static async Task<HostExit> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(process);
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(timeout);
		try
		{
			await process.WaitForExitAsync(deadline.Token);
			return HostExit.Exited;
		}
		catch (OperationCanceledException) when (deadline.IsCancellationRequested)
		{
			Stop(process);
			return cancellationToken.IsCancellationRequested ? HostExit.Cancelled : HostExit.TimedOut;
		}
	}

	/// <summary>Closes the main window, then kills the process tree if it is still running after a grace period.</summary>
	internal static void Stop(Process process)
	{
		ArgumentNullException.ThrowIfNull(process);
		try
		{
			if (process.HasExited)
			{
				return;
			}

			process.CloseMainWindow();
			if (!process.WaitForExit(CloseGrace))
			{
				process.Kill(true);
				process.WaitForExit(CloseGrace);
			}
		}
		catch (InvalidOperationException)
		{
			// The process exited between the checks, or was never started.
		}
	}
}
