using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class SdkTableRecordMutationPortTests
{
	[Fact]
	public void TrySetParentRejectsASelfReferentialRelationshipBeforeAccessingTheAddressList()
	{
		SdkTableRecordMutationPort port = new();
		MemoryRecordId id = new(42);

		TableRecordMutationStatus status = port.TrySetParent(id, id, out MemoryRecordSnapshot record);

		Assert.Equal(TableRecordMutationStatus.InvalidRelationship, status);
		Assert.Equal(default, record);
	}
}
