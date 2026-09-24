using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Runtime;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Source-generated Core diagnostic events (audit ch.24); event ids 1000–1999 of this assembly.</summary>
/// <remarks>
///     <para>
///         Redaction policy (audit Q46, A24-12 to A24-17): every parameter is an epoch, a count, a width, a duration, a
///         stable operation name, an enum value or a closed reason name. An event never carries an address, a value, a
///         symbol name, a module name, a path, a Lua script or its length unless unsafe Lua ran, an exception message or a
///         failure object.
///     </para>
///     <para>
///         Blocks: 1000–1099 runtime and capability gates, 1100–1199 target selection, 1200–1299 memory, 1300–1399
///         tables, 1400–1499 inspection, 1500–1599 pattern scans, 1600–1699 Lua, 1700–1799 Client-owned resource cleanup.
///         The ids are stable: an id is never reused for another event.
///     </para>
/// </remarks>
internal static partial class ClientCoreDiagnosticsLog
{
	[LoggerMessage(1000, LogLevel.Debug,
		"Runtime snapshot of activation {ActivationEpoch}: target {TargetArchitecture}, process pointer " +
		"{ProcessPointerBytes} byte(s), configured pointer {ConfiguredPointerBytes} byte(s) (0 = unknown), width " +
		"mismatch {PointerSizeMismatch}.")]
	internal static partial void RuntimeSnapshotCaptured(ILogger logger, long activationEpoch,
		CheatEngineArchitecture targetArchitecture, int processPointerBytes, int configuredPointerBytes,
		bool pointerSizeMismatch);

	[LoggerMessage(1001, LogLevel.Debug,
		"Capability {Capability} refused {Operation}: the {Gate} gate is {GateState}.")]
	internal static partial void CapabilityRefused(ILogger logger, string capability, string operation,
		ClientCapabilityEvidenceReasonCode gate, ClientCapabilityEvidenceState gateState);

	[LoggerMessage(1100, LogLevel.Debug,
		"Activation {ActivationEpoch} advanced the target selection to epoch {SelectionEpoch} during {Operation}: " +
		"{Reason}.")]
	internal static partial void TargetSelectionAdvanced(ILogger logger, long activationEpoch, long selectionEpoch,
		string operation, string reason);

	[LoggerMessage(1200, LogLevel.Information,
		"{Operation} was refused: Cheat Engine's configured pointer size ({ConfiguredPointerBytes} byte(s)) differs " +
		"from the target process width ({ProcessPointerBytes} byte(s)).")]
	internal static partial void PointerWidthMismatchRefused(ILogger logger, string operation, int processPointerBytes,
		int configuredPointerBytes);

	[LoggerMessage(1201, LogLevel.Debug,
		"{Operation} completed {Completed} of {Requested} operation(s); effect state {EffectState}.")]
	internal static partial void MemoryBatchCompleted(ILogger logger, string operation, int requested, int completed,
		string effectState);

	[LoggerMessage(1300, LogLevel.Debug,
		"Activation {ActivationEpoch} advanced the table generation to {TableGeneration} after a trusted table load " +
		"reached Cheat Engine.")]
	internal static partial void TableGenerationAdvanced(ILogger logger, long activationEpoch, long tableGeneration);

	[LoggerMessage(1301, LogLevel.Debug,
		"{Operation} refused a record identifier captured before table generation {TableGeneration}.")]
	internal static partial void StaleRecordIdentifierRefused(ILogger logger, string operation, long tableGeneration);

	[LoggerMessage(1302, LogLevel.Debug,
		"{Operation} did not apply the requested active state {RequestedState}: {Status}.")]
	internal static partial void RecordActivationNotApplied(ILogger logger, string operation, bool requestedState,
		string status);

	[LoggerMessage(1400, LogLevel.Debug, "{Operation} rejected the symbol registration: {Reason}.")]
	internal static partial void SymbolRegistrationRejected(ILogger logger, string operation, string reason);

	[LoggerMessage(1401, LogLevel.Debug, "A symbol lease release attempt ended with {ReleaseKind}.")]
	internal static partial void SymbolLeaseReleased(ILogger logger, SymbolLeaseReleaseKind releaseKind);

	[LoggerMessage(1500, LogLevel.Debug,
		"Pattern scan ({Scope}): {HostResultCount} host result(s), {MaterializedCount} materialized, truncated " +
		"{Truncated}; host scan {HostScanMilliseconds} ms, copy {CopyMilliseconds} ms.")]
	internal static partial void PatternScanCompleted(ILogger logger, PatternScanScope scope, long hostResultCount,
		int materializedCount, bool truncated, long hostScanMilliseconds, long copyMilliseconds);

	[LoggerMessage(1600, LogLevel.Debug,
		"{Operation} ended with {Outcome} in {ElapsedMilliseconds} ms (unsafe Lua length {ScriptLength}, 0 otherwise).")]
	internal static partial void LuaOperationCompleted(ILogger logger, string operation, string outcome,
		long elapsedMilliseconds, int scriptLength);

	[LoggerMessage(1700, LogLevel.Warning, "A Client-owned {ComponentType} failed to release with {ExceptionType}.")]
	internal static partial void CoreResourceCleanupFailed(ILogger logger, string componentType, string exceptionType);

	[LoggerMessage(1701, LogLevel.Debug, "{Operation} ended with {ReleaseKind} (host effect {HostEffect}).")]
	internal static partial void LeaseReleased(ILogger logger, string operation, LeaseReleaseKind releaseKind,
		CheatEngineHostEffect hostEffect);
}
