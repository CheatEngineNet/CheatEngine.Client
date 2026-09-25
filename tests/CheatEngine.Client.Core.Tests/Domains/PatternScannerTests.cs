using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class PatternScannerTests
{
	private const ScanProtectionRequirement Unspecified = ScanProtectionRequirement.Unspecified;
	private const ScanProtectionRequirement Required = ScanProtectionRequirement.Required;
	private const ScanProtectionRequirement Excluded = ScanProtectionRequirement.Excluded;
	private const ScanProtectionRequirement Any = ScanProtectionRequirement.Any;

	[Fact]
	public void TryValidateRequestRejectsTheDefaultStructBeforeAnyDispatcherOrSdkOperation()
	{
		bool valid = PatternScanner.TryValidateRequest(default, out CheatEngineFailure failure);

		Assert.False(valid);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
	}

	[Fact]
	public void TryValidateRequestAcceptsAConstructedBoundedRequest()
	{
		AobScanRequest request = new(new AobPattern("90"), 1);

		bool valid = PatternScanner.TryValidateRequest(request, out CheatEngineFailure failure);

		Assert.True(valid);
		Assert.Equal(default, failure);
	}

	/// <summary>
	///     The Client-owned options reach Cheat Engine as its own protection text and fast-scan method; the default filter
	///     is the empty text, the SDK-documented "find everything" value, never an omitted argument.
	/// </summary>
	[Theory]
	[InlineData(Unspecified, Unspecified, Unspecified, "")]
	[InlineData(Required, Excluded, Excluded, "+X-C-W")]
	[InlineData(Unspecified, Excluded, Required, "-C+W")]
	[InlineData(Any, Any, Any, "*X*C*W")]
	[InlineData(Excluded, Unspecified, Unspecified, "-X")]
	public void TheProtectionFilterBecomesCheatEngineProtectionText(ScanProtectionRequirement executable,
		ScanProtectionRequirement copyOnWrite, ScanProtectionRequirement writable, string expected)
	{
		AobScanOptions options = AobScanMapping.ToSdkOptions(
			new ScanProtectionFilter(executable, copyOnWrite, writable), ScanAlignment.None);

		Assert.Equal(expected, options.ProtectionFlags);
		Assert.Equal(FastScanMethod.NotAligned, options.AlignmentMethod);
		Assert.Null(options.AlignmentParameter);
	}

	[Fact]
	public void TheAlignmentRuleBecomesCheatEngineFastScanMethodAndParameter()
	{
		AobScanOptions aligned = AobScanMapping.ToSdkOptions(default, ScanAlignment.AlignedTo(16));
		AobScanOptions lastDigits = AobScanMapping.ToSdkOptions(default, ScanAlignment.LastDigits("f0"));

		Assert.Equal(FastScanMethod.Aligned, aligned.AlignmentMethod);
		Assert.Equal("16", aligned.AlignmentParameter);
		Assert.Equal(FastScanMethod.LastDigits, lastDigits.AlignmentMethod);
		Assert.Equal("F0", lastDigits.AlignmentParameter);
		Assert.Equal(string.Empty, lastDigits.ProtectionFlags);
	}

	/// <summary>Both routes send the same explicit protection text for the default filter.</summary>
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void TheDefaultFilterIsTheSameEmptyProtectionTextOnBothRoutes(bool bounded)
	{
		FakeAobScanPort port = new(new RecordingAobMatchList(["4010"]))
		{
			Modules = [new ModuleInfo("game.exe", 0x4000, new MemorySize(0x100), true, "game.exe")],
			Selection = bounded ? AobHosts.Local() : default,
			BoundedRows = [0x4010]
		};
		PatternScanner scanner = new(
			new SdkMainThreadDispatcher(InertCoreLifetime.Create(), new InlineMainThreadInvoker()), port);

		Assert.True(scanner.TryScan(new AobScanRequest(new AobPattern("90"), 1, new ModuleName("game.exe")), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken), failure.Message);

		AobScanOptions options = Assert.NotNull(port.LastOptions);
		Assert.Equal(string.Empty, options.ProtectionFlags);
		Assert.Equal(FastScanMethod.NotAligned, options.AlignmentMethod);
		Assert.Equal(bounded ? 1 : 0, port.BoundedCalls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void TheOptionsReachBothRoutes(bool bounded)
	{
		FakeAobScanPort port = new(new RecordingAobMatchList(["4010"]))
		{
			Modules = [new ModuleInfo("game.exe", 0x4000, new MemorySize(0x100), true, "game.exe")],
			Selection = bounded ? AobHosts.Local() : default,
			BoundedRows = [0x4010]
		};
		PatternScanner scanner = new(
			new SdkMainThreadDispatcher(InertCoreLifetime.Create(), new InlineMainThreadInvoker()), port);
		AobScanRequest request = new(new AobPattern("90"), 1, new ModuleName("game.exe"), null,
			new ScanProtectionFilter(Required, Excluded, Excluded), ScanAlignment.AlignedTo(4));

		Assert.True(scanner.TryScan(request, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken), failure.Message);

		AobScanOptions options = Assert.NotNull(port.LastOptions);
		Assert.Equal("+X-C-W", options.ProtectionFlags);
		Assert.Equal(FastScanMethod.Aligned, options.AlignmentMethod);
		Assert.Equal("4", options.AlignmentParameter);
		Assert.Equal(bounded ? 1 : 0, port.BoundedCalls);
	}

	/// <summary>Only a tampered value can be undefined; it is refused before dispatch instead of reaching Cheat Engine.</summary>
	[Theory]
	[InlineData("Protection")]
	[InlineData("AlignmentKind")]
	[InlineData("AlignmentDivisor")]
	public void TryValidateRequestRefusesATamperedOptionValue(string tampered)
	{
		ScanProtectionFilter protection = tampered == "Protection"
			? TamperedValues.WithBackingField(default(ScanProtectionFilter), nameof(ScanProtectionFilter.Writable),
				(ScanProtectionRequirement) 9)
			: default;
		ScanAlignment alignment = tampered switch
		{
			"AlignmentKind" => TamperedValues.WithBackingField(ScanAlignment.None, nameof(ScanAlignment.Mode),
				(ScanAlignmentMode) 9),
			"AlignmentDivisor" => TamperedValues.WithBackingField(ScanAlignment.AlignedTo(4),
				nameof(ScanAlignment.Mode), ScanAlignmentMode.None),
			_ => default
		};

		bool valid = PatternScanner.TryValidateRequest(
			new AobScanRequest(new AobPattern("90"), 1, null, null, protection, alignment),
			out CheatEngineFailure failure);

		Assert.False(valid);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
	}
}
