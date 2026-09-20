using Microsoft.Extensions.Logging;

namespace CheatEngine.Client.Hosting;

/// <summary>Source-generated lifecycle logging that intentionally excludes memory contents and Lua source.</summary>
internal static partial class ClientHostingLog
{
	[LoggerMessage(1, LogLevel.Debug, "Cheat Engine Client activation {Epoch} enabled.")]
	internal static partial void ActivationEnabled(ILogger logger, long epoch);

	[LoggerMessage(2, LogLevel.Warning, "Cheat Engine Client activation {Epoch} is rolling back after enable failed.")]
	internal static partial void ActivationRollingBack(ILogger logger, long epoch);

	[LoggerMessage(3, LogLevel.Debug, "Cheat Engine Client activation {Epoch} is disabling.")]
	internal static partial void ActivationDisabling(ILogger logger, long epoch);

	[LoggerMessage(4, LogLevel.Debug, "Cheat Engine Client activation {Epoch} disabled.")]
	internal static partial void ActivationDisabled(ILogger logger, long epoch);

	[LoggerMessage(5, LogLevel.Warning,
		"Cheat Engine Client activation {Epoch} completed cleanup with {FailureCount} callback failure(s).")]
	internal static partial void ActivationCleanupFailed(ILogger logger, long epoch, int failureCount);
}
