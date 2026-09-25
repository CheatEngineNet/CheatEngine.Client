using System.Collections.Immutable;

using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Fluent.Tests.Scanning;

public sealed class AobFluentBuilderTests
{
	private static readonly ScanProtectionFilter ExecutableCode = new(ScanProtectionRequirement.Required,
		ScanProtectionRequirement.Excluded, ScanProtectionRequirement.Excluded);

	[Fact]
	public void ConfigurationMethodsReturnNewBuilderWithoutChangingTheOriginal()
	{
		FakePatternScanner scanner = new();
		AobScanBuilder original = scanner.Aob("48 8B ?? 89");

		AobScanBuilder configured = original.InModule("game.exe").InRange(0x400000, 0x4FFFFF).Executable()
			.AlignedTo(4);

		Assert.Null(original.Module);
		Assert.Null(original.Range);
		Assert.True(original.Protection.IsUnspecified);
		Assert.Equal(ScanAlignment.None, original.Alignment);
		Assert.Equal("game.exe", configured.Module!.Value.Value);
		Assert.Equal(new AobScanRange(0x400000, 0x4FFFFF), configured.Range);
		Assert.Equal(ExecutableCode, configured.Protection);
		Assert.Equal(ScanAlignment.AlignedTo(4), configured.Alignment);
		Assert.Equal("48 8B ?? 89", configured.Pattern.Value);
	}

	[Fact]
	public void TheProtectionPresetsNameExecutableCodeAndWritableData()
	{
		FakePatternScanner scanner = new();

		AobScanBuilder executable = scanner.Aob("90").Executable();
		AobScanBuilder writable = scanner.Aob("90").Executable().Writable();

		Assert.Equal(ExecutableCode, executable.Protection);
		Assert.Equal(new ScanProtectionFilter(ScanProtectionRequirement.Unspecified, ScanProtectionRequirement.Excluded,
			ScanProtectionRequirement.Required), writable.Protection);
	}

	[Fact]
	public void RequireSingleUsesTwoResultLimitAndReturnsTheOnlyMatch()
	{
		Address expected = 0x401000;
		FakePatternScanner scanner = new(new AobScanResult([expected], false));

		Address actual = scanner.Aob("90 90").InModule("game.exe").RequireSingle()
			.Execute(TestContext.Current.CancellationToken);

		Assert.Equal(expected, actual);
		AobScanRequest? request = scanner.LastRequest;
		Assert.NotNull(request);
		Assert.Equal(2, request.Value.MaximumResults);
		Assert.Equal("game.exe", request.Value.Module!.Value.Value);
	}

	[Fact]
	public void FluentFiltersNormalizeAndForwardProtectionAlignmentAndRange()
	{
		Address expected = 0x401000;
		FakePatternScanner scanner = new(new AobScanResult([expected], false));
		ScanProtectionFilter protection = new(ScanProtectionRequirement.Any, ScanProtectionRequirement.Excluded,
			ScanProtectionRequirement.Required);

		Address actual = scanner.Aob("90")
			.WithProtection(protection)
			.WithLastDigits("f0")
			.InRange(0x400000, 0x4FFFFF)
			.RequireSingle()
			.Execute(TestContext.Current.CancellationToken);

		Assert.Equal(expected, actual);
		AobScanRequest request = Assert.IsType<AobScanRequest>(scanner.LastRequest);
		Assert.Equal(protection, request.Protection);
		Assert.Equal(ScanAlignmentMode.LastDigits, request.Alignment.Mode);
		Assert.Equal("F0", request.Alignment.Digits);
		Assert.Equal(new AobScanRange(0x400000, 0x4FFFFF), request.Range);
	}

	[Theory]
	[InlineData("")]
	[InlineData("0xF0")]
	public void WithLastDigitsRejectsMalformedDigitsBeforeTerminalSelection(string digits)
	{
		FakePatternScanner scanner = new();

		Assert.Throws<ArgumentException>(() => scanner.Aob("90").WithLastDigits(digits));
		Assert.Null(scanner.LastRequest);
	}

	[Fact]
	public void AlignedToRejectsAnInvalidDivisorBeforeTerminalSelection()
	{
		FakePatternScanner scanner = new();

		Assert.Throws<ArgumentOutOfRangeException>(() => scanner.Aob("90").AlignedTo(0));
		Assert.Null(scanner.LastRequest);
	}

	[Fact]
	public void RequireSingleReportsAmbiguousMatchWhenTheBoundedResultIsTruncated()
	{
		Address expected = 0x401000;
		FakePatternScanner scanner = new(new AobScanResult([expected], true));

		bool succeeded = scanner.Aob("90").RequireSingle().TryExecute(
			out Address address, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, address);
		Assert.Equal(CheatEngineFailureKind.AmbiguousMatch, failure.Kind);
		Assert.Equal("Aob.RequireSingle", failure.Operation);
	}

	[Fact]
	public void FirstOrNoneUsesOneResultLimitAndTreatsNoMatchAsSuccess()
	{
		FakePatternScanner scanner = new(new AobScanResult(ImmutableArray<Address>.Empty, false));

		bool succeeded = scanner.Aob("90").FirstOrNone().TryExecute(
			out Address? address, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Null(address);
		Assert.Equal(default, failure);
		AobScanRequest? request = scanner.LastRequest;
		Assert.NotNull(request);
		Assert.Equal(1, request.Value.MaximumResults);
	}

	[Fact]
	public void FirstOrNoneRejectsAHostResponseBeyondItsOneMatchLimit()
	{
		FakePatternScanner scanner = new(new AobScanResult([0x401000, 0x402000], false));

		bool succeeded = scanner.Aob("90").FirstOrNone().TryExecute(
			out Address? address, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(address);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal("Aob.FirstOrNone", failure.Operation);
	}

	[Fact]
	public void RequireSingleRejectsAHostResponseBeyondItsTwoMatchLimit()
	{
		FakePatternScanner scanner = new(new AobScanResult([0x401000, 0x402000, 0x403000], false));

		bool succeeded = scanner.Aob("90").RequireSingle().TryExecute(
			out Address address, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, address);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal("Aob.RequireSingle", failure.Operation);
	}

	[Fact]
	public void TakeRejectsAHostResponseBeyondTheRequestedLimit()
	{
		Address first = 0x401000;
		Address second = 0x402000;
		FakePatternScanner scanner = new(new AobScanResult([first, second], false));

		bool succeeded = scanner.Aob("90").Take(1).TryExecute(
			out AobScanResult result, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal("Aob.Take", failure.Operation);
		AobScanRequest? request = scanner.LastRequest;
		Assert.NotNull(request);
		Assert.Equal(1, request.Value.MaximumResults);
	}

	[Fact]
	public void TryExecutePreservesFailureReturnedByThePatternScanner()
	{
		CheatEngineFailure expectedFailure = new(
			CheatEngineFailureKind.CapabilityUnavailable,
			"Patterns.Scan",
			"AOB scanning is unavailable.");
		FakePatternScanner scanner = new(expectedFailure);

		bool succeeded = scanner.Aob("90").Take(1).TryExecute(
			out _, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(expectedFailure, failure);
	}

	[Fact]
	public void FirstOrNoneExecuteReturnsTheFirstCopiedMatch()
	{
		Address expected = 0x401000;
		FakePatternScanner scanner = new(new AobScanResult([expected], false));

		Address? actual = scanner.Aob("90").FirstOrNone().Execute(TestContext.Current.CancellationToken);

		Assert.NotNull(actual);
		Assert.Equal(expected, actual.Value);
		Assert.Equal(1, Assert.IsType<AobScanRequest>(scanner.LastRequest).MaximumResults);
	}

	[Fact]
	public void FirstOrNoneExecuteThrowsThePatternScannerFailure()
	{
		CheatEngineFailure expectedFailure = new(
			CheatEngineFailureKind.CapabilityUnavailable,
			"Patterns.Scan",
			"AOB scanning is unavailable.");
		FakePatternScanner scanner = new(expectedFailure);

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			scanner.Aob("90").FirstOrNone().Execute(TestContext.Current.CancellationToken));

		Assert.Equal(expectedFailure, exception.Failure);
	}

	[Fact]
	public void DefaultFirstOrNoneBuilderRejectsExecution()
	{
		AobFirstMatchBuilder builder = default;

		Assert.Throws<InvalidOperationException>(() => builder.Execute(TestContext.Current.CancellationToken));
	}

	[Fact]
	public void TakeExecuteReturnsTheBoundedCopiedResult()
	{
		Address first = 0x401000;
		Address second = 0x402000;
		FakePatternScanner scanner = new(new AobScanResult([first, second], true));

		AobScanResult actual = scanner.Aob("90").Take(2).Execute(TestContext.Current.CancellationToken);

		Assert.Equal([first, second], actual.Matches);
		Assert.True(actual.IsTruncated);
		Assert.Equal(2, Assert.IsType<AobScanRequest>(scanner.LastRequest).MaximumResults);
	}

	[Fact]
	public void TakeExecuteThrowsThePatternScannerFailure()
	{
		CheatEngineFailure expectedFailure = new(
			CheatEngineFailureKind.CapabilityUnavailable,
			"Patterns.Scan",
			"AOB scanning is unavailable.");
		FakePatternScanner scanner = new(expectedFailure);

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			scanner.Aob("90").Take(1).Execute(TestContext.Current.CancellationToken));

		Assert.Equal(expectedFailure, exception.Failure);
	}

	[Fact]
	public void DefaultManyMatchBuilderRejectsExecution()
	{
		AobManyMatchBuilder builder = default;

		Assert.Throws<InvalidOperationException>(() => builder.Execute(TestContext.Current.CancellationToken));
	}

	[Fact]
	public void AobRejectsWhitespaceOnlyPatterns()
	{
		FakePatternScanner scanner = new();

		Assert.Throws<ArgumentException>(() => scanner.Aob(" \t\r\n "));
	}

	[Fact]
	public void DefaultAobScanBuilderRejectsTerminalSelection()
	{
		AobScanBuilder builder = default;

		Assert.Throws<InvalidOperationException>(() => builder.RequireSingle());
	}

	[Fact]
	public void InModuleRejectsTheDefaultModuleNameBeforeTerminalSelection()
	{
		FakePatternScanner scanner = new();

		Assert.Throws<ArgumentException>(() => scanner.Aob("90").InModule(default(ModuleName)));
		Assert.Null(scanner.LastRequest);
	}

	[Fact]
	public void RequireSingleReportsNotFoundForNoMatches()
	{
		FakePatternScanner scanner = new(new AobScanResult(ImmutableArray<Address>.Empty, false));

		bool succeeded = scanner.Aob("90").RequireSingle().TryExecute(
			out Address address, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, address);
		Assert.Equal(CheatEngineFailureKind.NotFound, failure.Kind);
		Assert.Equal("Aob.RequireSingle", failure.Operation);
	}

	[Fact]
	public void RequireSinglePreservesThePatternScannerFailure()
	{
		CheatEngineFailure expectedFailure = new(
			CheatEngineFailureKind.CapabilityUnavailable,
			"Patterns.Scan",
			"AOB scanning is unavailable.");
		FakePatternScanner scanner = new(expectedFailure);

		bool succeeded = scanner.Aob("90").RequireSingle().TryExecute(
			out Address address, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, address);
		Assert.Equal(expectedFailure, failure);
	}

	[Fact]
	public void RequireSingleExecuteThrowsWhenNoMatchExists()
	{
		FakePatternScanner scanner = new(new AobScanResult(ImmutableArray<Address>.Empty, false));

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			scanner.Aob("90").RequireSingle().Execute(TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.NotFound, exception.Failure.Kind);
		Assert.Equal("Aob.RequireSingle", exception.Failure.Operation);
	}

	[Fact]
	public void RequireSingleReportsAmbiguousMatchForTwoNonTruncatedResults()
	{
		FakePatternScanner scanner = new(new AobScanResult([0x401000, 0x402000], false));

		bool succeeded = scanner.Aob("90").RequireSingle().TryExecute(
			out Address address, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, address);
		Assert.Equal(CheatEngineFailureKind.AmbiguousMatch, failure.Kind);
	}

	[Fact]
	public void DefaultSingleMatchBuilderRejectsExecution()
	{
		AobSingleMatchBuilder builder = default;

		Assert.Throws<InvalidOperationException>(() => builder.Execute(TestContext.Current.CancellationToken));
	}

	[Fact]
	public void TheParsedPatternEntryPointForwardsTheNormalizedPatternUnchanged()
	{
		Address expected = 0x401000;
		FakePatternScanner scanner = new(new AobScanResult([expected], false));
		Assert.True(AobPattern.TryParse("48 8b ?? 89", out AobPattern pattern));

		AobScanBuilder builder = scanner.Aob(pattern);
		Address actual = builder.RequireSingle().Execute(TestContext.Current.CancellationToken);

		Assert.Equal(pattern, builder.Pattern);
		Assert.Equal(expected, actual);
		Assert.Equal("48 8B ?? 89", Assert.IsType<AobScanRequest>(scanner.LastRequest).Pattern.Value);
	}

	[Fact]
	public void TheAobEntryPointsRejectANullScannerAndAnEmptyPattern()
	{
		IPatternScanner scanner = null!;
		FakePatternScanner bound = new();

		Assert.Throws<ArgumentNullException>(() => scanner.Aob("90"));
		Assert.Throws<ArgumentNullException>(() => scanner.Aob(new AobPattern("90")));
		Assert.Throws<ArgumentNullException>(() => bound.Aob((string) null!));
		Assert.Throws<ArgumentException>(() => bound.Aob(default(AobPattern)));
		Assert.Null(bound.LastRequest);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void RequireSingleReportsAmbiguousWhenASecondHostMatchExists()
	{
		FakePatternScanner scanner = new(new AobScanResult([0x401000], true));

		bool succeeded = scanner.Aob("90").RequireSingle().TryExecute(out Address address,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, address);
		Assert.Equal(CheatEngineFailureKind.AmbiguousMatch, failure.Kind);
		Assert.Equal("Aob.RequireSingle", failure.Operation);
		Assert.Equal(2, scanner.LastRequest!.Value.MaximumResults);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void AobAmbiguousFalseIsNeverReportedAsNotFound()
	{
		CheatEngineFailure indeterminate = new(CheatEngineFailureKind.IndeterminateHostResult, "Patterns.Scan",
			"Cheat Engine returned no AOB result list: zero matches or a host failure " +
			"(indistinguishable on this scan route).", null, CheatEngineHostEffect.Completed);
		FakePatternScanner scanner = new(indeterminate);
		AobScanBuilder builder = scanner.Aob("90 90").InModule("game.exe");

		bool firstSucceeded = builder.FirstOrNone().TryExecute(out Address? first, out CheatEngineFailure firstFailure,
			TestContext.Current.CancellationToken);
		bool singleSucceeded = builder.RequireSingle().TryExecute(out _, out CheatEngineFailure singleFailure,
			TestContext.Current.CancellationToken);
		bool manySucceeded = builder.Take(3).TryExecute(out AobScanResult many, out CheatEngineFailure manyFailure,
			TestContext.Current.CancellationToken);

		Assert.False(firstSucceeded);
		Assert.Null(first);
		Assert.Equal(indeterminate, firstFailure);
		Assert.False(singleSucceeded);
		Assert.NotEqual(CheatEngineFailureKind.NotFound, singleFailure.Kind);
		Assert.Equal(indeterminate, singleFailure);
		Assert.False(manySucceeded);
		Assert.Equal(default, many);
		Assert.Equal(indeterminate, manyFailure);
		CheatEngineOperationException thrown = Assert.Throws<CheatEngineOperationException>(() =>
			builder.FirstOrNone().Execute(TestContext.Current.CancellationToken));
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, thrown.Failure.Kind);
	}

	/// <summary>Every throwing terminal raises the cancellation exception with the caller's token.</summary>
	[Theory]
	[InlineData("FirstOrNone")]
	[InlineData("Take")]
	[InlineData("RequireSingle")]
	public void ExecuteThrowsTheCancellationExceptionForACancelledScan(string terminal)
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		CheatEngineFailure cancelled = new(CheatEngineFailureKind.Cancelled, "Patterns.Scan",
			"The operation was cancelled before Cheat Engine work began.", null, CheatEngineHostEffect.NotStarted);
		AobScanBuilder builder = new FakePatternScanner(cancelled).Aob("90");

		CheatEngineOperationCanceledException exception = Assert.Throws<CheatEngineOperationCanceledException>(() =>
			Execute(builder, terminal, cancellation.Token));

		Assert.Equal(cancelled, exception.Failure);
		Assert.Equal(cancellation.Token, exception.CancellationToken);
	}

	/// <summary>Every throwing terminal keeps the dedicated activation-expired exception instead of a generic one.</summary>
	[Theory]
	[InlineData("FirstOrNone")]
	[InlineData("Take")]
	[InlineData("RequireSingle")]
	public void ExecuteThrowsTheActivationExpiredExceptionForAnExpiredScanner(string terminal)
	{
		CheatEngineFailure expired = new(CheatEngineFailureKind.ActivationExpired, "Patterns.Scan",
			"The Client activation has ended.", null, CheatEngineHostEffect.NotStarted);
		AobScanBuilder builder = new FakePatternScanner(expired).Aob("90");

		CheatEngineActivationExpiredException exception = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			Execute(builder, terminal, TestContext.Current.CancellationToken));

		Assert.Equal(expired, exception.Failure);
	}

	private static void Execute(AobScanBuilder builder, string terminal, CancellationToken cancellationToken)
	{
		switch (terminal)
		{
			case "FirstOrNone":
				_ = builder.FirstOrNone().Execute(cancellationToken);
				break;
			case "Take":
				_ = builder.Take(2).Execute(cancellationToken);
				break;
			default:
				_ = builder.RequireSingle().Execute(cancellationToken);
				break;
		}
	}

	private sealed class FakePatternScanner : IPatternScanner
	{
		private readonly CheatEngineFailure _failure;
		private readonly AobScanResult _result;
		private readonly bool _succeeds;

		internal FakePatternScanner()
			: this(new AobScanResult(ImmutableArray<Address>.Empty, false))
		{
		}

		internal FakePatternScanner(AobScanResult result)
		{
			_result = result;
			_succeeds = true;
		}

		internal FakePatternScanner(CheatEngineFailure failure)
		{
			_failure = failure;
		}

		internal AobScanRequest? LastRequest
		{
			get;
			private set;
		}

		public bool TryScan(AobScanRequest request, out AobScanResult result, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			LastRequest = request;
			result = _result;
			failure = _failure;
			return _succeeds;
		}

		public AobScanResult Scan(AobScanRequest request, CancellationToken cancellationToken = default)
		{
			if (TryScan(request, out AobScanResult result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw(cancellationToken);
			return default;
		}

		public PatternScanOutcome ScanDetailed(AobScanRequest request, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException("Fluent terminals use TryScan only.");
		}
	}
}
