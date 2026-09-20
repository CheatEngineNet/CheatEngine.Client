using System.Collections.Immutable;

using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Tests.Scanning;

public sealed class AobFluentBuilderTests
{
	[Fact]
	public void ConfigurationMethodsReturnNewBuilderWithoutChangingTheOriginal()
	{
		FakePatternScanner scanner = new();
		AobScanBuilder original = scanner.Aob("48 8B ?? 89");

		AobScanBuilder configured = original.InModule("game.exe").InRange(0x400000, 0x4FFFFF).ReadableExecutable();

		Assert.Null(original.Module);
		Assert.Null(original.Range);
		Assert.Null(original.Options.ProtectionFlags);
		Assert.Equal("game.exe", configured.Module!.Value.Value);
		Assert.Equal(new AobScanRange(0x400000, 0x4FFFFF), configured.Range);
		Assert.Equal("+X-C-W", configured.Options.ProtectionFlags);
		Assert.Equal("48 8B ?? 89", configured.Pattern.Value);
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

		Address actual = scanner.Aob("90")
			.WithProtectionFlags("-w+x-c")
			.WithAlignment(FastScanMethod.LastDigits, "f0")
			.InRange(0x400000, 0x4FFFFF)
			.RequireSingle()
			.Execute(TestContext.Current.CancellationToken);

		Assert.Equal(expected, actual);
		AobScanRequest request = Assert.IsType<AobScanRequest>(scanner.LastRequest);
		Assert.Equal("+X-C-W", request.Options.ProtectionFlags);
		Assert.Equal(FastScanMethod.LastDigits, request.Options.AlignmentMethod);
		Assert.Equal("F0", request.Options.AlignmentParameter);
		Assert.Equal(new AobScanRange(0x400000, 0x4FFFFF), request.Range);
	}

	[Theory]
	[InlineData("+X+X")]
	[InlineData("X")]
	public void WithProtectionFlagsRejectsMalformedExpressionsBeforeTerminalSelection(string protection)
	{
		FakePatternScanner scanner = new();

		Assert.Throws<ArgumentException>(() => scanner.Aob("90").WithProtectionFlags(protection));
		Assert.Null(scanner.LastRequest);
	}

	[Fact]
	public void WithAlignmentRejectsAnInvalidDivisorBeforeTerminalSelection()
	{
		FakePatternScanner scanner = new();

		Assert.Throws<ArgumentException>(() => scanner.Aob("90").WithAlignment(FastScanMethod.Aligned, "0"));
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

			failure.Throw();
			return default;
		}
	}
}
