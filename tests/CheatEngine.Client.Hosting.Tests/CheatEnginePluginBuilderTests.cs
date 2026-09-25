using System.Reflection;

using CheatEngine.Client.Extensions.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Hosting.Tests;

public sealed class CheatEnginePluginBuilderTests
{
	private static readonly ReflectionAssembly PluginAssembly = typeof(CheatEnginePluginBuilderTests).Assembly;

	[Fact]
	public void BuildServiceProviderValidatesServicesAndBindsTheActivationConfiguration()
	{
		CheatEnginePluginBuilder builder = new(PluginAssembly);
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
		CheatEnginePluginBuilder builder = new(PluginAssembly);
		using ServiceProvider provider = builder.BuildServiceProvider();

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.BuildServiceProvider);

		Assert.Contains("only one provider", exception.Message, StringComparison.Ordinal);
	}

	/// <summary>Hosting creates the builder and builds the activation provider; application code can do neither.</summary>
	[Fact]
	public void NothingPublicCreatesTheBuilderOrBuildsItsProvider()
	{
		Type builder = typeof(CheatEnginePluginBuilder);
		MethodInfo? build = builder.GetMethod(nameof(CheatEnginePluginBuilder.BuildServiceProvider),
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		Assert.Empty(builder.GetConstructors());
		Assert.NotNull(build);
		Assert.True(build.IsAssembly);
		Assert.Equal(["Client", "Configuration", "Logging", "PluginDirectory", "Services"],
			builder.GetProperties().Select(static property => property.Name).Order(StringComparer.Ordinal));
		Assert.All(builder.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
									  BindingFlags.DeclaredOnly),
			static method => Assert.True(method.IsSpecialName, $"{method.Name} is a public builder method."));
		Assert.All(builder.GetProperties(), static property => Assert.Null(property.SetMethod));
	}

	[Fact]
	public void PluginDirectoryIsTheFolderOfThePluginAssembly()
	{
		CheatEnginePluginBuilder builder = new(PluginAssembly);

		string directory = builder.PluginDirectory;

		Assert.Equal(Path.GetDirectoryName(PluginAssembly.Location), directory);
		Assert.True(File.Exists(Path.Combine(directory, Path.GetFileName(PluginAssembly.Location))));
	}

	[Fact]
	public void PluginDirectoryRefusesAPluginAssemblyThatWasNotLoadedFromAFile()
	{
		CheatEnginePluginBuilder builder = new(new LocationlessAssembly());

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => builder.PluginDirectory);

		Assert.Contains("not loaded from a file", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void LoggingIsTheLoggingViewOfTheActivationServices()
	{
		CheatEnginePluginBuilder builder = new(PluginAssembly);

		Assert.Same(builder.Services, builder.Logging.Services);
	}

	/// <summary>
	///     The Core diagnostics of an activation log through the activation provider's logger factory, so a provider added
	///     through <see cref="CheatEnginePluginBuilder.Logging" /> receives them under their domain category.
	/// </summary>
	[Fact]
	public void ProvidersAddedThroughLoggingReceiveTheCoreDiagnosticEvents()
	{
		CapturingLoggerProvider logs = new();
		CheatEnginePluginBuilder builder = new(PluginAssembly);
		builder.Logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs);

		using ServiceProvider provider = builder.BuildServiceProvider();
		LoggerCoreDiagnostics diagnostics = new(provider.GetRequiredService<ILoggerFactory>());
		diagnostics.PointerWidthMismatchRefused("Memory.ReadPrimitive", 8, 4);

		(string Category, EventId EventId) entry = Assert.Single(logs.Entries);
		Assert.Equal(LoggerCoreDiagnostics.MemoryCategory, entry.Category);
		Assert.Equal(1200, entry.EventId.Id);
	}

	[Fact]
	public void TwoScopesShareProviderSingletonsButDisposeTheirOwnScopedServices()
	{
		CheatEnginePluginBuilder builder = new(PluginAssembly);
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

	/// <summary>An assembly loaded from memory: it has no file location.</summary>
	private sealed class LocationlessAssembly : ReflectionAssembly
	{
		public override string Location => string.Empty;
	}

	private sealed class CapturingLoggerProvider : ILoggerProvider
	{
		private readonly List<(string Category, EventId EventId)> _entries = [];
		private readonly Lock _gate = new();

		internal IReadOnlyList<(string Category, EventId EventId)> Entries
		{
			get
			{
				lock (_gate)
				{
					return [.. _entries];
				}
			}
		}

		public ILogger CreateLogger(string categoryName)
		{
			return new CapturingLogger(this, categoryName);
		}

		public void Dispose()
		{
		}

		private void Add(string category, EventId eventId)
		{
			lock (_gate)
			{
				_entries.Add((category, eventId));
			}
		}

		private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state)
				where TState : notnull
			{
				return null;
			}

			public bool IsEnabled(LogLevel logLevel)
			{
				return true;
			}

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
				Func<TState, Exception?, string> formatter)
			{
				owner.Add(category, eventId);
			}
		}
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
