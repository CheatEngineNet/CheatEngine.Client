using System.Reflection;

using CheatEngine.Client.Memory;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Extensions.DependencyInjection.Tests;

/// <summary>
///     The public surface of the composition layer: typed, never-null options, internal validators, and no implicit
///     memory codec (A5).
/// </summary>
public sealed class CompositionSurfaceTests
{
	[Fact]
	public void OptionsExposeNonNullTableRootsAndMemoryLimitsWithoutSetters()
	{
		PropertyInfo roots = typeof(CheatEngineClientOptions).GetProperty(
			nameof(CheatEngineClientOptions.AllowedTableRoots))!;
		PropertyInfo limits = typeof(CheatEngineClientOptions).GetProperty(
			nameof(CheatEngineClientOptions.MemoryResourceLimits))!;
		NullabilityInfoContext nullability = new();
		CheatEngineClientOptions options = new();

		Assert.Equal(typeof(IList<string>), roots.PropertyType);
		Assert.Equal(typeof(MemoryResourceLimits), limits.PropertyType);
		Assert.Null(roots.SetMethod);
		Assert.Null(limits.SetMethod);
		Assert.Equal(NullabilityState.NotNull, nullability.Create(roots).ReadState);
		Assert.Equal(NullabilityState.NotNull, nullability.Create(limits).ReadState);
		Assert.Empty(options.AllowedTableRoots);
		Assert.False(options.AllowedTableRoots.IsReadOnly);
		Assert.Equal(MemoryResourceLimits.DefaultMaximumReadBytes, options.MemoryResourceLimits.MaximumReadBytes);
	}

	[Fact]
	public void OptionsValidatorsAreRegisteredButNotPublic()
	{
		ServiceCollection services = new();
		services.AddCheatEngineClient();

		Type[] validators = services
			.Where(static descriptor => descriptor.ServiceType == typeof(IValidateOptions<CheatEngineClientOptions>))
			.Select(static descriptor => descriptor.ImplementationType!)
			.ToArray();

		Assert.Equal([typeof(ValidateCheatEngineClientOptions), typeof(CheatEngineClientOptionsSemanticValidator)],
			validators);
		Assert.All(validators, static validator =>
		{
			Assert.False(validator.IsVisible, $"{validator.Name} is public.");
			Assert.True(validator.IsSealed, $"{validator.Name} is not sealed.");
		});
		Assert.DoesNotContain(typeof(CheatEngineClientOptions).Assembly.GetExportedTypes(),
			static type => type.GetInterfaces().Any(static contract =>
				contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IValidateOptions<>)));
	}

	[Fact]
	public void TheCompositionLayerOffersNoCodecShortcutAndNoDefaultCodec()
	{
		Assert.DoesNotContain(typeof(CheatEngineClientBuilder).GetMethods(),
			static method => method.Name == "AddMemoryCodec");
		Assert.DoesNotContain(typeof(CheatEngineClientBuilder).Assembly.GetTypes(),
			static type => type.Name == "DefaultMemoryCodecs");
	}
}
