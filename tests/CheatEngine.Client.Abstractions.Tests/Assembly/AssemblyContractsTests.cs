#pragma warning disable CECLIENT5004 // The contract tests exercise the experimental Auto Assembler script type.

using CheatEngine.Client.Assembly;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Assembly;

public sealed class AssemblyContractsTests
{
	[Fact]
	public void InstructionSnapshotCopiesTheCallerByteSpan()
	{
		byte[] bytes = [0x90, 0x90];
		AssemblyInstructionSnapshot snapshot = new(0x401000, 2, "nop", bytes);
		bytes[0] = 0xCC;

		Assert.Equal((Address) 0x401000, snapshot.Address);
		Assert.Equal(2, snapshot.Length);
		Assert.Equal("nop", snapshot.Text);
		Assert.Equal([0x90, 0x90], snapshot.Bytes);
	}

	[Fact]
	public void InstructionSnapshotRejectsEmptyAndMismatchedBytePayloads()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new AssemblyInstructionSnapshot(0x401000, 0, "nop", [0x90]));
		Assert.Throws<ArgumentException>(() => new AssemblyInstructionSnapshot(0x401000, 1, " ", [0x90]));
		Assert.Throws<ArgumentException>(() => new AssemblyInstructionSnapshot(0x401000, 1, "nop", []));
		Assert.Throws<ArgumentException>(() => new AssemblyInstructionSnapshot(0x401000, 2, "nop", [0x90]));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void InstructionRequestRejectsBlankSource(string source)
	{
		Assert.Throws<ArgumentException>(() => new AssemblyInstructionRequest(0x401000, source));
	}

	[Fact]
	public void InstructionRequestPreservesTheAssemblyOriginAndSource()
	{
		AssemblyInstructionRequest request = new(0x401010, "mov eax, 1");

		Assert.Equal((Address) 0x401010, request.Address);
		Assert.Equal("mov eax, 1", request.Instruction);
	}

	[Fact]
	public void AutoAssemblerScriptPreservesItsOptionalDiagnosticName()
	{
		AutoAssemblerScript script = new("[ENABLE]\nnop", "sample.patch");

		Assert.Equal("[ENABLE]\nnop", script.Source);
		Assert.Equal("sample.patch", script.Name);
	}

	[Theory]
	[InlineData("")]
	[InlineData("  ")]
	public void AutoAssemblerScriptRejectsBlankSourceAndName(string blank)
	{
		Assert.Throws<ArgumentException>(() => new AutoAssemblerScript(blank));
		Assert.Throws<ArgumentException>(() => new AutoAssemblerScript("nop", blank));
	}
}
