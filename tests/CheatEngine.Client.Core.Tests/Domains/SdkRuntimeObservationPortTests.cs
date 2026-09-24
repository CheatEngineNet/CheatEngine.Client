using CheatEngine.Client.Core.Domains;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     The production runtime observation port reaches CheatEngine.SDK only through a Lua admission: without an enabled
///     plugin every member is refused before any Cheat Engine global is read (Q45).
/// </summary>
public sealed class SdkRuntimeObservationPortTests
{
	public static TheoryData<string> Members =>
	[
		nameof(IRuntimeObservationPort.TryObserveRuntimeInfo),
		nameof(IRuntimeObservationPort.ObserveHost),
		nameof(IRuntimeObservationPort.TryGetCheatEngineFileVersion),
		nameof(IRuntimeObservationPort.TryGetSystemArchitecture),
		nameof(IRuntimeObservationPort.TryIsCheatEngine64Bit),
		nameof(IRuntimeObservationPort.TryGetOperatingSystem),
		nameof(ITargetObservationPort.ObserveCurrent),
		nameof(ITargetObservationPort.ObserveTargetArchitecture),
		nameof(ITargetObservationPort.TryGetConfiguredPointerSize)
	];

	[Theory]
	[Trait("Qualification", "Q45")]
	[MemberData(nameof(Members))]
	public void EveryObservationRequiresAnEnabledPluginContext(string member)
	{
		SdkRuntimeObservationPort port = SdkRuntimeObservationPort.Instance;
		Action observe = member switch
		{
			nameof(IRuntimeObservationPort.TryObserveRuntimeInfo) => () => port.TryObserveRuntimeInfo(out _),
			nameof(IRuntimeObservationPort.ObserveHost) => () => port.ObserveHost(out _),
			nameof(IRuntimeObservationPort.TryGetCheatEngineFileVersion) => () =>
				port.TryGetCheatEngineFileVersion(out _),
			nameof(IRuntimeObservationPort.TryGetSystemArchitecture) => () => port.TryGetSystemArchitecture(out _),
			nameof(IRuntimeObservationPort.TryIsCheatEngine64Bit) => () => port.TryIsCheatEngine64Bit(out _),
			nameof(IRuntimeObservationPort.TryGetOperatingSystem) => () => port.TryGetOperatingSystem(out _),
			nameof(ITargetObservationPort.ObserveCurrent) => () => port.ObserveCurrent(out _),
			nameof(ITargetObservationPort.ObserveTargetArchitecture) => () => port.ObserveTargetArchitecture(out _),
			nameof(ITargetObservationPort.TryGetConfiguredPointerSize) => () =>
				port.TryGetConfiguredPointerSize(out _, out _),
			_ => throw new ArgumentOutOfRangeException(nameof(member), member, null)
		};

		Assert.Throws<InvalidOperationException>(observe);
	}

	[Fact]
	public void TheMemoryCodecContextPortObservesTargetsThroughTheSameReadOnlyOperations()
	{
		SdkMemoryCodecContextPort port = SdkMemoryCodecContextPort.Instance;

		Assert.Throws<InvalidOperationException>(() => port.ObserveCurrent(out _));
		Assert.Throws<InvalidOperationException>(() => port.ObserveTargetArchitecture(out _));
		Assert.Throws<InvalidOperationException>(() => port.TryGetConfiguredPointerSize(out _, out _));
	}
}
