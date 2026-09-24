using CheatEngine.Client.Allocations;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Allocations;

public sealed class AllocationContractsTests
{
	[Fact]
	public void RequestPreservesTheExactSizeProtectionAndPreferredAddress()
	{
		Address preferred = new(0x1_4000_0000);

		AllocationRequest request = new(4096, AllocationProtection.ExecuteReadWrite, preferred);

		Assert.Equal(4096, request.Size);
		Assert.Equal(AllocationProtection.ExecuteReadWrite, request.Protection);
		Assert.Equal(preferred, request.PreferredAddress);
	}

	[Fact]
	public void RequestDefaultsToReadWriteMemoryAnywhere()
	{
		AllocationRequest request = new(16);

		Assert.Equal(AllocationProtection.ReadWrite, request.Protection);
		Assert.Null(request.PreferredAddress);
		Assert.Equal(AllocationProtection.ReadWrite, default(AllocationProtection));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void RequestRejectsANonPositiveSize(long size)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new AllocationRequest(size));
	}

	[Fact]
	public void RequestRejectsAnUndefinedProtection()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new AllocationRequest(1, (AllocationProtection) 99));
	}

	[Fact]
	public void RequestRejectsTheNullAddressAsAPreference()
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(() => new AllocationRequest(1, preferredAddress: Address.Zero));

		Assert.Equal("preferredAddress", exception.ParamName);
	}

	[Fact]
	public void TheDefaultRequestHasNoSize()
	{
		AllocationRequest request = default;

		Assert.Equal(0, request.Size);
		Assert.Equal(AllocationProtection.ReadWrite, request.Protection);
		Assert.Null(request.PreferredAddress);
	}
}
