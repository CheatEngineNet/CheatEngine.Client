using CheatEngine.Client.Runtime;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Client.Hosting;

/// <summary>Source-generated lifecycle logging that intentionally excludes memory contents and Lua source.</summary>
/// <remarks>
///     Redaction policy (audit Q46): events carry only the activation epoch, stable stage names, counts, and exception
///     <em>type</em> names. They never carry exception messages, addresses, values, symbol expressions, file paths, or Lua
///     text, which are user data. Event ids 1–5 are frozen; 6–19 are reserved for Hosting cleanup and redaction events;
///     20–49 are activation identification events.
/// </remarks>
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
		"Cheat Engine Client activation {Epoch} completed cleanup with {FailureCount} failure(s).")]
	internal static partial void ActivationCleanupFailed(ILogger logger, long epoch, int failureCount);

	/// <summary>One cleanup stage failed; only its stable name and the exception type name are recorded.</summary>
	[LoggerMessage(6, LogLevel.Warning,
		"Cheat Engine Client activation {Epoch} cleanup stage {Stage} failed with {ExceptionType}.")]
	internal static partial void ActivationCleanupStageFailed(ILogger logger, long epoch, string stage,
		string exceptionType);

	/// <summary>Every cleanup stage was attempted; counts only.</summary>
	[LoggerMessage(7, LogLevel.Debug,
		"Cheat Engine Client activation {Epoch} attempted {AttemptedStages} cleanup stage(s); {FailedStages} failed.")]
	internal static partial void ActivationCleanupCompleted(ILogger logger, long epoch, int attemptedStages,
		int failedStages);

	/// <summary>
	///     Identifies the Client build, the consumed and loaded CheatEngine.SDK and the supported host profile once per
	///     enable (audit A24-12). Built from assembly metadata only: no path, no file read and no Lua call. The identity
	///     label says whether the loaded SDK is the reviewed package, another release the package gate accepts, or one
	///     it does not accept.
	/// </summary>
	[LoggerMessage(20, LogLevel.Information,
		"Cheat Engine Client activation {Epoch} enables {PluginType} with CheatEngine.Client {ClientVersion}; consumed " +
		"CheatEngine.SDK {ConsumedSdkVersion} (NuGet content hash {ConsumedSdkContentHash}), loaded " +
		"CheatEngine.SDK.Engine {LoadedSdkVersion} ({LoadedSdkIdentity}), package evidence {PackageEvidence}; " +
		"supported host profile {SupportedHost}.")]
	internal static partial void ActivationIdentified(ILogger logger, long epoch, string pluginType,
		string clientVersion, string consumedSdkVersion, string consumedSdkContentHash, string loadedSdkVersion,
		string loadedSdkIdentity, ClientCapabilityEvidenceState packageEvidence, string supportedHost);
}
