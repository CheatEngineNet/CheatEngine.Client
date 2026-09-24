using System.Runtime.Versioning;

using CheatEngine.Client.Tests.Infrastructure;

using LivePlugin.Qualification.Harness;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The runner's authorization files, evaluated by the harness's own gate and fault switch (compiled in from
///     <c>tests/CheatEngine.Client.LivePlugin.Qualification/Harness</c>): what the runner writes is exactly what the harness
///     accepts inside Cheat Engine, for the declared target only and never for longer than the runner's 25 minutes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AuthorizationManifestTests : IDisposable
{
	private const int HostProcessId = 5100;
	private const int TargetProcessId = 5200;
	private const string TargetSha256 = "2DABEFFD5DD45A3DA79697D6B8A3B7942A9EFF0EA78519A313ECB29AD5A865E3";

	private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 15, 30, TimeSpan.Zero);

	private readonly TemporaryDirectory _temporary = new("LiveQualificationAuthorization");

	public void Dispose()
	{
		_temporary.Dispose();
	}

	[Fact]
	public void TheHarnessGateAcceptsTheWrittenManifestForTheDeclaredTargetOnly()
	{
		string manifest = Write(AuthorizationManifestWriter.MaximumLifetime);

		AuthorizationDecision decision = QualificationAuthorization.Evaluate(new HostEnvironment(manifest, Now));

		Assert.True(decision.IsAllowed, decision.Denial.ToString());
		Assert.Equal(TargetProcessId, decision.TargetProcessId);
		Assert.Equal(TargetSha256, decision.TargetSha256);
		Assert.Equal(Now + AuthorizationManifestWriter.MaximumLifetime, decision.ExpiresUtc);
		Assert.True(decision.Allows(TargetProcessId));
		Assert.False(decision.Allows(HostProcessId));
	}

	[Fact]
	public void TheManifestExpiresBeforeTheHarnessLimit()
	{
		string manifest = Write(AuthorizationManifestWriter.MaximumLifetime);

		AuthorizationDecision expired = QualificationAuthorization.Evaluate(
			new HostEnvironment(manifest, Now + AuthorizationManifestWriter.MaximumLifetime));

		Assert.True(AuthorizationManifestWriter.MaximumLifetime < QualificationAuthorization.MaximumLifetime);
		Assert.Equal(AuthorizationDenial.ManifestExpired, expired.Denial);
		Assert.Throws<ArgumentOutOfRangeException>(() => Write(AuthorizationManifestWriter.MaximumLifetime + TimeSpan.FromSeconds(1)));
		Assert.Throws<ArgumentOutOfRangeException>(() => Write(TimeSpan.Zero));
	}

	[Fact]
	public void TheManifestNamesTheExactHostAndTheSessionInputsCarryIt()
	{
		string manifest = Write(TimeSpan.FromMinutes(5));
		IReadOnlyDictionary<string, string> inputs = AuthorizationManifestWriter.SessionInputs(manifest);

		Assert.Contains($"\"hostSha256\": \"{QualificationAuthorization.ExactCheatEngineSha256}\"", File.ReadAllText(manifest),
			StringComparison.Ordinal);
		Assert.Equal(LiveQualificationOptIn.Acknowledgement, QualificationAuthorization.Acknowledgement);
		Assert.Equal(QualificationAuthorization.Acknowledgement, inputs[QualificationAuthorization.AcknowledgementVariable]);
		Assert.Equal(manifest, inputs[QualificationAuthorization.ManifestVariable]);
		Assert.All(inputs.Keys, static name => Assert.StartsWith("CE_SDK_LIVE_PROBE_", name, StringComparison.Ordinal));
	}

	[Fact]
	public void AnotherHostIsRefusedByTheHarnessGate()
	{
		string manifest = Write(TimeSpan.FromMinutes(5));

		AuthorizationDecision decision = QualificationAuthorization.Evaluate(
			new HostEnvironment(manifest, Now, new ProcessImage(QualificationAuthorization.ExactCheatEngineSha256, "Amd64", "7.6.0.9999")));

		Assert.Equal(AuthorizationDenial.HostMismatch, decision.Denial);
	}

	[Theory]
	[InlineData(nameof(FaultStage.Configure))]
	[InlineData(nameof(FaultStage.ModuleOnDisabling))]
	[InlineData(nameof(FaultStage.ModuleOnDisablingAndResourceCleanup))]
	public void TheHarnessReadsTheWrittenFaultSwitch(string stageName)
	{
		FaultStage stage = Enum.Parse<FaultStage>(stageName);
		string plugin = _temporary.CreateDirectory("plugin-" + stage);
		HostEnvironment environment = new(Write(TimeSpan.FromMinutes(5)), Now);
		AuthorizationDecision allowed = QualificationAuthorization.Evaluate(environment);

		AuthorizationManifestWriter.WriteFaultSwitch(plugin, stage);
		FaultDecision selected = QualificationFaultSwitch.Read(plugin, allowed, environment);
		AuthorizationManifestWriter.RemoveFaultSwitch(plugin);
		FaultDecision removed = QualificationFaultSwitch.Read(plugin, allowed, environment);

		Assert.Equal(new FaultDecision(stage, FaultSwitchReason.Selected), selected);
		Assert.Equal(FaultDecision.NoFault, removed);
	}

	private string Write(TimeSpan lifetime)
	{
		return AuthorizationManifestWriter.Write(Path.Combine(_temporary.Path, "authorization.json"),
			QualificationAuthorization.ExactCheatEngineSha256, TargetProcessId, TargetSha256, Now, lifetime);
	}

	/// <summary>The view the harness has from inside Cheat Engine, with the runner's session inputs.</summary>
	private sealed class HostEnvironment(string manifestPath, DateTimeOffset now, ProcessImage? host = null) : IQualificationEnvironment
	{
		private readonly IReadOnlyDictionary<string, string> _variables = AuthorizationManifestWriter.SessionInputs(manifestPath);

		public DateTimeOffset UtcNow => now;

		public int CurrentProcessId => HostProcessId;

		public bool Is64BitProcess => true;

		public string? GetVariable(string name)
		{
			return _variables.GetValueOrDefault(name);
		}

		public bool TryReadFile(string path, out string text)
		{
			text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
			return File.Exists(path);
		}

		public bool TryDescribeProcessImage(int processId, out ProcessImage image)
		{
			image = processId switch
			{
				HostProcessId => host ?? new ProcessImage(QualificationAuthorization.ExactCheatEngineSha256, "Amd64",
					QualificationAuthorization.ExactCheatEngineFileVersion),
				TargetProcessId => new ProcessImage(TargetSha256, "Amd64", null),
				_ => default
			};
			return processId is HostProcessId or TargetProcessId;
		}
	}
}
