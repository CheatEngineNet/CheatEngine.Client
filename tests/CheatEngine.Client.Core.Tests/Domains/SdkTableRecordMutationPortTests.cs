using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
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

	[Fact]
	public void TheParentChainReadRefusesADetachedRuntimeBeforeReadingTheRecord()
	{
		// No Lua runtime is attached in unit tests: CheatEngine.SDK reports the admission as Detached, and the default
		// record is never read.
		LuaAdmissionRefusedException refused = Assert.Throws<LuaAdmissionRefusedException>(static () =>
			SdkTableRecordMutationPort.TryReadParent(default, out _));

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, refused.Failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, refused.Failure.HostEffect);
		Assert.Equal(CheatEngineFailureKind.ActivationExpired, CoreFailureFactory.GetKind(refused));
	}
}
