using CheatEngine.Client.Runtime;

namespace CheatEngine.Client.Abstractions.Tests.Runtime;

public sealed class ClientCapabilityEvidenceTests
{
	public static IEnumerable<object[]> EffectiveReasonPriorityCases
	{
		get
		{
			foreach (ClientCapabilityEvidenceState state in new[]
			         {
				         ClientCapabilityEvidenceState.Missing, ClientCapabilityEvidenceState.Faulted,
				         ClientCapabilityEvidenceState.Malformed, ClientCapabilityEvidenceState.Unknown
			         })
			{
				ClientCapabilityAvailabilityState expectedAvailabilityState =
					state == ClientCapabilityEvidenceState.Missing
						? ClientCapabilityAvailabilityState.Unavailable
						: ClientCapabilityAvailabilityState.Unknown;

				foreach (ClientCapabilityEvidenceReasonCode expectedReasonCode in GetPriority(state))
				{
					yield return [state, expectedReasonCode, expectedAvailabilityState];
				}
			}
		}
	}

	[Theory]
	[MemberData(nameof(EffectiveReasonPriorityCases))]
	public void EffectiveReasonCodePreservesEveryGatePriority(
		ClientCapabilityEvidenceState state,
		ClientCapabilityEvidenceReasonCode expectedReasonCode,
		ClientCapabilityAvailabilityState expectedAvailabilityState)
	{
		ClientCapabilityEvidence evidence = CreateEvidenceForPriority(state, expectedReasonCode);
		ClientCapabilityAvailability availability = new(ClientCapabilityId.ProcessSelection, evidence);

		Assert.Equal(expectedReasonCode, evidence.EffectiveReasonCode);
		Assert.Equal(ReasonFor(expectedReasonCode), evidence.EffectiveReason);
		Assert.Equal(expectedAvailabilityState, evidence.AvailabilityState);
		Assert.Equal(expectedAvailabilityState, availability.State);
		Assert.Equal(evidence.EffectiveReason, availability.Reason);
	}

	[Fact]
	public void MissingReasonWinsOverFaultedMalformedAndUnknownReasons()
	{
		ClientCapabilityEvidence evidence = new(
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown, ReasonFor(
				ClientCapabilityEvidenceReasonCode.Implementation)),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Malformed, ReasonFor(
				ClientCapabilityEvidenceReasonCode.Package)),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Faulted, ReasonFor(
				ClientCapabilityEvidenceReasonCode.Host)),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Missing, ReasonFor(
				ClientCapabilityEvidenceReasonCode.LiveQualification)),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown, ReasonFor(
				ClientCapabilityEvidenceReasonCode.Policy)),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied, ReasonFor(
				ClientCapabilityEvidenceReasonCode.Lifetime)));

		Assert.Equal(ClientCapabilityEvidenceReasonCode.LiveQualification, evidence.EffectiveReasonCode);
		Assert.Equal(ReasonFor(ClientCapabilityEvidenceReasonCode.LiveQualification), evidence.EffectiveReason);
		Assert.Equal(ClientCapabilityAvailabilityState.Unavailable, evidence.AvailabilityState);
	}

	[Theory]
	[InlineData(ClientCapabilityEvidenceState.Faulted)]
	[InlineData(ClientCapabilityEvidenceState.Malformed)]
	public void FaultedAndMalformedReasonsWinOverUnknownReasons(ClientCapabilityEvidenceState failedState)
	{
		ClientCapabilityEvidence evidence = new(
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied, ReasonFor(
				ClientCapabilityEvidenceReasonCode.Implementation)),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied, ReasonFor(
				ClientCapabilityEvidenceReasonCode.Package)),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown, ReasonFor(
				ClientCapabilityEvidenceReasonCode.Host)),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied, ReasonFor(
				ClientCapabilityEvidenceReasonCode.LiveQualification)),
			new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied, ReasonFor(
				ClientCapabilityEvidenceReasonCode.Policy)),
			new ClientCapabilityEvidenceGate(failedState, ReasonFor(ClientCapabilityEvidenceReasonCode.Lifetime)));

		Assert.Equal(ClientCapabilityEvidenceReasonCode.Lifetime, evidence.EffectiveReasonCode);
		Assert.Equal(ReasonFor(ClientCapabilityEvidenceReasonCode.Lifetime), evidence.EffectiveReason);
		Assert.Equal(ClientCapabilityAvailabilityState.Unknown, evidence.AvailabilityState);
	}

	[Fact]
	public void AllSatisfiedEvidenceUsesTheLifetimeReasonCode()
	{
		ClientCapabilityEvidence evidence = CreateEvidenceForPriority(
			ClientCapabilityEvidenceState.Satisfied, ClientCapabilityEvidenceReasonCode.Lifetime);

		Assert.Equal(ClientCapabilityEvidenceReasonCode.Lifetime, evidence.EffectiveReasonCode);
		Assert.Equal(ReasonFor(ClientCapabilityEvidenceReasonCode.Lifetime), evidence.EffectiveReason);
		Assert.True(evidence.IsExecutable);
		Assert.Equal(ClientCapabilityAvailabilityState.Available, evidence.AvailabilityState);
	}

	[Theory]
	[InlineData(ClientCapabilityAvailabilityState.Available, ClientCapabilityEvidenceReasonCode.Lifetime)]
	[InlineData(ClientCapabilityAvailabilityState.Unavailable, ClientCapabilityEvidenceReasonCode.Lifetime)]
	[InlineData(ClientCapabilityAvailabilityState.Unknown, ClientCapabilityEvidenceReasonCode.Host)]
	public void LegacyAvailabilityConstructionRetainsStateAndDisplayReason(
		ClientCapabilityAvailabilityState state,
		ClientCapabilityEvidenceReasonCode expectedReasonCode)
	{
		const string reason = "Legacy display reason.";
		ClientCapabilityAvailability availability = new(ClientCapabilityId.ProcessSelection, state, reason);

		Assert.Equal(state, availability.State);
		Assert.Equal(expectedReasonCode, availability.Evidence.EffectiveReasonCode);
		Assert.Equal(reason, availability.Evidence.EffectiveReason);
		Assert.Equal(reason, availability.Reason);
		Assert.Equal(state == ClientCapabilityAvailabilityState.Available, availability.IsAvailable);
		Assert.Equal(state != ClientCapabilityAvailabilityState.Unknown, availability.IsKnown);
	}

	[Fact]
	public void EffectiveReasonCodesUseStableUnderlyingValues()
	{
		Assert.Equal((byte) 0, (byte) ClientCapabilityEvidenceReasonCode.Implementation);
		Assert.Equal((byte) 1, (byte) ClientCapabilityEvidenceReasonCode.Package);
		Assert.Equal((byte) 2, (byte) ClientCapabilityEvidenceReasonCode.Host);
		Assert.Equal((byte) 3, (byte) ClientCapabilityEvidenceReasonCode.LiveQualification);
		Assert.Equal((byte) 4, (byte) ClientCapabilityEvidenceReasonCode.Policy);
		Assert.Equal((byte) 5, (byte) ClientCapabilityEvidenceReasonCode.Lifetime);
	}

	private static ClientCapabilityEvidence CreateEvidenceForPriority(
		ClientCapabilityEvidenceState state,
		ClientCapabilityEvidenceReasonCode expectedReasonCode)
	{
		ClientCapabilityEvidenceReasonCode[] priority = GetPriority(state);
		int expectedPriorityIndex = Array.IndexOf(priority, expectedReasonCode);

		return new ClientCapabilityEvidence(
			CreateGate(ClientCapabilityEvidenceReasonCode.Implementation, state, priority, expectedPriorityIndex),
			CreateGate(ClientCapabilityEvidenceReasonCode.Package, state, priority, expectedPriorityIndex),
			CreateGate(ClientCapabilityEvidenceReasonCode.Host, state, priority, expectedPriorityIndex),
			CreateGate(ClientCapabilityEvidenceReasonCode.LiveQualification, state, priority, expectedPriorityIndex),
			CreateGate(ClientCapabilityEvidenceReasonCode.Policy, state, priority, expectedPriorityIndex),
			CreateGate(ClientCapabilityEvidenceReasonCode.Lifetime, state, priority, expectedPriorityIndex));
	}

	private static ClientCapabilityEvidenceGate CreateGate(
		ClientCapabilityEvidenceReasonCode code,
		ClientCapabilityEvidenceState state,
		ClientCapabilityEvidenceReasonCode[] priority,
		int expectedPriorityIndex)
	{
		ClientCapabilityEvidenceState gateState = state == ClientCapabilityEvidenceState.Satisfied ||
		                                          Array.IndexOf(priority, code) < expectedPriorityIndex
			? ClientCapabilityEvidenceState.Satisfied
			: state;
		return new ClientCapabilityEvidenceGate(gateState, ReasonFor(code));
	}

	private static ClientCapabilityEvidenceReasonCode[] GetPriority(ClientCapabilityEvidenceState state)
	{
		return state switch
		{
			ClientCapabilityEvidenceState.Missing =>
			[
				ClientCapabilityEvidenceReasonCode.Lifetime,
				ClientCapabilityEvidenceReasonCode.Policy,
				ClientCapabilityEvidenceReasonCode.Implementation,
				ClientCapabilityEvidenceReasonCode.Package,
				ClientCapabilityEvidenceReasonCode.Host,
				ClientCapabilityEvidenceReasonCode.LiveQualification
			],
			ClientCapabilityEvidenceState.Faulted or ClientCapabilityEvidenceState.Malformed or
				ClientCapabilityEvidenceState.Unknown =>
				[
					ClientCapabilityEvidenceReasonCode.Host,
					ClientCapabilityEvidenceReasonCode.Package,
					ClientCapabilityEvidenceReasonCode.LiveQualification,
					ClientCapabilityEvidenceReasonCode.Implementation,
					ClientCapabilityEvidenceReasonCode.Policy,
					ClientCapabilityEvidenceReasonCode.Lifetime
				],
			ClientCapabilityEvidenceState.Satisfied =>
			[
				ClientCapabilityEvidenceReasonCode.Lifetime,
				ClientCapabilityEvidenceReasonCode.Policy,
				ClientCapabilityEvidenceReasonCode.Implementation,
				ClientCapabilityEvidenceReasonCode.Package,
				ClientCapabilityEvidenceReasonCode.Host,
				ClientCapabilityEvidenceReasonCode.LiveQualification
			],
			_ => throw new ArgumentOutOfRangeException(nameof(state), state,
				"The Client capability evidence state is not defined.")
		};
	}

	private static string ReasonFor(ClientCapabilityEvidenceReasonCode code)
	{
		return $"{code} reason.";
	}
}
