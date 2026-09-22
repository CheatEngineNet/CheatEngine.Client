namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Transfers release authority from an acquired owner to the Client object that publishes it.</summary>
/// <remarks>
///     <para>
///         Between the acquisition of an owner (for example the SDK <c>Owned&lt;StringList&gt;</c> returned by
///         <c>AobScanner.TryScan</c>) and the publication of the Client wrapper that will release it, exactly one party
///         must remain responsible for the release (audit F13). If publication fails for any reason, including an
///         allocation failure, this helper releases the owner exactly once and never retries the release.
///     </para>
///     <para>
///         On success the published object is the sole release authority; the helper never touches the owner again.
///     </para>
/// </remarks>
internal static class OwnershipHandoff
{
	/// <summary>Publishes <paramref name="owner" /> through <paramref name="publish" /> or releases it exactly once.</summary>
	/// <typeparam name="TOwner">The acquired owner type whose <see cref="IDisposable.Dispose" /> releases the resource.</typeparam>
	/// <typeparam name="TResult">The published object that becomes the single release authority.</typeparam>
	/// <param name="owner">The acquired owner. The caller must not use it after this call.</param>
	/// <param name="publish">Creates the object that takes over release authority.</param>
	/// <returns>The published object.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="owner" /> is <see langword="null" />.</exception>
	/// <exception cref="AggregateException">
	///     Publication failed and the release of <paramref name="owner" /> also failed. The first inner exception is the
	///     publication failure; the second is the release failure. The release is not confirmed.
	/// </exception>
	/// <remarks>
	///     When publication fails and the release succeeds, the original publication exception is rethrown unchanged
	///     (same instance, original stack trace).
	/// </remarks>
	internal static TResult Adopt<TOwner, TResult>(TOwner owner, Func<TOwner, TResult> publish)
		where TOwner : class, IDisposable
	{
		ArgumentNullException.ThrowIfNull(owner);

		try
		{
			ArgumentNullException.ThrowIfNull(publish);
			return publish(owner);
		}
		catch (Exception publishFailure)
		{
			try
			{
				owner.Dispose();
			}
			catch (Exception releaseFailure)
			{
				throw new AggregateException(
					"The acquired resource could not be published, and its release was not confirmed.",
					publishFailure,
					releaseFailure);
			}

			throw;
		}
	}
}
