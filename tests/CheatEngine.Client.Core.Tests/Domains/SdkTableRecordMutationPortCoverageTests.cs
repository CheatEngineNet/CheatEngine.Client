using CheatEngine.Client.Core.Domains;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class SdkTableRecordMutationPortCoverageTests
{
	[Fact]
	public void ParentRelationshipGuardFailsClosedForAnUnknownLinkStatus()
	{
		TableRecordMutationStatus status = TableParentRelationshipGuard.Validate(new MemoryRecordId(42),
			new MemoryRecordId(12), 1,
			static _ => new ParentChainStep((ParentChainStepKind) 99, default));

		Assert.Equal(TableRecordMutationStatus.HostRejected, status);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void ParentRelationshipGuardRequiresAPositiveTraversalBound(int maximumHops)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => TableParentRelationshipGuard.Validate(new MemoryRecordId(42),
			new MemoryRecordId(12), maximumHops, static _ => ParentChainStep.Root));
	}
}
