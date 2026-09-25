using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Scanning.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Maps the outcomes of CheatEngine.SDK 2.0.0 <c>AobScanner.TryScanOutcome</c> (with its target context) and
///     <c>AobScanner.TryScanWithinBounds</c> to the Client vocabulary, value by value (audit F06, F07, CRIT-03).
/// </summary>
/// <remarks>
///     <para>Global route, outcomes that hand out no usable result list:</para>
///     <list type="table">
///         <listheader>
///             <term>SDK outcome</term>
///             <description>Client failure kind and host effect</description>
///         </listheader>
///         <item>
///             <term><c>NoResult</c></term>
///             <description>
///                 <c>IndeterminateHostResult</c>, <c>Completed</c>, with <see cref="NoResultMessage" />: on Cheat Engine
///                 7.7 zero matches and host failures both return <c>nil</c>, so this is never <c>NotFound</c>.
///             </description>
///         </item>
///         <item>
///             <term><c>GlobalUnavailable</c></term>
///             <description><c>CapabilityUnavailable</c>, <c>NotStarted</c>: <c>AOBScan</c> was not called.</description>
///         </item>
///         <item><term><c>ProtectedLuaFailure</c></term><description><c>LuaError</c>, <c>Unknown</c>, naming the Lua status</description></item>
///         <item><term><c>InvalidResult</c></term><description><c>InvalidHostResult</c>, <c>Completed</c></description></item>
///         <item>
///             <term><c>ResultListCountUnavailable</c></term>
///             <description><c>InvalidHostResult</c>, <c>Completed</c>: CheatEngine.SDK released the list itself.</description>
///         </item>
///         <item>
///             <term><c>Matches</c> or <c>NoMatches</c> without a list</term>
///             <description><c>InvalidHostResult</c>, <c>Completed</c>: a broken SDK contract, never a success.</description>
///         </item>
///         <item>
///             <term><c>Unknown</c> or undefined</term>
///             <description><c>IndeterminateHostResult</c>, <c>Unknown</c></description>
///         </item>
///     </list>
///     <para>
///         <c>Matches</c> and <c>NoMatches</c> with a list are copied; <c>NoMatches</c> is the factual empty success.
///         Before anything is copied, and for <c>NoResult</c>, the target context decides whether the answer can be
///         attributed to one target (<see cref="JudgeTarget" />): a target that changed during the scan is
///         <see cref="CheatEngineFailureKind.TargetChanged" />, and a target whose identity was lost or gained during the
///         scan is <see cref="CheatEngineFailureKind.TargetIdentityUnavailable" />, both with
///         <see cref="CheatEngineHostEffect.Completed" />; the returned addresses are discarded. Messages name the
///         category only, never an address, a process or a pattern. The mapping-totality tests fail when the consumed SDK
///         adds a value.
///     </para>
///     <para>Bounded route (<see cref="ClassifyBounded" />):</para>
///     <list type="table">
///         <listheader>
///             <term>SDK outcome</term>
///             <description>Client result</description>
///         </listheader>
///         <item><term><c>Matches</c></term><description>The in-bounds addresses are published.</description></item>
///         <item>
///             <term><c>NoMatches</c></term>
///             <description>
///                 The factual empty success when Cheat Engine's error text was read; otherwise
///                 <c>IndeterminateHostResult</c>, <c>Completed</c>: a host error cannot be excluded.
///             </description>
///         </item>
///         <item>
///             <term><c>HostReportedError</c></term>
///             <description><c>OperationRejected</c>, <c>Completed</c>, carrying the SDK's bounded, unparsed text.</description>
///         </item>
///         <item><term><c>InvalidBounds</c></term><description><c>OperationRejected</c>, <c>NotStarted</c></description></item>
///         <item>
///             <term><c>SessionCreationFailed</c>, <c>TargetIdentityUnavailable</c></term>
///             <description>
///                 Fall back to the global route with managed post-filters; a creation whose rollback Cheat Engine did not
///                 confirm, or whose creation status this Client does not recognize, is <c>IndeterminateHostResult</c>,
///                 <c>CleanupUnconfirmed</c> instead (<see cref="ClassifyCreationFailure" />), as a value-scan session
///                 creation is.
///             </description>
///         </item>
///         <item><term><c>TargetChanged</c></term><description><c>TargetChanged</c></description></item>
///         <item><term><c>RuntimeInvalidated</c></term><description><c>RuntimeChanged</c></description></item>
///         <item><term><c>ScanFailed</c></term><description><c>LuaError</c>, naming the Lua status</description></item>
///         <item><term><c>InvalidResult</c></term><description><c>InvalidHostResult</c></description></item>
///         <item>
///             <term><c>Cancelled</c></term>
///             <description>
///                 <c>Cancelled</c>: <c>NotStarted</c> before the scan completed (no host scan time), <c>Completed</c>
///                 after it.
///             </description>
///         </item>
///         <item>
///             <term><c>WaitTimedOut</c>, <c>Unknown</c> or undefined</term>
///             <description><c>IndeterminateHostResult</c>, <c>Unknown</c>: this Client never sets a call deadline.</description>
///         </item>
///     </list>
///     <para>
///         The host effect of <c>TargetChanged</c>, <c>RuntimeInvalidated</c>, <c>ScanFailed</c> and
///         <c>InvalidResult</c> is <c>Completed</c> when the scan had completed (the SDK reported a host scan time) and
///         <c>Unknown</c> otherwise. The session release is checked separately
///         (<see cref="IsSessionReleaseConfirmed" />): an unconfirmed release turns any of these results into
///         <c>CleanupUnconfirmed</c>.
///     </para>
/// </remarks>
internal static class AobScanMapping
{
	/// <summary>The exact message of the global route's <c>NoResult</c> outcome.</summary>
	internal const string NoResultMessage =
		"CE AOBScan returned nil: on CE 7.7 zero matches and host failures share this shape";

	/// <summary>The message of a result list the scanner cannot use.</summary>
	internal const string InvalidListMessage = "Cheat Engine returned an invalid AOB result list.";

	/// <summary>Returns the failure of a global outcome that hands out no usable result list.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="host">The copied SDK outcome.</param>
	/// <returns>The classified failure; never a success.</returns>
	internal static CheatEngineFailure ToFailure(string operation, AobHostOutcome host)
	{
		return host.Kind switch
		{
			AobScanOutcomeKind.NoResult => new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult,
				operation, NoResultMessage, null, CheatEngineHostEffect.Completed),
			AobScanOutcomeKind.GlobalUnavailable => new CheatEngineFailure(
				CheatEngineFailureKind.CapabilityUnavailable, operation,
				"Cheat Engine's AOBScan global is absent or not callable.", null, CheatEngineHostEffect.NotStarted),
			AobScanOutcomeKind.ProtectedLuaFailure => new CheatEngineFailure(CheatEngineFailureKind.LuaError,
				operation, $"The protected AOBScan call failed with Lua status {host.LuaStatus}.", null,
				CheatEngineHostEffect.Unknown),
			AobScanOutcomeKind.InvalidResult => new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult,
				operation, "Cheat Engine's AOBScan returned a value that is not a result list.", null,
				CheatEngineHostEffect.Completed),
			AobScanOutcomeKind.ResultListCountUnavailable => new CheatEngineFailure(
				CheatEngineFailureKind.InvalidHostResult, operation,
				"Cheat Engine returned an AOB result list whose count could not be read; CheatEngine.SDK released it.",
				null, CheatEngineHostEffect.Completed),
			AobScanOutcomeKind.Matches or AobScanOutcomeKind.NoMatches => new CheatEngineFailure(
				CheatEngineFailureKind.InvalidHostResult, operation, InvalidListMessage, null,
				CheatEngineHostEffect.Completed),
			AobScanOutcomeKind.Unknown => Indeterminate(operation),
			_ => Indeterminate(operation)
		};
	}

	/// <summary>Decides whether the answer of one global scan can be attributed to one target.</summary>
	/// <param name="before">The selection observed immediately before the call.</param>
	/// <param name="after">The selection observed immediately after the call returned.</param>
	/// <returns>
	///     <see cref="AobTargetVerdict.Verified" /> when both observations are qualified and denote the same incarnation;
	///     <see cref="AobTargetVerdict.Changed" /> when they denote different incarnations or different process
	///     identifiers; <see cref="AobTargetVerdict.IdentityUnavailable" /> when only one of them is qualified, or when
	///     neither is and they differ otherwise; <see cref="AobTargetVerdict.Unverified" /> when neither is qualified and
	///     both report the same selection (a remote, file-as-process or unobservable target that did not visibly change).
	/// </returns>
	internal static AobTargetVerdict JudgeTarget(TargetSelectionFacts before, TargetSelectionFacts after)
	{
		if (before.IsQualified && after.IsQualified)
		{
			return before.Incarnation == after.Incarnation ? AobTargetVerdict.Verified : AobTargetVerdict.Changed;
		}

		if (before.SelectedProcessId is { } first && after.SelectedProcessId is { } second && first != second)
		{
			return AobTargetVerdict.Changed;
		}

		return before.IsQualified || after.IsQualified || before != after
			? AobTargetVerdict.IdentityUnavailable
			: AobTargetVerdict.Unverified;
	}

	/// <summary>Returns the failure that discards the answer of a scan whose target cannot be attributed.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="verdict">The target verdict of the scan.</param>
	/// <param name="failure">The failure when the answer is discarded.</param>
	/// <returns><see langword="true" /> when the verdict discards the answer.</returns>
	internal static bool TryGetTargetFailure(string operation, AobTargetVerdict verdict, out CheatEngineFailure failure)
	{
		switch (verdict)
		{
			case AobTargetVerdict.Changed:
				failure = new CheatEngineFailure(CheatEngineFailureKind.TargetChanged, operation,
					"Cheat Engine's selected target changed during the AOB scan; its answer was discarded.", null,
					CheatEngineHostEffect.Completed);
				return true;
			case AobTargetVerdict.IdentityUnavailable:
				failure = new CheatEngineFailure(CheatEngineFailureKind.TargetIdentityUnavailable, operation,
					"The selected target could not be identified the same way before and after the AOB scan; its " +
					"answer was discarded.", null, CheatEngineHostEffect.Completed);
				return true;
			default:
				failure = default;
				return false;
		}
	}

	/// <summary>Decides what the scanner does with a bounded result, before its session release is checked.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="result">The copied SDK result.</param>
	/// <param name="failure">
	///     The failure for <see cref="AobBoundedDisposition.Fail" />, and the reason of a
	///     <see cref="AobBoundedDisposition.FallBack" /> (reported only when the fallback cannot run); default otherwise.
	/// </param>
	/// <returns>Whether to publish the addresses, fall back to the global route, or fail.</returns>
	internal static AobBoundedDisposition ClassifyBounded(string operation, in AobBoundedHostResult result,
		out CheatEngineFailure failure)
	{
		failure = default;
		switch (result.Kind)
		{
			case AobBoundedScanOutcomeKind.Matches:
				return AobBoundedDisposition.Publish;
			case AobBoundedScanOutcomeKind.NoMatches when !result.IsHostErrorTextUnreadable:
				return AobBoundedDisposition.Publish;
			case AobBoundedScanOutcomeKind.NoMatches:
				failure = new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, operation,
					"The bounded AOB scan found no in-bounds match, but Cheat Engine's error text could not be read, " +
					"so a host error cannot be excluded.", null, CheatEngineHostEffect.Completed);
				return AobBoundedDisposition.Fail;
			case AobBoundedScanOutcomeKind.HostReportedError:
				string suffix = result.IsHostErrorTextTruncated ? " (truncated)" : string.Empty;
				failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
					$"Cheat Engine reported an error for the bounded AOB scan: {result.HostErrorText}{suffix}", null,
					CheatEngineHostEffect.Completed);
				return AobBoundedDisposition.Fail;
			case AobBoundedScanOutcomeKind.InvalidBounds:
				failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
					"CheatEngine.SDK refused the bounded AOB scan before any Cheat Engine call: its bounds are empty.",
					null, CheatEngineHostEffect.NotStarted);
				return AobBoundedDisposition.Fail;
			case AobBoundedScanOutcomeKind.SessionCreationFailed:
				return ClassifyCreationFailure(operation, result.CreationStatus, out failure);
			case AobBoundedScanOutcomeKind.TargetIdentityUnavailable:
				failure = new CheatEngineFailure(CheatEngineFailureKind.TargetIdentityUnavailable, operation,
					"The selected target could not be qualified during the bounded AOB scan; nothing was published.",
					null, ScanEffect(result));
				return AobBoundedDisposition.FallBack;
			case AobBoundedScanOutcomeKind.TargetChanged:
				failure = new CheatEngineFailure(CheatEngineFailureKind.TargetChanged, operation,
					"Cheat Engine's selected target changed during the bounded AOB scan; nothing was published.", null,
					ScanEffect(result));
				return AobBoundedDisposition.Fail;
			case AobBoundedScanOutcomeKind.RuntimeInvalidated:
				failure = new CheatEngineFailure(CheatEngineFailureKind.RuntimeChanged, operation,
					"The Lua runtime changed during the bounded AOB scan; nothing was published.", null,
					ScanEffect(result));
				return AobBoundedDisposition.Fail;
			case AobBoundedScanOutcomeKind.ScanFailed:
				failure = new CheatEngineFailure(CheatEngineFailureKind.LuaError, operation,
					$"A protected call of the bounded AOB scan failed with Lua status {result.LuaStatus}.", null,
					ScanEffect(result));
				return AobBoundedDisposition.Fail;
			case AobBoundedScanOutcomeKind.InvalidResult:
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, operation,
					"Cheat Engine returned a malformed wait result, count or row address for the bounded AOB scan.",
					null, ScanEffect(result));
				return AobBoundedDisposition.Fail;
			case AobBoundedScanOutcomeKind.Cancelled:
				failure = result.HostScanElapsed > TimeSpan.Zero
					? CancellationMapping.AfterNativeCall(operation,
						"The bounded AOB scan was cancelled after Cheat Engine completed it; nothing was published.")
					: CancellationMapping.BeforeNativeCall(operation,
						"The bounded AOB scan was cancelled before Cheat Engine started it.");
				return AobBoundedDisposition.Fail;
			case AobBoundedScanOutcomeKind.WaitTimedOut:
				failure = new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, operation,
					"The bounded AOB scan reported a call deadline, which this Client never sets.", null,
					CheatEngineHostEffect.Unknown);
				return AobBoundedDisposition.Fail;
			default:
				failure = new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, operation,
					"CheatEngine.SDK reported a bounded AOB outcome this Client version does not recognize.", null,
					CheatEngineHostEffect.Unknown);
				return AobBoundedDisposition.Fail;
		}
	}

	/// <summary>Decides what a bounded scan whose session could not be created means, by creation status.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="status">The SDK's session creation status.</param>
	/// <param name="failure">The failure, or the reason of the fallback (reported only when it cannot run).</param>
	/// <returns>
	///     <see cref="AobBoundedDisposition.FallBack" /> for every status after which CheatEngine.SDK holds no MemScan
	///     object (an absent or failing factory, an absent, invalid or aliased result, an unqualified target);
	///     <see cref="AobBoundedDisposition.Fail" /> otherwise.
	/// </returns>
	/// <remarks>
	///     <c>RollbackUnconfirmed</c> is <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> with
	///     <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />: a MemScan object Cheat Engine did not destroy is never
	///     hidden behind a second scan. <c>Unknown</c>, <c>Success</c> (a contradiction with a failed creation) and a value
	///     this Client version does not recognize fail closed the same way, because nothing proves that no object remains.
	///     The value-scan sessions classify the same statuses the same way (<c>ValueScanMapping</c>): an unconfirmed host
	///     rollback is an indeterminate host result, never a Client state
	///     (<see cref="CheatEngineFailureKind.InvalidState" />).
	/// </remarks>
	internal static AobBoundedDisposition ClassifyCreationFailure(string operation, MemoryScanCreationStatus status,
		out CheatEngineFailure failure)
	{
		switch (status)
		{
			case MemoryScanCreationStatus.GlobalUnavailable:
			case MemoryScanCreationStatus.LuaFailure:
			case MemoryScanCreationStatus.NoScannerResult:
			case MemoryScanCreationStatus.InvalidScannerResult:
			case MemoryScanCreationStatus.NoFoundListResult:
			case MemoryScanCreationStatus.InvalidFoundListResult:
			case MemoryScanCreationStatus.AliasedFoundList:
			case MemoryScanCreationStatus.TargetIdentityUnavailable:
				failure = new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable, operation,
					$"The bounded AOB scan session could not be created ({status}).", null,
					CheatEngineHostEffect.NotStarted);
				return AobBoundedDisposition.FallBack;
			case MemoryScanCreationStatus.RollbackUnconfirmed:
				failure = new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, operation,
					"The bounded AOB scan session could not be created, and Cheat Engine did not confirm its rollback.",
					null, CheatEngineHostEffect.CleanupUnconfirmed);
				return AobBoundedDisposition.Fail;
			default:
				failure = new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, operation,
					"The bounded AOB scan session could not be created, and CheatEngine.SDK reported a creation status " +
					"this Client version does not recognize, so no MemScan object is known to have been removed.", null,
					CheatEngineHostEffect.CleanupUnconfirmed);
				return AobBoundedDisposition.Fail;
		}
	}

	/// <summary>Translates the Client-owned protection filter and alignment rule into CheatEngine.SDK's scan options.</summary>
	/// <param name="protection">
	///     The protection filter; an all-unspecified filter is the empty protection text, which CheatEngine.SDK documents as
	///     Cheat Engine's "find everything" value.
	/// </param>
	/// <param name="alignment">The alignment rule.</param>
	/// <returns>
	///     The SDK options: the protection text in Cheat Engine's order (<c>X</c>, <c>C</c>, <c>W</c>; <c>+</c> required,
	///     <c>-</c> excluded, <c>*</c> either), and the fast-scan method with its decimal divisor or upper-case digits.
	/// </returns>
	/// <remarks>
	///     <para>
	///         The protection text is always explicit, so both routes send Cheat Engine the same argument: the bounded route
	///         turns an omitted text into the empty string itself, and the global <c>AOBScan</c> receives the empty string
	///         instead of an omitted or <c>nil</c> argument, whose meaning the SDK does not document.
	///     </para>
	///     <para>
	///         The public values validate themselves when they are created; <see cref="PatternScanner.TryValidateRequest" />
	///         refuses an undefined value before dispatch, so this translation never sees one.
	///     </para>
	/// </remarks>
	internal static AobScanOptions ToSdkOptions(ScanProtectionFilter protection, ScanAlignment alignment)
	{
		// The AOB options omit the alignment parameter without alignment (ScanOptionTranslation).
		(FastScanMethod method, string? parameter) = ScanOptionTranslation.ToFastScan(alignment, null);
		return new AobScanOptions(ScanOptionTranslation.ToProtectionText(protection), method, parameter);
	}

	/// <summary>Returns the public host outcome of a global scan.</summary>
	/// <param name="kind">The SDK outcome.</param>
	/// <returns>The same category; <see cref="PatternScanHostOutcomeKind.Unknown" /> for an undefined value.</returns>
	internal static PatternScanHostOutcomeKind ToHostOutcome(AobScanOutcomeKind kind)
	{
		return kind switch
		{
			AobScanOutcomeKind.Unknown => PatternScanHostOutcomeKind.Unknown,
			AobScanOutcomeKind.Matches => PatternScanHostOutcomeKind.Matches,
			AobScanOutcomeKind.NoMatches => PatternScanHostOutcomeKind.NoMatches,
			AobScanOutcomeKind.GlobalUnavailable => PatternScanHostOutcomeKind.GlobalUnavailable,
			AobScanOutcomeKind.ProtectedLuaFailure => PatternScanHostOutcomeKind.ProtectedLuaFailure,
			AobScanOutcomeKind.NoResult => PatternScanHostOutcomeKind.NoResult,
			AobScanOutcomeKind.InvalidResult => PatternScanHostOutcomeKind.InvalidResult,
			AobScanOutcomeKind.ResultListCountUnavailable => PatternScanHostOutcomeKind.ResultListCountUnavailable,
			_ => PatternScanHostOutcomeKind.Unknown
		};
	}

	/// <summary>Returns the public host outcome of a bounded scan.</summary>
	/// <param name="kind">The SDK outcome.</param>
	/// <returns>
	///     The same category. <c>InvalidBounds</c> (refused before any Cheat Engine call), <c>SessionCreationFailed</c>
	///     (no scan ran; the request falls back) and <c>WaitTimedOut</c> (a deadline this Client never sets) have no host
	///     outcome and are <see cref="PatternScanHostOutcomeKind.Unknown" />, like an undefined value.
	/// </returns>
	internal static PatternScanHostOutcomeKind ToHostOutcome(AobBoundedScanOutcomeKind kind)
	{
		return kind switch
		{
			AobBoundedScanOutcomeKind.Unknown => PatternScanHostOutcomeKind.Unknown,
			AobBoundedScanOutcomeKind.Matches => PatternScanHostOutcomeKind.Matches,
			AobBoundedScanOutcomeKind.NoMatches => PatternScanHostOutcomeKind.NoMatches,
			AobBoundedScanOutcomeKind.InvalidBounds => PatternScanHostOutcomeKind.Unknown,
			AobBoundedScanOutcomeKind.SessionCreationFailed => PatternScanHostOutcomeKind.Unknown,
			AobBoundedScanOutcomeKind.ScanFailed => PatternScanHostOutcomeKind.ScanFailed,
			AobBoundedScanOutcomeKind.WaitTimedOut => PatternScanHostOutcomeKind.Unknown,
			AobBoundedScanOutcomeKind.HostReportedError => PatternScanHostOutcomeKind.HostReportedError,
			AobBoundedScanOutcomeKind.InvalidResult => PatternScanHostOutcomeKind.InvalidResult,
			AobBoundedScanOutcomeKind.TargetChanged => PatternScanHostOutcomeKind.TargetChanged,
			AobBoundedScanOutcomeKind.TargetIdentityUnavailable => PatternScanHostOutcomeKind.TargetIdentityUnavailable,
			AobBoundedScanOutcomeKind.RuntimeInvalidated => PatternScanHostOutcomeKind.RuntimeChanged,
			AobBoundedScanOutcomeKind.Cancelled => PatternScanHostOutcomeKind.Cancelled,
			_ => PatternScanHostOutcomeKind.Unknown
		};
	}

	/// <summary>Gets whether the SDK read the host result count of a bounded scan, so its metrics are meaningful.</summary>
	/// <remarks>
	///     A completed copy always read the count. CheatEngine.SDK also reads it before any row, and keeps its accounting
	///     on a failure, so a row that could not be read or a cancellation observed between rows still carries it: a
	///     non-zero count or a read row proves that the count was read.
	/// </remarks>
	internal static bool HasReadCount(in AobBoundedHostResult result)
	{
		return result.Kind is AobBoundedScanOutcomeKind.Matches or AobBoundedScanOutcomeKind.NoMatches
				   or AobBoundedScanOutcomeKind.HostReportedError || result.RowsRead > 0 || result.HostResultCount > 0;
	}

	/// <summary>Checks the one child-before-parent release of a bounded scan's MemScan session.</summary>
	/// <param name="result">The copied SDK result.</param>
	/// <param name="released">The combined release kind (<see cref="SdkReleaseOutcomes.Worst" />).</param>
	/// <returns>
	///     <see langword="true" /> when no session was created, or when both owners report <c>Released</c> and a scan that
	///     may still have been running needed no stop or had its stop confirmed.
	/// </returns>
	internal static bool IsSessionReleaseConfirmed(in AobBoundedHostResult result, out LeaseReleaseKind released)
	{
		if (result.CreationStatus != MemoryScanCreationStatus.Success ||
			result.Kind == AobBoundedScanOutcomeKind.SessionCreationFailed)
		{
			// No session was published, so there is nothing to release; an unconfirmed rollback is its own failure.
			released = LeaseReleaseKind.Released;
			return true;
		}

		released = SdkReleaseOutcomes.Worst(SdkReleaseOutcomes.FromTarget(result.FoundListRelease),
			SdkReleaseOutcomes.FromTarget(result.MemScanRelease)).Kind;
		if (released == LeaseReleaseKind.Released && !ScanTermination.IsStopConfirmed(result.ReleaseTermination))
		{
			released = LeaseReleaseKind.CleanupUnconfirmed;
		}

		return released == LeaseReleaseKind.Released;
	}

	/// <summary>The effect of a failure the SDK observed during a bounded scan: completed only after the scan completed.</summary>
	private static CheatEngineHostEffect ScanEffect(in AobBoundedHostResult result)
	{
		return result.HostScanElapsed > TimeSpan.Zero ? CheatEngineHostEffect.Completed : CheatEngineHostEffect.Unknown;
	}

	private static CheatEngineFailure Indeterminate(string operation)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, operation,
			"CheatEngine.SDK reported an AOB outcome this Client version does not recognize.", null,
			CheatEngineHostEffect.Unknown);
	}
}

/// <summary>What the scanner does with the result of a bounded scan.</summary>
internal enum AobBoundedDisposition
{
	/// <summary>The scan failed; report its failure.</summary>
	Fail = 0,

	/// <summary>The scan succeeded; publish its in-bounds addresses.</summary>
	Publish = 1,

	/// <summary>The bounded route could not run on this target; run the global route with managed post-filters.</summary>
	FallBack = 2
}

/// <summary>Whether the answer of one scan can be attributed to the target the caller expected.</summary>
internal enum AobTargetVerdict
{
	/// <summary>No target could be qualified, but the selection did not visibly change: the answer is kept, unverified.</summary>
	Unverified = 0,

	/// <summary>One qualified incarnation before and after the call: the answer is kept and verified.</summary>
	Verified = 1,

	/// <summary>The selection changed during the call: the answer is discarded.</summary>
	Changed = 2,

	/// <summary>The identity was lost, gained or otherwise differed during the call: the answer is discarded.</summary>
	IdentityUnavailable = 3
}
