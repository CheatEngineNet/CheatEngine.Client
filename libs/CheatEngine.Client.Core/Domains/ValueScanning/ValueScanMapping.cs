using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Scanning.Values;

namespace CheatEngine.Client.Core.Domains.ValueScanning;

/// <summary>Maps every value-scan outcome that CheatEngine.SDK 2.0.0 reports to the Client vocabulary.</summary>
/// <remarks>
///     <para>
///         Each mapping is total over its SDK enum, and a value this Client version does not know fails closed
///         (<see cref="CheatEngineFailureKind.IndeterminateHostResult" />, <see cref="CheatEngineHostEffect.Unknown" />, or
///         the <c>Unknown</c> member of the Client enum); the mapping-totality tests fail when the consumed SDK adds a
///         value. Exceptions are classified by <see cref="SdkBoundary" />; this type only decides how far the Cheat Engine
///         call got when one was thrown.
///     </para>
///     <para>
///         A creation that the SDK refused after it created an object has already been rolled back by the SDK, child
///         before parent: nothing that the Client owns remains, which <see cref="CheatEngineHostEffect.NotApplied" />
///         reports. <see cref="MemoryScanCreationStatus.RollbackUnconfirmed" /> may leave a scanner in Cheat Engine, and so
///         may a status that contradicts the published session or that this Client version does not recognize, since
///         nothing proves that no object remains: all three are
///         <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> with
///         <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />.
///     </para>
///     <para>
///         The bounded AOB route reports the same three statuses with the same
///         <see cref="CheatEngineHostEffect.CleanupUnconfirmed" /> effect but the
///         <see cref="CheatEngineFailureKind.InvalidState" /> kind, because there a failed creation ends an internal step
///         of a scan. Here the creation is the public operation, and the SDK reports only that the rollback was not
///         confirmed, not why the creation failed: the result cannot be attributed to one cause.
///     </para>
/// </remarks>
internal static class ValueScanMapping
{
	/// <summary>Maps a creation status other than <see cref="MemoryScanCreationStatus.Success" /> to its failure.</summary>
	/// <param name="status">The status of <c>MemoryScanSessions.TryCreateWithOutcome</c>.</param>
	/// <param name="operation">The public Client operation name.</param>
	/// <returns>
	///     The classified failure; a success without a session is a contract break, reported like an unrecognized status.
	/// </returns>
	internal static CheatEngineFailure FromCreationStatus(MemoryScanCreationStatus status, string operation)
	{
		return status switch
		{
			MemoryScanCreationStatus.TargetIdentityUnavailable => Failure(
				CheatEngineFailureKind.TargetIdentityUnavailable, operation, CheatEngineHostEffect.NotStarted,
				"The identity of Cheat Engine's selected target could not be established, so no scan object was created."),
			MemoryScanCreationStatus.GlobalUnavailable => Failure(CheatEngineFailureKind.CapabilityUnavailable,
				operation, CheatEngineHostEffect.NotApplied,
				"Cheat Engine's createMemScan or createFoundList global is unavailable; no scan session exists."),
			MemoryScanCreationStatus.LuaFailure => Failure(CheatEngineFailureKind.LuaError, operation,
				CheatEngineHostEffect.Unknown,
				"A Cheat Engine scan factory raised a Lua error; no scan session exists."),
			MemoryScanCreationStatus.NoScannerResult or MemoryScanCreationStatus.NoFoundListResult => Failure(
				CheatEngineFailureKind.OperationRejected, operation, CheatEngineHostEffect.NotApplied,
				"A Cheat Engine scan factory returned nil; no scan session exists."),
			MemoryScanCreationStatus.InvalidScannerResult or MemoryScanCreationStatus.InvalidFoundListResult
				or MemoryScanCreationStatus.AliasedFoundList => Failure(CheatEngineFailureKind.InvalidHostResult,
					operation, CheatEngineHostEffect.NotApplied,
					"A Cheat Engine scan factory returned a value that is not a distinct Cheat Engine object; no scan " +
					"session exists."),
			MemoryScanCreationStatus.RollbackUnconfirmed => Failure(CheatEngineFailureKind.IndeterminateHostResult,
				operation, CheatEngineHostEffect.CleanupUnconfirmed,
				"Creating the scan session failed and Cheat Engine did not confirm the destruction of the objects it " +
				"had created: a scanner may remain in Cheat Engine."),
			MemoryScanCreationStatus.Success => Failure(CheatEngineFailureKind.IndeterminateHostResult, operation,
				CheatEngineHostEffect.CleanupUnconfirmed,
				"CheatEngine.SDK reported a created scan session but published none: a scanner may remain in Cheat " +
				"Engine."),
			_ => Failure(CheatEngineFailureKind.IndeterminateHostResult, operation,
				CheatEngineHostEffect.CleanupUnconfirmed,
				"CheatEngine.SDK reported no recognized scan-session creation status, so no scan object is known to " +
				"have been removed.")
		};
	}

	/// <summary>Maps the SDK session state to the Client session state.</summary>
	/// <param name="state">The state reported by CheatEngine.SDK.</param>
	/// <returns>The Client state; <see cref="ValueScanSessionState.Unknown" /> for an unrecognized value.</returns>
	internal static ValueScanSessionState ToState(MemoryScanState state)
	{
		return state switch
		{
			MemoryScanState.New => ValueScanSessionState.Created,
			MemoryScanState.Scanning => ValueScanSessionState.Scanning,
			MemoryScanState.ResultsReady => ValueScanSessionState.ResultsReady,
			MemoryScanState.Invalidated => ValueScanSessionState.Invalidated,
			MemoryScanState.Disposed => ValueScanSessionState.Released,
			_ => ValueScanSessionState.Unknown
		};
	}

	/// <summary>Maps the SDK invalidation reason to the Client invalidation kind.</summary>
	/// <param name="reason">The reason reported by CheatEngine.SDK.</param>
	/// <returns>
	///     The Client kind; <see cref="ValueScanInvalidationKind.Unknown" /> for an unrecognized value and for
	///     <see cref="MemoryScanInvalidationReason.ScanTerminated" />, which only the experimental stop request that the
	///     Client never makes produces.
	/// </returns>
	internal static ValueScanInvalidationKind ToInvalidation(MemoryScanInvalidationReason reason)
	{
		return reason switch
		{
			MemoryScanInvalidationReason.None => ValueScanInvalidationKind.None,
			MemoryScanInvalidationReason.ProtectedLuaFailure => ValueScanInvalidationKind.HostCallFailed,
			MemoryScanInvalidationReason.RuntimeIdentityChanged => ValueScanInvalidationKind.RuntimeChanged,
			MemoryScanInvalidationReason.TargetChanged => ValueScanInvalidationKind.TargetChanged,
			// A reused PID names another process incarnation: for the Client that is a change of target.
			MemoryScanInvalidationReason.TargetProcessReused => ValueScanInvalidationKind.TargetChanged,
			MemoryScanInvalidationReason.ScanTerminated => ValueScanInvalidationKind.Unknown,
			_ => ValueScanInvalidationKind.Unknown
		};
	}

	/// <summary>Maps the cancellation milestone of a cancellable SDK operation to a cancellation failure.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="milestone">Where CheatEngine.SDK observed the cancellation.</param>
	/// <returns>
	///     A <see cref="CheatEngineFailureKind.Cancelled" /> failure: <see cref="CheatEngineHostEffect.NotStarted" />
	///     before the native call, <see cref="CheatEngineHostEffect.Completed" /> after it returned, otherwise
	///     <see cref="CheatEngineHostEffect.Unknown" />.
	/// </returns>
	internal static CheatEngineFailure Cancelled(string operation, MemoryScanCancellationMilestone milestone)
	{
		return milestone switch
		{
			MemoryScanCancellationMilestone.CancelledBeforeNativeCall => CancellationMapping.BeforeNativeCall(operation),
			MemoryScanCancellationMilestone.ObservedAfterNativeCall => CancellationMapping.AfterNativeCall(operation),
			MemoryScanCancellationMilestone.None => Failure(CheatEngineFailureKind.Cancelled, operation,
				CheatEngineHostEffect.Unknown,
				"The operation was cancelled and CheatEngine.SDK recorded no cancellation milestone."),
			_ => Failure(CheatEngineFailureKind.Cancelled, operation, CheatEngineHostEffect.Unknown,
				"The operation was cancelled at a point this Client version does not recognize.")
		};
	}

	/// <summary>Maps the status of a result-page copy to its failure.</summary>
	/// <param name="status">The status of <c>TryCopyResultsPageCancellable</c>.</param>
	/// <param name="milestone">The cancellation milestone of the copy.</param>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="failure">The failure, or the default value for a page (a copied page or an empty result set).</param>
	/// <returns><see langword="true" /> when the status is a failure.</returns>
	internal static bool TryGetPageFailure(MemoryScanMaterializationStatus status,
		MemoryScanCancellationMilestone milestone, string operation, out CheatEngineFailure failure)
	{
		failure = status switch
		{
			MemoryScanMaterializationStatus.Success or MemoryScanMaterializationStatus.NoResults => default,
			MemoryScanMaterializationStatus.DestinationTooSmall => Failure(CheatEngineFailureKind.ResultLimitExceeded,
				operation, CheatEngineHostEffect.Completed,
				"The result page did not fit the Client's copy buffer; no result was copied."),
			MemoryScanMaterializationStatus.Cancelled => Cancelled(operation, milestone),
			MemoryScanMaterializationStatus.RuntimeInvalidated => Failure(CheatEngineFailureKind.RuntimeChanged,
				operation, CheatEngineHostEffect.NotStarted,
				"The scan session belongs to an earlier Lua runtime; only its release remains."),
			MemoryScanMaterializationStatus.TargetIdentityUnavailable => Failure(
				CheatEngineFailureKind.TargetIdentityUnavailable, operation, CheatEngineHostEffect.NotStarted,
				"The identity of Cheat Engine's selected target could not be established; no result was read."),
			MemoryScanMaterializationStatus.TargetIdentityMismatch => Failure(CheatEngineFailureKind.TargetChanged,
				operation, CheatEngineHostEffect.NotStarted,
				"Cheat Engine selected another target than the session's; only its release remains."),
			MemoryScanMaterializationStatus.LuaFailure => Failure(CheatEngineFailureKind.LuaError, operation,
				CheatEngineHostEffect.Unknown, "A Cheat Engine result call raised a Lua error; no result was copied."),
			MemoryScanMaterializationStatus.InvalidResult => Failure(CheatEngineFailureKind.InvalidHostResult,
				operation, CheatEngineHostEffect.Completed,
				"Cheat Engine returned a result count, address or value outside its documented shape; no result was " +
				"copied."),
			MemoryScanMaterializationStatus.PageStartOutOfRange => Failure(CheatEngineFailureKind.OperationRejected,
				operation, CheatEngineHostEffect.Completed,
				"The page starts at or beyond the number of results; no result was copied."),
			_ => Failure(CheatEngineFailureKind.IndeterminateHostResult, operation, CheatEngineHostEffect.Unknown,
				"CheatEngine.SDK reported no recognized result-copy status; no result was copied.")
		};
		return status is not (MemoryScanMaterializationStatus.Success or MemoryScanMaterializationStatus.NoResults);
	}

	/// <summary>Combines the release status of the found list and of the scanner, keeping the worse.</summary>
	/// <param name="statuses">The SDK release statuses.</param>
	/// <returns>The lease outcome.</returns>
	/// <remarks>
	///     Both objects confirmed as released still leave <see cref="LeaseReleaseKind.CleanupUnconfirmed" /> when a scan
	///     that may have been running was not confirmed as stopped (<see cref="IsStopConfirmed" />), as the bounded AOB
	///     route reports it. A release that could not reach Cheat Engine already reports its refusal in both statuses.
	/// </remarks>
	internal static LeaseReleaseOutcome FromRelease(ValueScanReleaseStatuses statuses)
	{
		LeaseReleaseOutcome released = SdkReleaseOutcomes.Worst(SdkReleaseOutcomes.FromTarget(statuses.FoundList),
			SdkReleaseOutcomes.FromTarget(statuses.MemScan));
		return released.Kind == LeaseReleaseKind.Released && !IsStopConfirmed(statuses.Termination)
			? SdkReleaseOutcomes.Worst(released,
				new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started))
			: released;
	}

	/// <summary>Returns whether no scan could still be running when the session's objects were released.</summary>
	/// <param name="termination">The SDK's termination status of the release.</param>
	/// <returns>
	///     <see langword="true" /> only when no stop was needed or the one cooperative stop was confirmed; an unconfirmed,
	///     refused or unrecognized stop is <see langword="false" />.
	/// </returns>
	internal static bool IsStopConfirmed(MemoryScanTerminationStatus termination)
	{
		return termination switch
		{
			MemoryScanTerminationStatus.NotRequired or MemoryScanTerminationStatus.Confirmed => true,
			MemoryScanTerminationStatus.Unknown or MemoryScanTerminationStatus.WaitTimedOut
				or MemoryScanTerminationStatus.TerminateFailed or MemoryScanTerminationStatus.WaitFailed
				or MemoryScanTerminationStatus.NotInvoked => false,
			_ => false
		};
	}

	/// <summary>Returns how far a scan, wait or reset got when CheatEngine.SDK threw.</summary>
	/// <param name="fault">The SDK fault.</param>
	/// <returns>
	///     <see cref="CheatEngineHostEffect.NotStarted" /> for a refusal that precedes every Cheat Engine call (session state,
	///     re-entrant call, request validation, changed runtime or target), <see cref="CheatEngineHostEffect.Started" /> for a
	///     Cheat Engine call that failed, otherwise <see cref="CheatEngineHostEffect.Unknown" />.
	/// </returns>
	internal static CheatEngineHostEffect MutationFaultEffect(Exception fault)
	{
		return fault switch
		{
			MemoryScanStateException or ArgumentException => CheatEngineHostEffect.NotStarted,
			MemoryScanException scan => MutationFaultEffect(scan.FailureKind),
			_ => CheatEngineHostEffect.Unknown
		};
	}

	/// <summary>Returns how far a scan, wait or reset got for an SDK memory-scan failure category.</summary>
	/// <param name="kind">The category of the <see cref="MemoryScanException" />.</param>
	/// <returns>The host effect; <see cref="CheatEngineHostEffect.Unknown" /> for an unrecognized value.</returns>
	internal static CheatEngineHostEffect MutationFaultEffect(MemoryScanFailureKind kind)
	{
		return kind switch
		{
			// The SDK checks the runtime and target context before any Cheat Engine call of an operation.
			MemoryScanFailureKind.RuntimeInvalidated or MemoryScanFailureKind.TargetIdentityUnavailable
				or MemoryScanFailureKind.TargetIdentityMismatch => CheatEngineHostEffect.NotStarted,
			MemoryScanFailureKind.MissingCapability or MemoryScanFailureKind.LuaError
				or MemoryScanFailureKind.UnexpectedResult => CheatEngineHostEffect.Started,
			_ => CheatEngineHostEffect.Unknown
		};
	}

	/// <summary>Returns how far a result-count read got when CheatEngine.SDK threw.</summary>
	/// <param name="fault">The SDK fault.</param>
	/// <returns>
	///     <see cref="CheatEngineHostEffect.NotStarted" /> for a refusal that precedes the Cheat Engine call,
	///     <see cref="CheatEngineHostEffect.Completed" /> for a count Cheat Engine returned in a malformed shape, otherwise
	///     <see cref="CheatEngineHostEffect.Unknown" />.
	/// </returns>
	internal static CheatEngineHostEffect ReadFaultEffect(Exception fault)
	{
		return fault switch
		{
			MemoryScanStateException or ArgumentException => CheatEngineHostEffect.NotStarted,
			MemoryScanException { FailureKind: MemoryScanFailureKind.UnexpectedResult } =>
				CheatEngineHostEffect.Completed,
			MemoryScanException scan when MutationFaultEffect(scan.FailureKind) == CheatEngineHostEffect.NotStarted =>
				CheatEngineHostEffect.NotStarted,
			_ => CheatEngineHostEffect.Unknown
		};
	}

	/// <summary>Creates the refusal of an operation on a released session.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="outcome">The outcome of the release that ended the session, when the lease recorded one.</param>
	/// <returns>
	///     <see cref="CheatEngineFailureKind.TargetChanged" />, <see cref="CheatEngineFailureKind.TargetIdentityUnavailable" />
	///     or <see cref="CheatEngineFailureKind.RuntimeChanged" /> when the release was refused for that reason, otherwise
	///     <see cref="CheatEngineFailureKind.InvalidState" />; always <see cref="CheatEngineHostEffect.NotStarted" />.
	/// </returns>
	/// <remarks>
	///     CheatEngine.SDK 2.0.0 reports a session released after a change of the Lua runtime as <c>NotInvoked</c>
	///     (<see cref="LeaseReleaseKind.CleanupUnavailable" />), so such a session reports
	///     <see cref="CheatEngineFailureKind.InvalidState" />; the <see cref="LeaseReleaseKind.RefusedRuntimeChanged" /> arm
	///     keeps the mapping total over the lease vocabulary.
	/// </remarks>
	internal static CheatEngineFailure Released(string operation, LeaseReleaseOutcome? outcome)
	{
		CheatEngineFailureKind kind = outcome?.Kind switch
		{
			LeaseReleaseKind.RefusedTargetChanged => CheatEngineFailureKind.TargetChanged,
			LeaseReleaseKind.RefusedTargetIdentityUnavailable => CheatEngineFailureKind.TargetIdentityUnavailable,
			LeaseReleaseKind.RefusedRuntimeChanged => CheatEngineFailureKind.RuntimeChanged,
			_ => CheatEngineFailureKind.InvalidState
		};
		return Failure(kind, operation, CheatEngineHostEffect.NotStarted,
			"The value-scan session was released; create a new session.");
	}

	/// <summary>Appends Cheat Engine's own error text of the scan to a failure message.</summary>
	/// <param name="failure">The failure.</param>
	/// <param name="hostErrorText">Cheat Engine's bounded error text, or <see langword="null" />.</param>
	/// <param name="truncated">Whether CheatEngine.SDK truncated the text.</param>
	/// <returns>The failure with the text appended, or <paramref name="failure" /> when there is no text.</returns>
	internal static CheatEngineFailure WithHostErrorText(CheatEngineFailure failure, string? hostErrorText,
		bool truncated)
	{
		if (string.IsNullOrWhiteSpace(hostErrorText))
		{
			return failure;
		}

		string message = failure.Message + " Cheat Engine reported: " + hostErrorText.Trim() +
						 (truncated ? " (truncated)" : string.Empty);
		return new CheatEngineFailure(failure.Kind, failure.Operation, message, failure.Exception, failure.HostEffect);
	}

	private static CheatEngineFailure Failure(CheatEngineFailureKind kind, string operation,
		CheatEngineHostEffect hostEffect, string message)
	{
		return new CheatEngineFailure(kind, operation, message, null, hostEffect);
	}
}
