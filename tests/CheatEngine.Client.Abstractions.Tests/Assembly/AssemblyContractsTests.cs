#pragma warning disable CECLIENT5003 // The contract tests exercise the experimental instruction types.
#pragma warning disable CECLIENT5004 // The contract tests exercise the experimental Auto Assembler script type.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using CheatEngine.Client.Assembly;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Assembly;

public sealed class AssemblyContractsTests
{
	private const string InstructionDiagnosticId = "CECLIENT5003";

	private const string UrlFormat =
		"https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Abstractions/README.md#{0}";

	[Fact]
	public void InstructionSnapshotCopiesTheCallerByteSpanAndTheDisassemblerColumns()
	{
		byte[] bytes = [0x8B, 0x45, 0x08];
		AssemblyInstructionSnapshot snapshot = new(0x401000, 3, "00401000", "mov eax,[ebp+08]", "", bytes);
		bytes[0] = 0xCC;

		Assert.Equal((Address) 0x401000, snapshot.Address);
		Assert.Equal(3, snapshot.Length);
		Assert.Equal("00401000", snapshot.AddressText);
		Assert.Equal("mov eax,[ebp+08]", snapshot.Opcode);
		Assert.Equal(string.Empty, snapshot.Extra);
		Assert.Equal("mov eax,[ebp+08]", snapshot.Text);
		Assert.Equal([0x8B, 0x45, 0x08], snapshot.Bytes);
		Assert.Equal(snapshot.Length, snapshot.Bytes.Length);
	}

	[Theory]
	[InlineData("", "call 00402000")]
	[InlineData("  ", "call 00402000")]
	[InlineData("->game.exe+2000", "call 00402000 ->game.exe+2000")]
	public void InstructionTextJoinsTheOpcodeAndANonBlankAnnotation(string extra, string expected)
	{
		AssemblyInstructionSnapshot snapshot = new(0x401000, 5, "00401000", "call 00402000", extra,
			[0xE8, 0xFB, 0x0F, 0x00, 0x00]);

		Assert.Equal(expected, snapshot.Text);
		Assert.Equal(extra, snapshot.Extra);
	}

	[Fact]
	public void InstructionSnapshotRejectsEmptyAndMismatchedBytePayloads()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new AssemblyInstructionSnapshot(0x401000, 0, "00401000", "nop", "", [0x90]));
		Assert.Throws<ArgumentException>(() => new AssemblyInstructionSnapshot(0x401000, 1, "00401000", " ", "", [0x90]));
		Assert.Throws<ArgumentNullException>(() =>
			new AssemblyInstructionSnapshot(0x401000, 1, null!, "nop", "", [0x90]));
		Assert.Throws<ArgumentNullException>(() =>
			new AssemblyInstructionSnapshot(0x401000, 1, "00401000", "nop", null!, [0x90]));
		Assert.Throws<ArgumentException>(() => new AssemblyInstructionSnapshot(0x401000, 1, "00401000", "nop", "", []));
		Assert.Throws<ArgumentException>(() =>
			new AssemblyInstructionSnapshot(0x401000, 2, "00401000", "nop", "", [0x90]));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void InstructionRequestRejectsBlankSource(string source)
	{
		Assert.Throws<ArgumentException>(() => new AssemblyInstructionRequest(0x401000, source));
	}

	[Fact]
	public void InstructionRequestPreservesTheAssemblyOriginSourceAndDefaults()
	{
		AssemblyInstructionRequest request = new(0x401010, "mov eax, 1");

		Assert.Equal((Address) 0x401010, request.Address);
		Assert.Equal("mov eax, 1", request.Instruction);
		Assert.Equal(InstructionEncodingPreference.None, request.Preference);
		Assert.False(request.SkipRangeCheck);
	}

	[Fact]
	public void InstructionRequestKeepsItsEncodingPreferenceAndRangeCheckOption()
	{
		AssemblyInstructionRequest request = new(0x401010, "jmp 00401100", InstructionEncodingPreference.Far, true);

		Assert.Equal(InstructionEncodingPreference.Far, request.Preference);
		Assert.True(request.SkipRangeCheck);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(4)]
	public void InstructionRequestRejectsAnUndefinedEncodingPreference(int preference)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new AssemblyInstructionRequest(0x401000, "nop", (InstructionEncodingPreference) preference));
	}

	[Fact]
	public void EncodingPreferencesMirrorCheatEngineAssemblerPreferences()
	{
		(string Name, int Value)[] expected =
		[
			(nameof(InstructionEncodingPreference.None), 0), (nameof(InstructionEncodingPreference.Short), 1),
			(nameof(InstructionEncodingPreference.Long), 2), (nameof(InstructionEncodingPreference.Far), 3)
		];

		Assert.Equal(expected,
			Enum.GetValues<InstructionEncodingPreference>().Select(static value => (value.ToString(), (int) value)));
		Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(InstructionEncodingPreference)));
	}

	[Theory]
	[InlineData(typeof(IAssemblyClient))]
	[InlineData(typeof(AssemblyInstructionRequest))]
	[InlineData(typeof(AssemblyInstructionSnapshot))]
	[InlineData(typeof(InstructionEncodingPreference))]
	public void EveryInstructionTypeIsExperimentalUnderItsDocumentedId(Type type)
	{
		ExperimentalAttribute experimental = Assert.Single(type.GetCustomAttributes<ExperimentalAttribute>());

		Assert.Equal(InstructionDiagnosticId, experimental.DiagnosticId);
		Assert.Equal(UrlFormat, experimental.UrlFormat);
	}

	[Fact]
	public void TheClientAssemblyPropertyIsExperimentalUnderTheInstructionId()
	{
		PropertyInfo property = typeof(ICheatEngineClient).GetProperty(nameof(ICheatEngineClient.Assembly))!;

		ExperimentalAttribute experimental = Assert.Single(property.GetCustomAttributes<ExperimentalAttribute>());

		Assert.Equal(typeof(IAssemblyClient), property.PropertyType);
		Assert.Equal(InstructionDiagnosticId, experimental.DiagnosticId);
		Assert.Equal(UrlFormat, experimental.UrlFormat);
	}

	[Fact]
	public void TheInstructionClientOffersTheFourOperationFamiliesInTryAndThrowingForms()
	{
		string[] expected =
		[
			"Assemble", "Disassemble", "GetInstructionLength", "GetPreviousInstructionAddress", "TryAssemble",
			"TryDisassemble", "TryGetInstructionLength", "TryGetPreviousInstructionAddress"
		];

		Assert.Equal(expected,
			typeof(IAssemblyClient).GetMethods().Select(static method => method.Name).Order(StringComparer.Ordinal));
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
