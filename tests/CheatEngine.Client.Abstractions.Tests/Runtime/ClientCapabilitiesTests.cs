using CheatEngine.Client.Runtime;

namespace CheatEngine.Client.Abstractions.Tests.Runtime;

public sealed class ClientCapabilitiesTests
{
	[Fact]
	public void CollectionPreservesDistinctObservedCapabilitiesAndTheirReasons()
	{
		ClientCapabilityAvailability unsafeLua = new(
			ClientCapabilityId.UnsafeLuaExecution,
			ClientCapabilityAvailabilityState.Available,
			"Explicit opt-in.");
		ClientCapabilityAvailability valueScanning = new(
			ClientCapabilityId.ValueScanning,
			ClientCapabilityAvailabilityState.Unavailable,
			"Live ownership gate pending.");

		ClientCapabilities capabilities = ClientCapabilities.Create([unsafeLua, valueScanning]);

		Assert.Equal(2, capabilities.Count);
		Assert.True(capabilities.TryGet(ClientCapabilityId.UnsafeLuaExecution,
			out ClientCapabilityAvailability foundUnsafeLua));
		Assert.Equal(unsafeLua, foundUnsafeLua);
		Assert.True(foundUnsafeLua.IsAvailable);
		Assert.True(capabilities.TryGet(ClientCapabilityId.ValueScanning,
			out ClientCapabilityAvailability foundValueScanning));
		Assert.Equal(valueScanning, foundValueScanning);
		Assert.False(foundValueScanning.IsAvailable);
		Assert.True(foundValueScanning.IsKnown);
	}

	[Fact]
	public void EvidenceSeparatesIndependentPrerequisitesWithoutPromotingAnOptIn()
	{
		ClientCapabilityEvidence evidence = new(
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied,
				"The adapter is implemented."),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown,
				"The consumed package identity has not been established."),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown,
				"The host primitive has not been probed."),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown,
				"No live qualification record is attached."),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied,
				"The explicit policy opt-in is present."),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied,
				"The activation is current."));

		ClientCapabilityAvailability availability = new(ClientCapabilityId.UnsafeLuaExecution, evidence);

		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, availability.Evidence.Policy.State);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, availability.Evidence.Host.State);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, availability.Evidence.LiveQualification.State);
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, availability.State);
		Assert.False(availability.IsAvailable);
		Assert.Contains("host primitive", availability.Reason, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(ClientCapabilityEvidenceState.Missing)]
	[InlineData(ClientCapabilityEvidenceState.Faulted)]
	[InlineData(ClientCapabilityEvidenceState.Malformed)]
	public void EvidenceKeepsMissingFaultedAndMalformedHostPrerequisitesDistinct(
		ClientCapabilityEvidenceState hostState)
	{
		ClientCapabilityEvidenceGate established = new(ClientCapabilityEvidenceState.Satisfied, "Established.");
		ClientCapabilityEvidence evidence = new(
			established,
			established,
			new ClientCapabilityEvidenceGate(hostState, "Host-specific result."),
			established,
			established,
			established);

		ClientCapabilityAvailability availability = new(ClientCapabilityId.ProcessSelection, evidence);

		Assert.Equal(hostState, availability.Evidence.Host.State);
		Assert.Equal(hostState == ClientCapabilityEvidenceState.Missing
			? ClientCapabilityAvailabilityState.Unavailable
			: ClientCapabilityAvailabilityState.Unknown, availability.State);
		Assert.Equal("Host-specific result.", availability.Reason);
	}

	[Fact]
	public void CollectionRejectsDuplicateCapabilityIdentifiers()
	{
		ClientCapabilityAvailability observation = new(
			ClientCapabilityId.ValueScanning,
			ClientCapabilityAvailabilityState.Unavailable,
			"Live ownership gate pending.");

		Assert.Throws<ArgumentException>(() => ClientCapabilities.Create([observation, observation]));
	}

	[Theory]
	[InlineData(3)]
	[InlineData(255)]
	public void AvailabilityRejectsUndefinedStates(byte state)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ClientCapabilityAvailability(
			ClientCapabilityId.ValueScanning,
			(ClientCapabilityAvailabilityState) state,
			"Invalid test state."));
	}

	[Fact]
	public void CapabilityIdentifierRejectsBlankInputAndFormatsItsStableValue()
	{
		Assert.Throws<ArgumentException>(() => new ClientCapabilityId(" "));

		ClientCapabilityId capability = new("Client.Sample");

		Assert.Equal("Client.Sample", capability.Value);
		Assert.Equal(capability.Value, capability.ToString());
	}

	[Fact]
	public void AdvancedDomainCapabilityIdentifiersAreStableAndDistinct()
	{
		ClientCapabilityId[] capabilities =
		[
			ClientCapabilityId.Allocations,
			ClientCapabilityId.Assembly,
			ClientCapabilityId.RemoteExecution,
			ClientCapabilityId.Debugger,
			ClientCapabilityId.Hotkeys,
			ClientCapabilityId.Timers,
			ClientCapabilityId.Speed,
			ClientCapabilityId.Hashing,
			ClientCapabilityId.Dbvm
		];

		Assert.Equal(9, capabilities.Length);
		Assert.Equal(9, capabilities.Select(static capability => capability.Value).Distinct().Count());
		Assert.All(capabilities, static capability => Assert.StartsWith("Client.", capability.Value));
	}
}
