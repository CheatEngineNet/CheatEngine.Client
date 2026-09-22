using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Infrastructure;

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
			OwnershipHandoff.Adopt<CountingOwner, object>(owner, _ => throw publicationFailure));

		Assert.Same(publicationFailure, thrown);
		Assert.Equal(1, owner.DisposeCount);
	}

	[Fact]
	public void AuthorityTransferWithInjectedFailureAndFailingReleaseReportsBothFailures()
	{
		InvalidOperationException publicationFailure = new("publication failed");
		InvalidOperationException releaseFailure = new("release failed");
		CountingOwner owner = new(releaseFailure);

		AggregateException exception = Assert.Throws<AggregateException>(() =>
			OwnershipHandoff.Adopt<CountingOwner, object>(owner, _ => throw publicationFailure));

		Assert.Collection(
			exception.InnerExceptions,
			first => Assert.Same(publicationFailure, first),
			second => Assert.Same(releaseFailure, second));
		Assert.Contains("release was not confirmed", exception.Message, StringComparison.Ordinal);
		Assert.Equal(1, owner.DisposeCount);
	}

	[Fact]
	public void AuthorityTransferOnSuccessNeverReleasesTheOwner()
	{
		CountingOwner owner = new();

		PublishedWrapper wrapper = OwnershipHandoff.Adopt(owner, static acquired => new PublishedWrapper(acquired));

		Assert.Same(owner, wrapper.Owner);
		Assert.Equal(0, owner.DisposeCount);
	}

	[Fact]
	public void AuthorityTransferWithoutAPublisherReleasesTheOwnerOnce()
	{
		CountingOwner owner = new();

		Assert.Throws<ArgumentNullException>(() => OwnershipHandoff.Adopt<CountingOwner, object>(owner, null!));

		Assert.Equal(1, owner.DisposeCount);
	}

	[Fact]
	public void AuthorityTransferRejectsAMissingOwner()
	{
		Assert.Throws<ArgumentNullException>(() =>
			OwnershipHandoff.Adopt<CountingOwner, object>(null!, static _ => new object()));
	}

	private sealed class CountingOwner(Exception? releaseFailure = null) : IDisposable
	{
		internal int DisposeCount
		{
			get;
			private set;
		}

		public void Dispose()
		{
			DisposeCount++;
			if (releaseFailure is not null)
			{
				throw releaseFailure;
			}
		}
	}

	private sealed class PublishedWrapper(CountingOwner owner)
	{
		internal CountingOwner Owner
		{
			get;
		} = owner;
	}
}
