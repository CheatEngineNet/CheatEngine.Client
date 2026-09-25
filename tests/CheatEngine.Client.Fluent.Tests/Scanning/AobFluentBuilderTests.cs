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
		Assert.Equal("Patterns.Scan", failure.Operation);
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
		Assert.Equal("Patterns.Scan", failure.Operation);
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
		Assert.Equal("Patterns.Scan", failure.Operation);
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
		Assert.Equal("Patterns.Scan", failure.Operation);
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
		Assert.Equal("Patterns.Scan", exception.Failure.Operation);
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

	/// <summary>
	///     Core's shape when a third in-request match proves the copy incomplete: two copied matches, truncated. The
	///     second copied match makes the result ambiguous.
	/// </summary>
	[Fact]
	[Trait("Qualification", "Q28")]
	public void RequireSingleReportsAmbiguousWhenASecondHostMatchExists()
	{
		FakePatternScanner scanner = new(new AobScanResult([0x401000, 0x402000], true));

		bool succeeded = scanner.Aob("90").RequireSingle().TryExecute(out Address address,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, address);
		Assert.Equal(CheatEngineFailureKind.AmbiguousMatch, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
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
		FakePatternScanner scanner = new(cancelled);
		AobScanBuilder builder = scanner.Aob("90");

		CheatEngineOperationCanceledException exception = Assert.Throws<CheatEngineOperationCanceledException>(() =>
			Execute(builder, terminal, cancellation.Token));

		Assert.IsAssignableFrom<OperationCanceledException>(exception);
		Assert.Equal(cancelled, exception.Failure);
		Assert.Equal(cancellation.Token, exception.CancellationToken);
		Assert.Equal(cancellation.Token, scanner.LastCancellationToken);
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

	/// <summary>
	///     A factual zero on each route: the bounded route's empty in-bounds result (with or without rows the Client's
	///     own scope check dropped), a managed-filter global list whose rows all lie outside the request, and an empty
	///     list that an unscoped global scan does return. Every row was read in each case.
	/// </summary>
	[Theory]
	[InlineData(PatternScanScope.HostBoundedRange, 0UL)]
	[InlineData(PatternScanScope.HostBoundedRange, 2UL)]
	[InlineData(PatternScanScope.GlobalHostScanWithManagedFilter, 3UL)]
	[InlineData(PatternScanScope.GlobalHostScan, 0UL)]
	public void FirstOrNoneReturnsNullForTheFactualZeroOfEveryRoute(PatternScanScope scope, ulong filteredOut)
	{
		FakePatternScanner scanner = new(new AobScanResult(ImmutableArray<Address>.Empty, false), scope, filteredOut);

		bool succeeded = scanner.Aob("90").FirstOrNone().TryExecute(out Address? address,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Null(address);
		Assert.True(failure.IsDefault);
		Assert.Equal(1, scanner.LastRequest!.Value.MaximumResults);
	}

	/// <summary>
	///     An empty copy that left rows unread proves nothing: no terminal turns it into <see langword="null" />,
	///     <see cref="CheatEngineFailureKind.NotFound" /> or an empty result. Core never publishes this shape (it fails a
	///     bounded destination filled with rows outside the request itself, and the global route reads every row of an
	///     empty copy), so this is the terminals' own guard.
	/// </summary>
	[Theory]
	[InlineData("FirstOrNone", PatternScanScope.HostBoundedRange)]
	[InlineData("FirstOrNone", PatternScanScope.GlobalHostScanWithManagedFilter)]
	[InlineData("RequireSingle", PatternScanScope.GlobalHostScanWithManagedFilter)]
	[InlineData("Take", PatternScanScope.GlobalHostScan)]
	public void AnEmptyCopyThatLeftRowsUnreadIsIndeterminate(string terminal, PatternScanScope scope)
	{
		FakePatternScanner scanner = new(new AobScanResult(ImmutableArray<Address>.Empty, false), scope, 4, 5);
		AobScanBuilder builder = scanner.Aob("90");

		bool succeeded;
		CheatEngineFailure failure;
		switch (terminal)
		{
			case "FirstOrNone":
				succeeded = builder.FirstOrNone().TryExecute(out Address? address, out failure,
					TestContext.Current.CancellationToken);
				Assert.Null(address);
				break;
			case "RequireSingle":
				succeeded = builder.RequireSingle().TryExecute(out _, out failure,
					TestContext.Current.CancellationToken);
				break;
			default:
				succeeded = builder.Take(3).TryExecute(out AobScanResult result, out failure,
					TestContext.Current.CancellationToken);
				Assert.Equal(default, result);
				break;
		}

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		CheatEngineOperationException thrown = Assert.Throws<CheatEngineOperationException>(() =>
			Execute(builder, terminal, TestContext.Current.CancellationToken));
		Assert.Equal(failure, thrown.Failure);
	}

	[Fact]
	public void FirstOrNoneReturnsTheFirstCopiedMatchEvenWhenRowsRemainUnread()
	{
		Address expected = 0x401000;
		FakePatternScanner scanner = new(new AobScanResult([expected], true),
			PatternScanScope.GlobalHostScanWithManagedFilter, 1, 7);

		Address? actual = scanner.Aob("90").InModule("game.exe").FirstOrNone()
			.Execute(TestContext.Current.CancellationToken);

		Assert.Equal(expected, actual);
	}

	/// <summary>
	///     One copied match with unread rows never proves uniqueness, and never proves a second match either.
	///     <c>true</c> is the shape Core's bounded route publishes when its full destination (three slots for a limit of
	///     two) held one match inside the request and two rows the Client dropped, with a row left unread: one copied
	///     match, truncated, not exhaustive. <c>false</c> is a guard: a shape Core never publishes.
	/// </summary>
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void RequireSingleNeedsEveryRowReadToProveUniqueness(bool truncated)
	{
		PatternScanMetrics metrics = truncated
			? new PatternScanMetrics(PatternScanScope.HostBoundedRange, 4, 3, 2, 1, 0, 0, 1, false,
				TimeSpan.FromMilliseconds(3), TimeSpan.FromMilliseconds(1))
			: new PatternScanMetrics(PatternScanScope.GlobalHostScanWithManagedFilter, 3, 1, 0, 1, 0, 0, 2, false,
				TimeSpan.FromMilliseconds(3), TimeSpan.FromMilliseconds(1));
		FakePatternScanner scanner = new(new AobScanResult([0x40FC], truncated), metrics);

		bool succeeded = scanner.Aob("90 90 90 90").InModule("game.exe").RequireSingle().TryExecute(
			out Address address, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, address);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Equal(2, scanner.LastRequest!.Value.MaximumResults);
	}

	[Fact]
	public void CardinalityFailuresFollowACompletedScan()
	{
		FakePatternScanner none = new(new AobScanResult(ImmutableArray<Address>.Empty, false));
		FakePatternScanner two = new(new AobScanResult([0x401000, 0x402000], false));

		_ = none.Aob("90").RequireSingle().TryExecute(out _, out CheatEngineFailure notFound,
			TestContext.Current.CancellationToken);
		_ = two.Aob("90").RequireSingle().TryExecute(out _, out CheatEngineFailure ambiguous,
			TestContext.Current.CancellationToken);
		_ = two.Aob("90").FirstOrNone().TryExecute(out _, out CheatEngineFailure beyondLimit,
			TestContext.Current.CancellationToken);

		Assert.Equal((CheatEngineFailureKind.NotFound, CheatEngineHostEffect.Completed),
			(notFound.Kind, notFound.HostEffect));
		Assert.Equal((CheatEngineFailureKind.AmbiguousMatch, CheatEngineHostEffect.Completed),
			(ambiguous.Kind, ambiguous.HostEffect));
		Assert.Equal((CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Completed),
			(beyondLimit.Kind, beyondLimit.HostEffect));
	}

	/// <summary>
	///     <c>Take</c> follows <see cref="AobScanRequest.MaximumResults" />: any positive limit is forwarded unchanged,
	///     and Core's copy cap of 65,535 addresses is reported through <see cref="AobScanResult.IsTruncated" />.
	/// </summary>
	[Theory]
	[InlineData(65_535)]
	[InlineData(65_536)]
	[InlineData(int.MaxValue)]
	public void TakeForwardsAPositiveLimitUnchangedWhateverTheCopyCap(int maximumResults)
	{
		Address match = 0x401000;
		FakePatternScanner scanner = new(new AobScanResult([match], true));

		AobScanResult result = scanner.Aob("90").Take(maximumResults).Execute(TestContext.Current.CancellationToken);

		Assert.Equal(maximumResults, scanner.LastRequest!.Value.MaximumResults);
		Assert.Equal([match], result.Matches);
		Assert.True(result.IsTruncated);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void TakeRejectsALimitThatIsNotPositiveBeforeAnyScan(int maximumResults)
	{
		FakePatternScanner scanner = new();

		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(() => scanner.Aob("90").Take(maximumResults));

		Assert.Equal("maximumResults", exception.ParamName);
		Assert.Null(scanner.LastRequest);
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

	/// <summary>
	///     A scanner that answers every request with one detailed outcome whose metrics are consistent with its copied
	///     result. Fluent terminals read <see cref="IPatternScanner.ScanDetailed" /> only. Its default accounting follows
	///     the global route (a truncated copy examined one row beyond it); a test of another Core shape passes its
	///     metrics, and <c>FluentAobTerminalCompositionTests</c> in Core.Tests runs the terminals against Core's real
	///     scanner on every route.
	/// </summary>
	private sealed class FakePatternScanner : IPatternScanner
	{
		private readonly PatternScanOutcome _outcome;

		internal FakePatternScanner()
			: this(new AobScanResult(ImmutableArray<Address>.Empty, false))
		{
		}

		/// <summary>Creates a scanner whose scan succeeds, with the global route's accounting.</summary>
		/// <param name="result">The copied result.</param>
		/// <param name="scope">The route that ran.</param>
		/// <param name="filteredOut">The rows read that lay outside the request.</param>
		/// <param name="unreadRows">The rows left unread; any makes the in-request count inexact.</param>
		internal FakePatternScanner(AobScanResult result, PatternScanScope scope = PatternScanScope.HostBoundedRange,
			ulong filteredOut = 0, ulong unreadRows = 0)
			: this(result, GlobalAccounting(result, scope, filteredOut, unreadRows))
		{
		}

		/// <summary>Creates a scanner whose scan succeeds with explicit metrics.</summary>
		/// <param name="result">The copied result.</param>
		/// <param name="metrics">The metrics Core publishes with that result.</param>
		internal FakePatternScanner(AobScanResult result, PatternScanMetrics metrics)
		{
			PatternScanRouteReason reason = metrics.Scope switch
			{
				PatternScanScope.GlobalHostScan => PatternScanRouteReason.UnscopedRequest,
				PatternScanScope.HostBoundedRange => PatternScanRouteReason.ScopedRequestOnQualifiedTarget,
				_ => PatternScanRouteReason.TargetIdentityNotQualified
			};
			PatternScanHostOutcomeKind hostOutcome = metrics.HostResultCount == 0
				? PatternScanHostOutcomeKind.NoMatches
				: PatternScanHostOutcomeKind.Matches;
			_outcome = new PatternScanOutcome(result, null, metrics, hostOutcome, reason, false);
		}

		/// <summary>Creates a scanner whose scan fails.</summary>
		/// <param name="failure">The classified failure.</param>
		internal FakePatternScanner(CheatEngineFailure failure)
		{
			_outcome = new PatternScanOutcome(null, failure, null, PatternScanHostOutcomeKind.Unknown,
				PatternScanRouteReason.Unknown, false);
		}

		internal AobScanRequest? LastRequest
		{
			get;
			private set;
		}

		internal CancellationToken LastCancellationToken
		{
			get;
			private set;
		}

		public bool TryScan(AobScanRequest request, out AobScanResult result, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException("Fluent terminals read ScanDetailed only.");
		}

		public AobScanResult Scan(AobScanRequest request, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException("Fluent terminals read ScanDetailed only.");
		}

		public PatternScanOutcome ScanDetailed(AobScanRequest request, CancellationToken cancellationToken = default)
		{
			LastRequest = request;
			LastCancellationToken = cancellationToken;
			return _outcome;
		}

		/// <summary>
		///     Builds the global route's accounting: every copied and filtered row was examined, a truncated copy also
		///     examined the in-request row that proved the cut, and the unread rows follow.
		/// </summary>
		private static PatternScanMetrics GlobalAccounting(AobScanResult result, PatternScanScope scope,
			ulong filteredOut, ulong unreadRows)
		{
			ulong examined = (ulong) result.Matches.Length + filteredOut + (result.IsTruncated ? 1UL : 0UL);
			return new PatternScanMetrics(scope, examined + unreadRows, examined, filteredOut, result.Matches.Length,
				0, 0, unreadRows, unreadRows == 0, TimeSpan.FromMilliseconds(3), TimeSpan.FromMilliseconds(1));
		}
	}
}
