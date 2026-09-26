using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>
///     Reports that <see cref="OwnershipHandoff.Adopt{TOwner, TResult}" /> could not publish an acquired owner and that
///     the owner's release was not confirmed.
/// </summary>
/// <remarks>
///     <para>
///         The release kind is typed (<see cref="ReleaseKind" />), so a caller reports
///         <see cref="CheatEngineHostEffect.CleanupUnconfirmed" /> without reading the message. The first inner exception
///         is the publication failure; a second inner exception is the release failure when the release threw.
///     </para>
///     <para>
///         It never leaves Core: the domain that called the port maps it to its own classified failure.
///     </para>
/// </remarks>
internal sealed class OwnershipHandoffException : AggregateException
{
	/// <summary>Creates the report of a failed handoff whose release was not confirmed.</summary>
	/// <param name="releaseKind">
	///     The release outcome, never <see cref="LeaseReleaseKind.Released" />; <see cref="LeaseReleaseKind.Unknown" />
	///     when the release threw.
	/// </param>
	/// <param name="publishFailure">The exception that prevented publication.</param>
	/// <param name="releaseFailure">The exception the release threw, if it threw.</param>
	internal OwnershipHandoffException(LeaseReleaseKind releaseKind, Exception publishFailure,
		Exception? releaseFailure)
		: base($"The acquired resource could not be published, and its release was not confirmed ({releaseKind}).",
			releaseFailure is null ? [publishFailure] : [publishFailure, releaseFailure])
	{
		ReleaseKind = releaseKind;
		PublishFailure = publishFailure;
		ReleaseFailure = releaseFailure;
	}

	/// <summary>Gets how the release of the acquired owner ended; never <see cref="LeaseReleaseKind.Released" />.</summary>
	internal LeaseReleaseKind ReleaseKind
	{
		get;
	}

	/// <summary>Gets the exception that prevented publication.</summary>
	internal Exception PublishFailure
	{
		get;
	}

	/// <summary>Gets the exception the release threw, or <see langword="null" /> when it returned an outcome.</summary>
	internal Exception? ReleaseFailure
	{
		get;
	}
}
