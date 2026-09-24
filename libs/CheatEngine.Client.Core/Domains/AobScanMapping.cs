using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Maps the outcome of CheatEngine.SDK 2.0.0 <c>AobScanner.TryScanOutcome</c> and its target context to the Client
///     vocabulary, value by value (audit F06, CRIT-03).
/// </summary>
/// <remarks>
///     <para>Outcomes that hand out no usable result list:</para>
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

	private static CheatEngineFailure Indeterminate(string operation)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, operation,
			"CheatEngine.SDK reported an AOB outcome this Client version does not recognize.", null,
			CheatEngineHostEffect.Unknown);
	}
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
