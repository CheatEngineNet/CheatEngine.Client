using System.Collections.Immutable;

using CheatEngine.Client.Debugger;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Debugger;

public sealed class DebuggerContractsTests
{
	[Fact]
	public void BreakpointRequestPreservesAddressAndKind()
	{
		BreakpointRequest request = new(0x401000, BreakpointKind.Access);

		Assert.Equal((Address) 0x401000, request.Address);
		Assert.Equal(BreakpointKind.Access, request.Kind);
	}

	[Fact]
	public void BreakpointRequestRejectsAnUndefinedKind()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new BreakpointRequest(0x401000, (BreakpointKind) 99));
	}

	[Fact]
	public void BreakpointEventRequiresInitializedCopiedRegisterSnapshots()
	{
		Assert.Throws<ArgumentException>(() => new BreakpointEvent(0x401000, 1, default));

		ImmutableArray<BreakpointRegisterSnapshot> registers = [new("RAX", 42)];
		BreakpointEvent breakpointEvent = new(0x401000, 1, registers);

		Assert.Equal((Address) 0x401000, breakpointEvent.Address);
		Assert.Equal(1, breakpointEvent.ThreadId);
		Assert.Equal(registers, breakpointEvent.Registers);
	}

	[Fact]
	public void BreakpointEventRejectsANegativeThreadIdentifier()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new BreakpointEvent(
			0x401000,
			-1,
			ImmutableArray<BreakpointRegisterSnapshot>.Empty));
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	public void RegisterSnapshotRejectsABlankName(string name)
	{
		Assert.Throws<ArgumentException>(() => new BreakpointRegisterSnapshot(name, 0));
	}
}
