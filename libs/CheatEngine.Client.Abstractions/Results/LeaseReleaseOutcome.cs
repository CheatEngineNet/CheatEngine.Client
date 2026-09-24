namespace CheatEngine.Client.Results;

/// <summary>The copied result of one attempt to release a Client lease (<see cref="ICheatEngineLease" />).</summary>
/// <remarks>
///     <para>
///         <see cref="Kind" /> says what the attempt did and <see cref="HostEffect" /> how far the Cheat Engine release
///         call got. The three flags are derived from <see cref="Kind" /> and exactly one of them is
///         <see langword="true" />:
///     </para>
///     <list type="table">
///         <listheader>
///             <term>Flag</term>
///             <description>Kinds</description>
///         </listheader>
///         <item>
///             <term><see cref="IsComplete" /></term>
///             <description>
///                 <see cref="LeaseReleaseKind.Released" />, <see cref="LeaseReleaseKind.AlreadyReleased" />,
///                 <see cref="LeaseReleaseKind.Replaced" />, <see cref="LeaseReleaseKind.Superseded" />,
///                 <see cref="LeaseReleaseKind.ExternallyRemoved" />
///             </description>
///         </item>
///         <item>
///             <term><see cref="IsRetryable" /></term>
///             <description>
///                 <see cref="LeaseReleaseKind.Unknown" />, <see cref="LeaseReleaseKind.CleanupUnavailable" />, and any
///                 kind this version does not define
///             </description>
///         </item>
///         <item>
///             <term><see cref="RequiresManualRecovery" /></term>
///             <description>
///                 <see cref="LeaseReleaseKind.PartiallyReleased" />, the four <c>Refused*</c> kinds,
///                 <see cref="LeaseReleaseKind.CleanupUnconfirmed" />
///             </description>
///         </item>
///     </list>
///     <para>
///         The <see langword="default" /> value is <see cref="LeaseReleaseKind.Unknown" /> with an
///         <see cref="CheatEngineHostEffect.Unknown" /> effect: it never reads as a release. <see cref="ToString" /> returns
///         only the kind and the effect, which are safe to log.
///     </para>
/// </remarks>
public readonly record struct LeaseReleaseOutcome
{
	/// <summary>Creates a release outcome.</summary>
	/// <param name="kind">What the release attempt did.</param>
	/// <param name="hostEffect">How far the Cheat Engine release call got.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="kind" /> or <paramref name="hostEffect" /> is not a defined value.
	/// </exception>
	public LeaseReleaseOutcome(LeaseReleaseKind kind, CheatEngineHostEffect hostEffect)
	{
		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind), kind, "The lease release kind must be a defined value.");
		}

		if (!Enum.IsDefined(hostEffect))
		{
			throw new ArgumentOutOfRangeException(nameof(hostEffect), hostEffect,
				"The Cheat Engine host effect must be a defined value.");
		}

		Kind = kind;
		HostEffect = hostEffect;
	}

	/// <summary>Gets what the release attempt did.</summary>
	public LeaseReleaseKind Kind
	{
		get;
	}

	/// <summary>Gets how far the Cheat Engine release call got.</summary>
	/// <remarks>
	///     <see cref="CheatEngineHostEffect.NotStarted" /> when no release call was made (a refusal, an unavailable
	///     cleanup, a lease that was already released or replaced), <see cref="CheatEngineHostEffect.Completed" /> for a
	///     confirmed release, <see cref="CheatEngineHostEffect.Started" /> when a call began without a confirmed result,
	///     and <see cref="CheatEngineHostEffect.Unknown" /> when nothing is known.
	/// </remarks>
	public CheatEngineHostEffect HostEffect
	{
		get;
	}

	/// <summary>
	///     Gets whether the release ended and nothing that the lease owned is known to remain: the lease needs no further
	///     action.
	/// </summary>
	public bool IsComplete => Kind is LeaseReleaseKind.Released or LeaseReleaseKind.AlreadyReleased
		or LeaseReleaseKind.Replaced or LeaseReleaseKind.Superseded or LeaseReleaseKind.ExternallyRemoved;

	/// <summary>
	///     Gets whether the release ended but the resource, or part of it, may remain in Cheat Engine or in the target,
	///     and the Client will not try again: only a manual recovery (or the end of the target process) removes it.
	/// </summary>
	public bool RequiresManualRecovery => Kind is LeaseReleaseKind.PartiallyReleased
		or LeaseReleaseKind.RefusedNoTarget or LeaseReleaseKind.RefusedTargetChanged
		or LeaseReleaseKind.RefusedTargetIdentityUnavailable or LeaseReleaseKind.RefusedRuntimeChanged
		or LeaseReleaseKind.CleanupUnconfirmed;

	/// <summary>
	///     Gets whether nothing was released and the lease is still active: a later <see cref="ICheatEngineLease.Release" />
	///     can try again, and the activation cleanup tries again before the plugin is disabled.
	/// </summary>
	/// <remarks>
	///     <see langword="true" /> only for <see cref="LeaseReleaseKind.Unknown" /> and
	///     <see cref="LeaseReleaseKind.CleanupUnavailable" /> (and a kind this version does not define), the same rule as
	///     CheatEngine.SDK: a release call that began is never retried.
	/// </remarks>
	public bool IsRetryable => !IsComplete && !RequiresManualRecovery;

	/// <summary>Returns only the kind and the host effect, which are safe to log.</summary>
	/// <returns>A description in the form <c>Kind (host effect: HostEffect)</c>.</returns>
	public override string ToString()
	{
		return $"{Kind} (host effect: {HostEffect})";
	}
}
