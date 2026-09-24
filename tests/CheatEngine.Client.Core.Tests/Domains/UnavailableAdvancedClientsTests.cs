using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Domains.Allocations;
using CheatEngine.Client.Core.Domains.Assembly;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class UnavailableAdvancedClientsTests
{
	private static readonly Address TestAddress = new(0x401000);

	[Fact]
	[Trait("Qualification", "Q44")]
	public void AllocationTryOperationReportsTheLiveGateAndLeavesNoLease()
	{
		UnavailableAllocationClient client = new();

		bool succeeded = client.TryAllocate(new TargetAllocationRequest(4096), out ITargetMemoryLease? lease,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal("Allocations.Allocate", failure.Operation);
		Assert.Contains("Cheat Engine 7.7 x64 live gate", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void AllocationThrowingOperationPreservesTheUnavailableFailure()
	{
		UnavailableAllocationClient client = new();

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			_ = client.Allocate(new TargetAllocationRequest(1), TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void AssemblyTryOperationsReturnDefaultsAndAStableCapabilityFailure()
	{
		UnavailableAssemblyClient client = new();

		Assert.False(client.TryDisassemble(TestAddress, out AssemblyInstructionSnapshot instruction,
			out CheatEngineFailure disassemble,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, instruction);
		Assert.Equal("Assembly.Disassemble", disassemble.Operation);

		Assert.False(client.TryGetInstructionSize(TestAddress, out int size, out CheatEngineFailure sizeFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(0, size);
		Assert.Equal("Assembly.GetInstructionSize", sizeFailure.Operation);

		Assert.False(client.TryGetPreviousInstruction(TestAddress, out Address previous,
			out CheatEngineFailure previousFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, previous);
		Assert.Equal("Assembly.GetPreviousInstruction", previousFailure.Operation);

		Assert.False(client.TryGetComment(TestAddress, out string? comment, out CheatEngineFailure commentFailure,
			TestContext.Current.CancellationToken));
		Assert.Null(comment);
		Assert.Equal("Assembly.GetComment", commentFailure.Operation);

		Assert.False(client.TryAssemble(new AssemblyInstructionRequest(TestAddress, "nop"), out _,
			out CheatEngineFailure assemble,
			TestContext.Current.CancellationToken));
		Assert.Equal("Assembly.Assemble", assemble.Operation);

		Assert.False(client.TryApplyPatch(new AutoAssemblerScript("[ENABLE]\n[DISABLE]"),
			out IAutoAssemblerPatchLease? patch, out CheatEngineFailure patchFailure,
			TestContext.Current.CancellationToken));
		Assert.Null(patch);
		Assert.Equal("Assembly.ApplyPatch", patchFailure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void AssemblyThrowingOperationPreservesTheUnavailableFailure()
	{
		UnavailableAssemblyClient client = new();

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
		{
			_ = client.Disassemble(TestAddress, TestContext.Current.CancellationToken);
		});

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, exception.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void CancellationTakesPrecedenceOverTheCapabilityGate()
	{
		UnavailableAllocationClient client = new();
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		bool succeeded = client.TryAllocate(new TargetAllocationRequest(1), out ITargetMemoryLease? lease,
			out CheatEngineFailure failure, cancellation.Token);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal("Allocations.Allocate", failure.Operation);
	}
}
