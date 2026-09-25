using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>
///     Maps every release status that CheatEngine.SDK 2.0.0 reports to the one Client lease vocabulary
///     (<see cref="LeaseReleaseOutcome" />), and combines the outcomes of a lease made of several parts.
/// </summary>
/// <remarks>
///     <para>
///         Each mapping is total over its SDK enum. A value this Client version does not know is
///         <see cref="LeaseReleaseKind.Unknown" /> with an <see cref="CheatEngineHostEffect.Unknown" /> effect: an
///         unrecognized status never reads as a release. The mapping-totality tests fail when the consumed SDK adds a
///         value.
///     </para>
///     <para>
///         The host effect records how far the release call got: <see cref="CheatEngineHostEffect.Completed" /> for a
///         confirmed release, <see cref="CheatEngineHostEffect.Started" /> for a call that began without a confirmed
///         result, and <see cref="CheatEngineHostEffect.NotStarted" /> when no release call was made.
///     </para>
///     <para>
///         The SDK's <c>NotInvoked</c> consumes a target-bound owner, yet it maps to the retryable
///         <see cref="LeaseReleaseKind.CleanupUnavailable" /> (the same rule as the SDK's symbol leases): a later attempt
///         through a consumed owner returns the same status without any Cheat Engine call, and the activation cleanup
///         reports it if it is still unavailable at deactivation.
///     </para>
///     <para>
///         The SDK Lua registration lease is not mapped here: the generated Lua registrar owns it and maps its
///         <c>LuaRegistrationReleaseKind</c> in the consumer's assembly.
///     </para>
/// </remarks>
internal static class SdkReleaseOutcomes
{
	/// <summary>Maps the release status of a target-bound SDK owner (<c>Owned&lt;T&gt;</c>, memory-scan owners).</summary>
	/// <param name="status">The status reported by CheatEngine.SDK.</param>
	/// <returns>The Client outcome; <see cref="LeaseReleaseKind.Unknown" /> for an unrecognized value.</returns>
	internal static LeaseReleaseOutcome FromTarget(TargetReleaseStatus status)
	{
		return status switch
		{
			// No release was attempted yet: the owner is still held, so the lease stays retryable.
			TargetReleaseStatus.Unspecified => Outcome(LeaseReleaseKind.Unknown, CheatEngineHostEffect.NotStarted),
			TargetReleaseStatus.Released => Outcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
			TargetReleaseStatus.RefusedNoTarget => Outcome(LeaseReleaseKind.RefusedTargetNotAttached,
				CheatEngineHostEffect.NotStarted),
			TargetReleaseStatus.RefusedIdentityUnavailable => Outcome(LeaseReleaseKind.RefusedTargetIdentityUnavailable,
				CheatEngineHostEffect.NotStarted),
			TargetReleaseStatus.RefusedTargetChanged => Outcome(LeaseReleaseKind.RefusedTargetChanged,
				CheatEngineHostEffect.NotStarted),
			// A reused PID names another process incarnation: for the Client that is a change of target.
			TargetReleaseStatus.RefusedProcessReused => Outcome(LeaseReleaseKind.RefusedTargetChanged,
				CheatEngineHostEffect.NotStarted),
			TargetReleaseStatus.UnconfirmedAfterInvocation => Outcome(LeaseReleaseKind.CleanupUnconfirmed,
				CheatEngineHostEffect.Started),
			TargetReleaseStatus.NotInvoked => Outcome(LeaseReleaseKind.CleanupUnavailable,
				CheatEngineHostEffect.NotStarted),
			TargetReleaseStatus.RefusedRuntimeChanged => Outcome(LeaseReleaseKind.RefusedRuntimeChanged,
				CheatEngineHostEffect.NotStarted),
			_ => Unrecognized()
		};
	}

	/// <summary>Maps the release kind of an SDK symbol or symbol-list registration lease.</summary>
	/// <param name="kind">The kind reported by CheatEngine.SDK.</param>
	/// <returns>The Client outcome; <see cref="LeaseReleaseKind.Unknown" /> for an unrecognized value.</returns>
	internal static LeaseReleaseOutcome FromSymbolRegistration(SymbolRegistrationReleaseKind kind)
	{
		return kind switch
		{
			SymbolRegistrationReleaseKind.Unknown => Unrecognized(),
			SymbolRegistrationReleaseKind.Released => Outcome(LeaseReleaseKind.Released,
				CheatEngineHostEffect.Completed),
			SymbolRegistrationReleaseKind.AlreadyReleased => Outcome(LeaseReleaseKind.AlreadyReleased,
				CheatEngineHostEffect.NotStarted),
			SymbolRegistrationReleaseKind.Superseded => Outcome(LeaseReleaseKind.Superseded,
				CheatEngineHostEffect.NotStarted),
			SymbolRegistrationReleaseKind.StaleRuntime => Outcome(LeaseReleaseKind.RefusedRuntimeChanged,
				CheatEngineHostEffect.NotStarted),
			SymbolRegistrationReleaseKind.CleanupUnavailable => Outcome(LeaseReleaseKind.CleanupUnavailable,
				CheatEngineHostEffect.NotStarted),
			SymbolRegistrationReleaseKind.CleanupIndeterminate => Outcome(LeaseReleaseKind.CleanupUnconfirmed,
				CheatEngineHostEffect.Started),
			SymbolRegistrationReleaseKind.Replaced => Outcome(LeaseReleaseKind.Replaced,
				CheatEngineHostEffect.NotStarted),
			SymbolRegistrationReleaseKind.ExternallyRemoved => Outcome(LeaseReleaseKind.ExternallyRemoved,
				CheatEngineHostEffect.NotStarted),
			_ => Unrecognized()
		};
	}

	/// <summary>Combines the outcomes of two parts of one lease, keeping the worse of the two.</summary>
	/// <param name="first">The outcome of one part.</param>
	/// <param name="second">The outcome of the other part.</param>
	/// <returns>The combined outcome; the operation is commutative.</returns>
	/// <remarks>
	///     <para>
	///         The worse kind is the one that leaves more to do. A retryable kind ranks highest, because the lease must
	///         stay active so that the part that can still be released is retried; a kind that requires manual recovery
	///         ranks next (an unconfirmed call above a partial release, above a refusal); a complete kind ranks lowest.
	///         An unrecognized kind ranks like <see cref="LeaseReleaseKind.Unknown" />.
	///     </para>
	///     <para>
	///         The host effects combine separately: equal effects stay, <see cref="CheatEngineHostEffect.Unknown" /> wins,
	///         then <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />; any other mix means that part of the release
	///         ran and part did not, which is <see cref="CheatEngineHostEffect.Started" />.
	///     </para>
	/// </remarks>
	internal static LeaseReleaseOutcome Worst(LeaseReleaseOutcome first, LeaseReleaseOutcome second)
	{
		LeaseReleaseKind kind = Rank(first.Kind) >= Rank(second.Kind) ? first.Kind : second.Kind;
		return new LeaseReleaseOutcome(kind, CombineEffects(first.HostEffect, second.HostEffect));
	}

	/// <summary>Orders the kinds from the one that leaves nothing to do to the one that leaves the most.</summary>
	private static int Rank(LeaseReleaseKind kind)
	{
		return kind switch
		{
			LeaseReleaseKind.Released => 0,
			LeaseReleaseKind.AlreadyReleased => 1,
			LeaseReleaseKind.Superseded => 2,
			LeaseReleaseKind.Replaced => 3,
			LeaseReleaseKind.ExternallyRemoved => 4,
			LeaseReleaseKind.RefusedTargetNotAttached => 5,
			LeaseReleaseKind.RefusedTargetIdentityUnavailable => 6,
			LeaseReleaseKind.RefusedTargetChanged => 7,
			LeaseReleaseKind.RefusedRuntimeChanged => 8,
			LeaseReleaseKind.PartiallyReleased => 9,
			LeaseReleaseKind.CleanupUnconfirmed => 10,
			LeaseReleaseKind.CleanupUnavailable => 11,
			_ => 12
		};
	}

	private static CheatEngineHostEffect CombineEffects(CheatEngineHostEffect first, CheatEngineHostEffect second)
	{
		if (first == second)
		{
			return first;
		}

		if (first == CheatEngineHostEffect.Unknown || second == CheatEngineHostEffect.Unknown)
		{
			return CheatEngineHostEffect.Unknown;
		}

		return first == CheatEngineHostEffect.CleanupUnconfirmed || second == CheatEngineHostEffect.CleanupUnconfirmed
			? CheatEngineHostEffect.CleanupUnconfirmed
			: CheatEngineHostEffect.Started;
	}

	private static LeaseReleaseOutcome Outcome(LeaseReleaseKind kind, CheatEngineHostEffect hostEffect)
	{
		return new LeaseReleaseOutcome(kind, hostEffect);
	}

	private static LeaseReleaseOutcome Unrecognized()
	{
		return new LeaseReleaseOutcome(LeaseReleaseKind.Unknown, CheatEngineHostEffect.Unknown);
	}
}
