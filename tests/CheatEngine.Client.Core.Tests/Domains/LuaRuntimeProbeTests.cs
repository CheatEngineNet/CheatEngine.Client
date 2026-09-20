using CheatEngine.Client.Core.Domains;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class LuaRuntimeProbeTests
{
	[Fact]
	public void GetCheatEngineVersionRequiresAnEnabledPluginContext()
	{
		LuaRuntimeProbe probe = new();

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => probe.GetCheatEngineVersion());

		Assert.Contains("plugin is not enabled", exception.Message, StringComparison.OrdinalIgnoreCase);
	}
}
