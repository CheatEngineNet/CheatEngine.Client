using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     The production mutation port against the real CheatEngine.SDK 2.0.0 <c>AddressListMutations</c>, without a Lua
///     runtime: what the SDK decides before any Lua call reaches the port as an outcome, and its admission fault reaches
///     <see cref="TableClient" />, whose SDK boundary translates it (TryContractTests).
/// </summary>
public sealed class SdkTableRecordMutationPortTests
{
	[Fact]
	public void CheatEngineSdkRefusesASelfParentBeforeAnyLuaCall()
	{
		SdkTableRecordMutationPort port = new();
		MemoryRecordId id = new(42);

		TableRecordMutationOutcome outcome = port.TrySetParent(id, id, out MemoryRecordSnapshot record);

		Assert.Equal(TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.SelfParent), outcome);
		Assert.Equal(default, record);
	}

	[Fact]
	public void TheParentTraversalLimitIsTheBoundTheContractDocuments()
	{
		// The port always passes this bound, never the SDK default overload; ITableClient.TrySetParent documents it.
		Assert.Equal(4096, SdkTableRecordMutationPort.ParentTraversalHops);
	}

	[Fact]
	public void CommandsThatNeedTheLuaRuntimeLetItsAdmissionFaultReachTheSdkBoundary()
	{
		// No Lua runtime is attached in unit tests: AddressListMutations acquires its Lua operation itself and throws
		// InvalidOperationException, which the port neither catches nor classifies.
		SdkTableRecordMutationPort port = new();

		Assert.Throws<InvalidOperationException>(() => port.TryDelete(new MemoryRecordId(7)));
		Assert.Throws<InvalidOperationException>(() => port.TrySetActive(new MemoryRecordId(7), true));
		Assert.Throws<InvalidOperationException>(() =>
			port.TrySetParent(new MemoryRecordId(7), new MemoryRecordId(8), out _));
	}
}
