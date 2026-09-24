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
}
