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

	[Fact]
	public void TargetIsX86RequiresAnEnabledPluginContext()
	{
		LuaRuntimeProbe probe = new();

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => probe.TargetIsX86());

		Assert.Contains("plugin is not enabled", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void TargetIsArmRequiresAnEnabledPluginContext()
	{
		LuaRuntimeProbe probe = new();

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => probe.TargetIsArm());

		Assert.Contains("plugin is not enabled", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void GetConfiguredPointerSizeRequiresAnEnabledPluginContext()
	{
		LuaRuntimeProbe probe = new();

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => probe.GetConfiguredPointerSize());

		Assert.Contains("plugin is not enabled", exception.Message, StringComparison.OrdinalIgnoreCase);
	}
}
