#pragma warning disable CECLIENT5004 // These tests exercise the experimental Auto Assembler opt-in.

using System.Reflection;

using CheatEngine.Client.Assembly;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Client.Extensions.DependencyInjection.Tests;

/// <summary>
///     <c>EnableAutoAssemblerPatches()</c> is the only path that registers <see cref="IAutoAssemblerClient" /> and sets
///     the activation policy, exactly like the unsafe Lua opt-in (plan L17, Q44).
/// </summary>
public sealed class AutoAssemblerPatchesRegistrationTests
{
	[Fact]
	[Trait("Qualification", "Q44")]
	public void WithoutTheOptInNothingIsRegisteredAndThePolicyStaysDisabled()
	{
		ServiceCollection services = new();
		services.AddCheatEngineClient();

		using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});

		Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(IAutoAssemblerClient));
		Assert.DoesNotContain(services,
			static descriptor => descriptor.ServiceType == typeof(AutoAssemblerPatchesRegistration));
		Assert.Null(provider.GetService<IAutoAssemblerClient>());
		Assert.False(ReadPolicy(provider, "EnableAutoAssemblerPatches"));
	}

	[Fact]
	public void TheOptInRegistersOneClientAndEnablesOnlyItsOwnPolicy()
	{
		ServiceCollection services = new();
		CheatEngineClientBuilder builder = services.AddCheatEngineClient();

		Assert.Same(builder, builder.EnableAutoAssemblerPatches().EnableAutoAssemblerPatches());
		using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});

		ServiceDescriptor client = Assert.Single(services,
			static descriptor => descriptor.ServiceType == typeof(IAutoAssemblerClient));
		Assert.Equal(ServiceLifetime.Singleton, client.Lifetime);
		Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(AutoAssemblerPatchesRegistration));
		Assert.True(ReadPolicy(provider, "EnableAutoAssemblerPatches"));
		Assert.False(ReadPolicy(provider, "EnableUnsafeLuaExecution"));
	}

	[Fact]
	public void AClientRegisteredByAnotherPathIsRefused()
	{
		ServiceCollection services = new();
		CheatEngineClientBuilder builder = services.AddCheatEngineClient();
		services.AddSingleton<IAutoAssemblerClient>(static _ =>
			throw new InvalidOperationException("A foreign registration must never be resolved."));

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => builder.EnableAutoAssemblerPatches());

		Assert.Contains("EnableAutoAssemblerPatches()", exception.Message, StringComparison.Ordinal);
		Assert.DoesNotContain(services,
			static descriptor => descriptor.ServiceType == typeof(AutoAssemblerPatchesRegistration));
	}

	[Fact]
	public void ConfigurationCannotEnableAutoAssemblerPatches()
	{
		using ConfigurationManager configuration = new();
		configuration["CheatEngineClient:EnableAutoAssemblerPatches"] = "true";
		ServiceCollection services = new();
		services.AddCheatEngineClient(configuration);
		using ServiceProvider provider = services.BuildServiceProvider();

		Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(IAutoAssemblerClient));
		Assert.Empty(provider.GetServices<IAutoAssemblerClient>());
		Assert.False(ReadPolicy(provider, "EnableAutoAssemblerPatches"));
	}

	/// <summary>Reads one flag of the activation policy that the provider composes (a Core-internal type).</summary>
	private static bool ReadPolicy(IServiceProvider provider, string flag)
	{
		Type policyType = Type.GetType("CheatEngine.Client.Core.Infrastructure.CoreClientPolicy, CheatEngine.Client.Core",
			throwOnError: true)!;
		object policy = provider.GetRequiredService(policyType);
		PropertyInfo property = policyType.GetProperty(flag, BindingFlags.Instance | BindingFlags.NonPublic) ??
								throw new InvalidOperationException($"CoreClientPolicy has no {flag} flag.");
		return (bool) property.GetValue(policy)!;
	}
}
