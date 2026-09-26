using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Transfers release authority from an acquired owner to the Client object that publishes it.</summary>
/// <remarks>
///     <para>
///         Between the acquisition of an owner (for example the SDK <c>Owned&lt;StringList&gt;</c> returned by
///         <c>AobScanner.TryScanOutcome</c>) and the publication of the Client wrapper that will release it, exactly one
///         party must remain responsible for the release (audit F13). If publication fails for any reason, including an
///         allocation failure, this helper releases the owner exactly once and never retries the release.
///     </para>
///     <para>
///         On success the published object is the sole release authority; the helper never touches the owner again.
///     </para>
/// </remarks>
internal static class OwnershipHandoff
{
	/// <summary>Publishes <paramref name="owner" /> through <paramref name="publish" /> or releases it exactly once.</summary>
	/// <typeparam name="TOwner">The acquired owner type.</typeparam>
	/// <typeparam name="TResult">The published object that becomes the single release authority.</typeparam>
	/// <param name="owner">The acquired owner. The caller must not use it after this call.</param>
	/// <param name="publish">Creates the object that takes over release authority.</param>
	/// <param name="release">
	///     Releases the owner once and reports the outcome (for an SDK owner, <c>ReleaseWithOutcome</c> mapped through
	///     <see cref="SdkReleaseOutcomes" />). Called only when publication fails.
	/// </param>
	/// <returns>The published object.</returns>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="owner" /> or <paramref name="release" /> is <see langword="null" />.
	/// </exception>
	/// <exception cref="OwnershipHandoffException">
	///     Publication failed and the release of <paramref name="owner" /> was not confirmed. The exception carries the
	///     release kind (<see cref="LeaseReleaseKind.Unknown" /> when the release threw); its first inner exception is the
	///     publication failure and a second inner exception is the release failure when the release threw.
	/// </exception>
	/// <remarks>
	///     When publication fails and the release is confirmed (<see cref="LeaseReleaseKind.Released" />), the original
	///     publication exception is rethrown unchanged (same instance, original stack trace).
	/// </remarks>
	internal static TResult Adopt<TOwner, TResult>(TOwner owner, Func<TOwner, TResult> publish,
		Func<TOwner, LeaseReleaseOutcome> release)
		where TOwner : class
	{
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(release);

		try
		{
			ArgumentNullException.ThrowIfNull(publish);
			return publish(owner);
		}
		catch (Exception publishFailure)
		{
			LeaseReleaseOutcome outcome;
			try
			{
				outcome = release(owner);
			}
			catch (Exception releaseFailure)
			{
				throw new OwnershipHandoffException(LeaseReleaseKind.Unknown, publishFailure, releaseFailure);
			}

			if (outcome.Kind != LeaseReleaseKind.Released)
			{
				throw new OwnershipHandoffException(outcome.Kind, publishFailure, null);
			}

			throw;
		}
	}

	/// <summary>Reports a failed handoff whose release was not confirmed, for the operation that called the port.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="handoff">The failed handoff.</param>
	/// <param name="subject">What the owner held, for the message (for example <c>AOB result list</c>).</param>
	/// <param name="lifetime">The owning activation lifetime, when the caller has one.</param>
	/// <returns>
	///     The publication fault classified by <see cref="SdkBoundary.Translate" /> with a completed effect (Cheat Engine
	///     returned the owner), then marked <see cref="CheatEngineHostEffect.CleanupUnconfirmed" /> with the release kind
	///     (<see cref="WithUnconfirmedRelease" />).
	/// </returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation ended while the SDK call ran.</exception>
	/// <remarks>
	///     Every route that publishes an SDK owner through <see cref="Adopt{TOwner, TResult}" /> reports its failed
	///     handoff with this one mapping: the AOB result list and the Auto Assembler patch. A handoff whose release was
	///     confirmed rethrows the publication fault instead, which the route classifies like any SDK fault.
	/// </remarks>
	internal static CheatEngineFailure ToFailure(string operation, OwnershipHandoffException handoff, string subject,
		CoreLifetime? lifetime)
	{
		ArgumentNullException.ThrowIfNull(handoff);
		CheatEngineFailure publishFailure = SdkBoundary.Translate(operation, handoff.PublishFailure,
			CheatEngineHostEffect.Completed, lifetime);
		return WithUnconfirmedRelease(publishFailure, subject, handoff.ReleaseKind, handoff.ReleaseFailure);
	}

	/// <summary>Adds a release that was not confirmed to the failure that caused it.</summary>
	/// <param name="primary">The failure that caused the release.</param>
	/// <param name="subject">What the released owner held, for the message.</param>
	/// <param name="released">The release kind, never <see cref="LeaseReleaseKind.Released" />.</param>
	/// <param name="releaseFault">The exception the release threw, if it threw.</param>
	/// <returns>
	///     The failure with its kind and operation, the release appended to its message, both exceptions, and
	///     <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />.
	/// </returns>
	internal static CheatEngineFailure WithUnconfirmedRelease(CheatEngineFailure primary, string subject,
		LeaseReleaseKind released, Exception? releaseFault)
	{
		Exception? exception = (primary.Exception, releaseFault) switch
		{
			({ } cause, { } fault) => new AggregateException(cause, fault),
			({ } cause, null) => cause,
			_ => releaseFault
		};
		return new CheatEngineFailure(primary.Kind, primary.Operation,
			$"{primary.Message} The {subject} release was not confirmed ({released}).", exception,
			CheatEngineHostEffect.CleanupUnconfirmed);
	}
}
