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
		string allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CheatEngine.Client.Hosting.Tests"));
		builder.Configuration["CheatEngineClient:AllowedTableRoots:0"] = allowedRoot;

		using ServiceProvider provider = builder.BuildServiceProvider();
		CheatEngineClientOptions options = provider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;

		Assert.Equal([allowedRoot], Assert.IsType<string[]>(options.AllowedTableRoots));
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

	[Fact]
	public void TwoScopesShareProviderSingletonsButDisposeTheirOwnScopedServices()
	{
		CheatEnginePluginBuilder builder = new();
		builder.Services.AddSingleton<ProviderOwnedDisposable>();
		builder.Services.AddScoped<ScopeOwnedDisposable>();
		ProviderOwnedDisposable providerOwned;
		ScopeOwnedDisposable firstScoped;
		ScopeOwnedDisposable secondScoped;

		using (ServiceProvider provider = builder.BuildServiceProvider())
		{
			using (IServiceScope firstScope = provider.CreateScope())
			{
				providerOwned = firstScope.ServiceProvider.GetRequiredService<ProviderOwnedDisposable>();
				firstScoped = firstScope.ServiceProvider.GetRequiredService<ScopeOwnedDisposable>();
			}

			Assert.Equal(1, firstScoped.DisposeCount);
			Assert.Equal(0, providerOwned.DisposeCount);

			using (IServiceScope secondScope = provider.CreateScope())
			{
				Assert.Same(providerOwned, secondScope.ServiceProvider.GetRequiredService<ProviderOwnedDisposable>());
				secondScoped = secondScope.ServiceProvider.GetRequiredService<ScopeOwnedDisposable>();
			}

			Assert.NotSame(firstScoped, secondScoped);
			Assert.Equal(1, secondScoped.DisposeCount);
			Assert.Equal(0, providerOwned.DisposeCount);
		}

		Assert.Equal(1, providerOwned.DisposeCount);
	}

	private sealed class ProviderOwnedDisposable : IDisposable
	{
		internal int DisposeCount
		{
			get;
			private set;
		}

		public void Dispose()
		{
			DisposeCount++;
		}
	}

	private sealed class ScopeOwnedDisposable : IDisposable
	{
		internal int DisposeCount
		{
			get;
			private set;
		}

		public void Dispose()
		{
			DisposeCount++;
		}
	}
}
