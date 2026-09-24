using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Objects;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class HostEffectMappingTests
{
	[Theory]
	[InlineData(EngineEffectState.Unknown, CheatEngineHostEffect.Unknown)]
	[InlineData(EngineEffectState.NotStarted, CheatEngineHostEffect.NotStarted)]
	[InlineData(EngineEffectState.NotApplied, CheatEngineHostEffect.NotApplied)]
	[InlineData(EngineEffectState.Applied, CheatEngineHostEffect.Completed)]
	public void EachSdkEffectStateMapsToItsClientHostEffect(EngineEffectState state, CheatEngineHostEffect expected)
	{
		Assert.Equal(expected, HostEffectMapping.FromSdk(state));
	}

	[Fact]
	public void EverySdkEffectStateOfTheConsumedPackageIsMapped()
	{
		EngineEffectState[] mapped =
		[
			EngineEffectState.Unknown, EngineEffectState.NotStarted, EngineEffectState.NotApplied,
			EngineEffectState.Applied
		];

		// A value added by a later CheatEngine.SDK fails here until the mapping and the theory above cover it.
		Assert.Equal(mapped.Order(), Enum.GetValues<EngineEffectState>().Order());
	}

	[Fact]
	public void AnUnrecognizedSdkEffectStateNeverReadsAsAnEstablishedEffect()
	{
		EngineEffectState unrecognized = (EngineEffectState) (Enum.GetValues<EngineEffectState>().Max(static state =>
			(int) state) + 1);

		Assert.Equal(CheatEngineHostEffect.Unknown, HostEffectMapping.FromSdk(unrecognized));
	}
}
