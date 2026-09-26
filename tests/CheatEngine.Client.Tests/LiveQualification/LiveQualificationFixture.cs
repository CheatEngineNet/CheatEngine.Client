using System.Runtime.Versioning;

using CheatEngine.Client.Tests.Packaging;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The serial collection of the live qualification tests: one Cheat Engine sandbox at a time, never in parallel with
///     another live fact.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
[SupportedOSPlatform("windows")]
public sealed class LiveQualificationSerialGroup : ICollectionFixture<LiveQualificationFixture>
{
	/// <summary>The collection name.</summary>
	public const string Name = "Live qualification";
}

/// <summary>
///     Evaluates the opt-in first and does nothing else when it refuses, so an unauthorized run fails in milliseconds
///     with instructions. Only an authorized run initializes the <see cref="PackagedClientFeedFixture" /> the plugins are
///     built from: the exact packages of <c>CHEATENGINE_CLIENT_PACKAGE_SOURCE</c>, restored in isolated consumers with
///     CheatEngine.SDK from nuget.org.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LiveQualificationFixture : IAsyncLifetime
{
	private PackagedClientFeedFixture? _feed;
	private LiveQualificationRun? _run;

	/// <summary>The opt-in decision of this run.</summary>
	internal LiveQualificationDecision Decision
	{
		get;
		private set;
	} = new(null, "The live qualification fixture was not initialized.");

	/// <summary>The packed Client feed; only an authorized run has one.</summary>
	internal PackagedClientFeedFixture Feed =>
		_feed ?? throw new InvalidOperationException("Only an authorized live qualification run builds the packaged feed.");

	/// <inheritdoc />
	public async ValueTask InitializeAsync()
	{
		Decision = LiveQualificationOptIn.ResolveFromEnvironment();
		if (!Decision.IsAuthorized)
		{
			return;
		}

		_feed = new PackagedClientFeedFixture();
		await _feed.InitializeAsync();
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (_feed is not null)
		{
			await _feed.DisposeAsync();
		}
	}

	/// <summary>
	///     The run the sessions S1 to S6 of this test run share: one run directory, one receipt ledger and one summary.
	///     The S0 spike keeps its own run, whose receipts are never committed.
	/// </summary>
	internal LiveQualificationRun Run(LiveQualificationInputs inputs)
	{
		ArgumentNullException.ThrowIfNull(inputs);
		return _run ??= LiveQualificationRun.Create(inputs.RunRoot);
	}

	/// <summary>Fails the calling test, with the opt-in instructions, unless this run is authorized and its feed is usable.</summary>
	internal LiveQualificationInputs RequireAuthorization()
	{
		Assert.True(Decision.IsAuthorized, Decision.Refusal);
		Feed.RequirePackages();
		return Decision.Inputs!;
	}
}
