using CheatEngine.Client.Allocations;

namespace CheatEngine.Client.Abstractions.Tests.Allocations;

public sealed class AllocationContractsTests
{
	[Fact]
	public void RequestPreservesTheExactSizeAndAccess()
	{
		TargetAllocationRequest request = new(4096, TargetAllocationAccess.ExecuteReadWrite);

		Assert.Equal(4096, request.Size);
		Assert.Equal(TargetAllocationAccess.ExecuteReadWrite, request.Access);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void RequestRejectsANonPositiveSize(long size)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new TargetAllocationRequest(size));
	}

	[Fact]
	public void RequestRejectsAnUndefinedAccess()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new TargetAllocationRequest(1, (TargetAllocationAccess) 99));
	}
}
