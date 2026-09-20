using CheatEngine.Client.Extensions.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Hosting.Tests;

public sealed class CheatEnginePluginBuilderTests
{
	[Fact]
	public void BuildServiceProviderValidatesServicesAndBindsTheActivationConfiguration()
	{
		CheatEnginePluginBuilder builder = new();
		builder.Configuration["CheatEngineClient:DefaultMaximumAobResults"] = "19";

		using ServiceProvider provider = builder.BuildServiceProvider();
		CheatEngineClientOptions options = provider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;

		Assert.Equal(19, options.DefaultMaximumAobResults);
		Assert.Same(builder.Configuration, provider.GetRequiredService<IConfiguration>());
		Assert.Same(builder.Configuration, provider.GetRequiredService<IConfigurationRoot>());
	}

	[Fact]
	public void BuildServiceProviderIsSingleUse()
	{
		CheatEnginePluginBuilder builder = new();
		using ServiceProvider provider = builder.BuildServiceProvider();

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.BuildServiceProvider);

		Assert.Contains("only one provider", exception.Message, StringComparison.Ordinal);
	}
}
