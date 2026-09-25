using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class PatternScannerTests
{
	private const ScanProtectionRequirement Unspecified = ScanProtectionRequirement.Unspecified;
	private const ScanProtectionRequirement Required = ScanProtectionRequirement.Required;
	private const ScanProtectionRequirement Excluded = ScanProtectionRequirement.Excluded;
	private const ScanProtectionRequirement Any = ScanProtectionRequirement.Any;

	[Fact]
	public void ValidateRequestThrowsForTheDefaultStructBeforeAnyDispatcherOrSdkOperation()
	{
		ArgumentException thrown = Assert.Throws<ArgumentException>(() => PatternScanner.ValidateRequest(default));

		Assert.Equal("request", thrown.ParamName);
	}

	[Fact]
	public void ValidateRequestAcceptsAConstructedBoundedRequest()
	{
		AobScanRequest request = new(new AobPattern("90"), 1);

		Exception? thrown = Record.Exception(() => PatternScanner.ValidateRequest(request));

		Assert.Null(thrown);
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

	/// <summary>
	///     The default request, and a request whose field or option was tampered with, are programming errors: every
	///     entry point throws what the request's constructor throws for the same value, before any Cheat Engine call.
	/// </summary>
	[Theory]
	[InlineData("Default", typeof(ArgumentException))]
	[InlineData("NegativeLimit", typeof(ArgumentOutOfRangeException))]
	[InlineData("EmptyModule", typeof(ArgumentException))]
	[InlineData("InvertedRange", typeof(ArgumentOutOfRangeException))]
	[InlineData("Protection", typeof(ArgumentOutOfRangeException))]
	[InlineData("AlignmentKind", typeof(ArgumentOutOfRangeException))]
	[InlineData("AlignmentDivisor", typeof(ArgumentOutOfRangeException))]
	public void AnInvalidRequestThrowsFromEveryEntryPointBeforeAnyCheatEngineCall(string invalid, Type expected)
	{
		FakeAobScanPort port = new(new RecordingAobMatchList(["4010"]));
		PatternScanner scanner = new(
			new SdkMainThreadDispatcher(InertCoreLifetime.Create(), new InlineMainThreadInvoker()), port);
		AobScanRequest request = CreateInvalidRequest(invalid);
		CancellationToken token = TestContext.Current.CancellationToken;

		Exception[] thrown =
		[
			Assert.ThrowsAny<ArgumentException>(() => scanner.TryScan(request, out _, out _, token)),
			Assert.ThrowsAny<ArgumentException>(() => scanner.Scan(request, token)),
			Assert.ThrowsAny<ArgumentException>(() => scanner.ScanDetailed(request, token))
		];

		Assert.All(thrown, exception =>
		{
			Assert.IsType(expected, exception);
			Assert.Equal("request", ((ArgumentException) exception).ParamName);
		});
		Assert.Equal(0, port.EnumerationCalls);
		Assert.Equal(0, port.SelectionCalls);
		Assert.Equal(0, port.ScanCalls);
	}

	private static AobScanRequest CreateInvalidRequest(string invalid)
	{
		AobScanRequest valid = new(new AobPattern("90"), 1);
		return invalid switch
		{
			"Default" => default,
			"NegativeLimit" => TamperedValues.WithBackingField(valid, nameof(AobScanRequest.MaximumResults), -1),
			"EmptyModule" => TamperedValues.WithBackingField(valid, nameof(AobScanRequest.Module),
				(ModuleName?) default(ModuleName)),
			"InvertedRange" => TamperedValues.WithBackingField(valid, nameof(AobScanRequest.Range),
				(AobScanRange?) TamperedValues.WithBackingField(new AobScanRange(new Address(0x10), new Address(0x10)),
					nameof(AobScanRange.Start), new Address(0x20))),
			"Protection" => TamperedValues.WithBackingField(valid, nameof(AobScanRequest.Protection),
				TamperedValues.WithBackingField(default(ScanProtectionFilter), nameof(ScanProtectionFilter.Writable),
					(ScanProtectionRequirement) 9)),
			"AlignmentKind" => TamperedValues.WithBackingField(valid, nameof(AobScanRequest.Alignment),
				TamperedValues.WithBackingField(ScanAlignment.None, nameof(ScanAlignment.Mode), (ScanAlignmentMode) 9)),
			"AlignmentDivisor" => TamperedValues.WithBackingField(valid, nameof(AobScanRequest.Alignment),
				TamperedValues.WithBackingField(ScanAlignment.AlignedTo(4), nameof(ScanAlignment.Mode),
					ScanAlignmentMode.None)),
			_ => throw new ArgumentOutOfRangeException(nameof(invalid), invalid, null)
		};
	}
}
