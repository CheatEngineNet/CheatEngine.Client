using System.Reflection;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Hosting.Tests;

public sealed class CheatEngineClientPluginTests
{
	[Fact]
	public void ProtectedBaseConstructorAllowsAPublicParameterlessConcretePlugin()
	{
		ConstructorInfo? baseConstructor = typeof(CheatEngineClientPlugin).GetConstructor(
			BindingFlags.Instance | BindingFlags.NonPublic,
			binder: null,
			types: Type.EmptyTypes,
			modifiers: null);
		ConstructorInfo? concreteConstructor = typeof(TestPlugin).GetConstructor(Type.EmptyTypes);

		Assert.NotNull(baseConstructor);
		Assert.True(baseConstructor.IsFamily);
		Assert.NotNull(concreteConstructor);
		Assert.True(concreteConstructor.IsPublic);
		Assert.NotNull(new TestPlugin());
	}

	[Fact]
	public void GetRequiredClientWithoutAnActiveEnableEpochThrowsLifecycleException()
	{
		TestPlugin plugin = new();

		CheatEngineClientLifecycleException exception =
			Assert.Throws<CheatEngineClientLifecycleException>(plugin.GetRequiredClientForTest);

		Assert.Equal(CheatEngineFailureKind.InvalidState, exception.Failure.Kind);
		Assert.Equal("GetClient", exception.Failure.Operation);
		Assert.Contains("only while the plugin is enabled", exception.Message, StringComparison.Ordinal);
	}

	private sealed class TestPlugin : CheatEngineClientPlugin
	{
		protected override void Configure(CheatEnginePluginBuilder builder)
		{
		}

		internal ICheatEngineClient GetRequiredClientForTest() => GetRequiredClient();
	}
}
