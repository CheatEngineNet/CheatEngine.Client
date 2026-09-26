using System.Text.Json;

namespace LivePlugin.Qualification.Harness;

/// <summary>Where the harness throws on purpose for the rollback (Q06) and cleanup (Q43) scenarios.</summary>
internal enum FaultStage
{
	/// <summary>No fault.</summary>
	None = 0,

	/// <summary><c>Configure</c> throws before any activation exists.</summary>
	Configure,

	/// <summary>The fault module throws in <c>OnEnabled</c>, after the first recording module entered.</summary>
	ModuleOnEnabled,

	/// <summary>The fault module throws in <c>OnDisabling</c>.</summary>
	ModuleOnDisabling,

	/// <summary>The activation-scoped resource throws in <c>Dispose</c>.</summary>
	ResourceCleanup,

	/// <summary>Both <see cref="ModuleOnDisabling" /> and <see cref="ResourceCleanup" />.</summary>
	ModuleOnDisablingAndResourceCleanup
}

/// <summary>Why the fault switch selected what it selected.</summary>
internal enum FaultSwitchReason
{
	/// <summary>No switch file exists next to the plugin.</summary>
	Absent = 0,

	/// <summary>The switch was read and selects a stage (possibly <see cref="FaultStage.None" />).</summary>
	Selected,

	/// <summary>A switch exists but the gate did not authorize the run, so it is ignored.</summary>
	IgnoredUnauthorized,

	/// <summary>The switch has another schema, is malformed or names an unknown stage, so it is ignored.</summary>
	IgnoredInvalid
}

/// <summary>The decision of one enable: the stage to fault and why. A reference type, so it can be a DI singleton.</summary>
internal sealed record FaultDecision(FaultStage Stage, FaultSwitchReason Reason)
{
	/// <summary>Gets the decision when no switch exists.</summary>
	internal static FaultDecision NoFault
	{
		get;
	} = new(FaultStage.None, FaultSwitchReason.Absent);

	/// <summary>Whether a module <c>OnEnabled</c> fault is selected.</summary>
	internal bool FaultsEnable => Stage == FaultStage.ModuleOnEnabled;

	/// <summary>Whether a module <c>OnDisabling</c> fault is selected.</summary>
	internal bool FaultsDisabling => Stage is FaultStage.ModuleOnDisabling or FaultStage.ModuleOnDisablingAndResourceCleanup;

	/// <summary>Whether a resource cleanup fault is selected.</summary>
	internal bool FaultsResourceCleanup => Stage is FaultStage.ResourceCleanup or FaultStage.ModuleOnDisablingAndResourceCleanup;
}

/// <summary>
///     Reads the fault switch the CheatEngine.SDK qualification runner writes next to the first plugin of a scenario that
///     declares a <c>faultStage</c> (<c>liveprobe.fault.json</c>, schema <c>ce77-live-probe-fault-v1</c>, removed by the
///     runner when an operator step says <c>removeFaultFile</c>). The switch is read once per enable and honored only
///     when the qualification gate authorized the run, so a stray file on a user's machine never breaks a plugin.
/// </summary>
internal static class QualificationFaultSwitch
{
	internal const string FileName = "liveprobe.fault.json";
	internal const string Schema = "ce77-live-probe-fault-v1";

	/// <summary>Reads the switch of <paramref name="pluginDirectory" /> under the given gate decision.</summary>
	internal static FaultDecision Read(string? pluginDirectory, AuthorizationDecision authorization,
		IQualificationEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(authorization);
		ArgumentNullException.ThrowIfNull(environment);
		if (string.IsNullOrEmpty(pluginDirectory) ||
			!environment.TryReadFile(Path.Combine(pluginDirectory, FileName), out string text))
		{
			return FaultDecision.NoFault;
		}

		if (!authorization.IsAllowed)
		{
			return new FaultDecision(FaultStage.None, FaultSwitchReason.IgnoredUnauthorized);
		}

		return TryParse(text, out FaultStage stage)
			? new FaultDecision(stage, FaultSwitchReason.Selected)
			: new FaultDecision(FaultStage.None, FaultSwitchReason.IgnoredInvalid);
	}

	private static bool TryParse(string text, out FaultStage stage)
	{
		stage = FaultStage.None;
		try
		{
			using JsonDocument document = JsonDocument.Parse(text);
			JsonElement root = document.RootElement;
			return root.ValueKind == JsonValueKind.Object &&
				   root.TryGetProperty("schema", out JsonElement schema) &&
				   schema.ValueKind == JsonValueKind.String &&
				   string.Equals(schema.GetString(), Schema, StringComparison.Ordinal) &&
				   root.TryGetProperty("throwIn", out JsonElement throwIn) &&
				   throwIn.ValueKind == JsonValueKind.String &&
				   Enum.TryParse(throwIn.GetString(), ignoreCase: false, out stage) &&
				   Enum.IsDefined(stage);
		}
		catch (JsonException)
		{
			stage = FaultStage.None;
			return false;
		}
	}
}
