using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

/// <summary>Proves the single release authority between SDK owner acquisition and Client publication (audit F13).</summary>
public sealed class OwnershipHandoffTests
{
	[Theory]
	[InlineData(nameof(OutOfMemoryException))]
	[InlineData(nameof(InvalidOperationException))]
	[SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
		Justification = "Simulates the allocation failure named by audit F13 inside a test publisher only.")]
	public void AuthorityTransferWithInjectedFailureAfterAcquisitionReleasesTheOwnerExactlyOnce(string failureType)
	{
		Exception publicationFailure = failureType == nameof(OutOfMemoryException)
			? new OutOfMemoryException("simulated allocation failure while publishing the wrapper")
			: new InvalidOperationException("simulated wrapper constructor validation failure");
		CountingOwner owner = new();

		Exception thrown = Assert.ThrowsAny<Exception>(() =>
			OwnershipHandoff.Adopt<CountingOwner, object>(owner, _ => throw publicationFailure, CountingOwner.Release));

		Assert.Same(publicationFailure, thrown);
		Assert.Equal(1, owner.ReleaseCount);
	}

	[Fact]
	public void AuthorityTransferWithInjectedFailureAndThrowingReleaseReportsBothFailures()
	{
		InvalidOperationException publicationFailure = new("publication failed");
		InvalidOperationException releaseFailure = new("release failed");
		CountingOwner owner = new(releaseFailure: releaseFailure);

		OwnershipHandoffException exception = Assert.Throws<OwnershipHandoffException>(() =>
			OwnershipHandoff.Adopt<CountingOwner, object>(owner, _ => throw publicationFailure, CountingOwner.Release));

		Assert.Collection(
			exception.InnerExceptions,
			first => Assert.Same(publicationFailure, first),
			second => Assert.Same(releaseFailure, second));
		Assert.Equal(LeaseReleaseKind.Unknown, exception.ReleaseKind);
		Assert.Same(publicationFailure, exception.PublishFailure);
		Assert.Same(releaseFailure, exception.ReleaseFailure);
		Assert.Equal(1, owner.ReleaseCount);
	}

	/// <summary>
	///     The SDK's <c>ReleaseWithOutcome</c> never throws: an unconfirmed release is an outcome, which the handoff
	///     reports with the publication failure instead of hiding it.
	/// </summary>
	[Theory]
	[InlineData(TargetReleaseStatus.UnconfirmedAfterInvocation, LeaseReleaseKind.CleanupUnconfirmed)]
	[InlineData(TargetReleaseStatus.NotInvoked, LeaseReleaseKind.CleanupUnavailable)]
	[InlineData(TargetReleaseStatus.RefusedRuntimeChanged, LeaseReleaseKind.RefusedRuntimeChanged)]
	public void AuthorityTransferWithInjectedFailureAndUnconfirmedReleaseReportsTheReleaseKind(
		TargetReleaseStatus status, LeaseReleaseKind expectedKind)
	{
		InvalidOperationException publicationFailure = new("publication failed");
		CountingOwner owner = new(status);

		OwnershipHandoffException exception = Assert.Throws<OwnershipHandoffException>(() =>
			OwnershipHandoff.Adopt<CountingOwner, object>(owner, _ => throw publicationFailure, CountingOwner.Release));

		Assert.Same(publicationFailure, Assert.Single(exception.InnerExceptions));
		Assert.Equal(expectedKind, exception.ReleaseKind);
		Assert.Null(exception.ReleaseFailure);
		Assert.Equal(1, owner.ReleaseCount);
	}

	[Fact]
	public void AuthorityTransferOnSuccessNeverReleasesTheOwner()
	{
		CountingOwner owner = new();

		PublishedWrapper wrapper = OwnershipHandoff.Adopt(owner, static acquired => new PublishedWrapper(acquired),
			CountingOwner.Release);

		Assert.Same(owner, wrapper.Owner);
		Assert.Equal(0, owner.ReleaseCount);
	}

	[Fact]
	public void AuthorityTransferWithoutAPublisherReleasesTheOwnerOnce()
	{
		CountingOwner owner = new();

		Assert.Throws<ArgumentNullException>(() =>
			OwnershipHandoff.Adopt<CountingOwner, object>(owner, null!, CountingOwner.Release));

		Assert.Equal(1, owner.ReleaseCount);
	}

	[Fact]
	public void AuthorityTransferRejectsAMissingOwner()
	{
		Assert.Throws<ArgumentNullException>(() =>
			OwnershipHandoff.Adopt<CountingOwner, object>(null!, static _ => new object(), CountingOwner.Release));
	}

	[Fact]
	public void AuthorityTransferRejectsAMissingReleaseBeforePublishing()
	{
		CountingOwner owner = new();
		bool published = false;

		Assert.Throws<ArgumentNullException>(() => OwnershipHandoff.Adopt<CountingOwner, object>(owner, _ =>
		{
			published = true;
			return new object();
		}, null!));

		Assert.False(published);
		Assert.Equal(0, owner.ReleaseCount);
	}

	private sealed class CountingOwner(
		TargetReleaseStatus status = TargetReleaseStatus.Released,
		Exception? releaseFailure = null)
	{
		internal int ReleaseCount
		{
			get;
			private set;
		}

		/// <summary>Releases like the SDK adapter: one release, mapped through <see cref="SdkReleaseOutcomes" />.</summary>
		internal static LeaseReleaseOutcome Release(CountingOwner owner)
		{
			owner.ReleaseCount++;
			if (owner.ReleaseFailure is not null)
			{
				throw owner.ReleaseFailure;
			}

			return SdkReleaseOutcomes.FromTarget(owner.Status);
		}

		private TargetReleaseStatus Status
		{
			get;
		} = status;

		private Exception? ReleaseFailure
		{
			get;
		} = releaseFailure;
	}

	private sealed class PublishedWrapper(CountingOwner owner)
	{
		internal CountingOwner Owner
		{
			get;
		} = owner;
	}
}
