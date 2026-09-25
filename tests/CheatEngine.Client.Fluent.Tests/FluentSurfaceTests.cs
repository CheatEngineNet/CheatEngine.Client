using System.Reflection;
using System.Runtime.CompilerServices;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Scanning;

namespace CheatEngine.Client.Fluent.Tests;

/// <summary>Pins the shape of the Fluent public surface: one bound entry point per domain.</summary>
public sealed class FluentSurfaceTests
{
	private static readonly Type[] FluentTypes = typeof(CheatEngineMemoryFluentExtensions).Assembly.GetExportedTypes();

	private static readonly Type[] Builders =
	[
		typeof(MemoryAddressBuilder), typeof(MemoryPointerChainBuilder), typeof(MemoryPrimitiveBatchBuilder<>),
		typeof(AobScanBuilder), typeof(AobFirstMatchBuilder), typeof(AobManyMatchBuilder), typeof(AobSingleMatchBuilder)
	];

	[Fact]
	public void EveryFluentDomainHasOneBoundEntryPoint()
	{
		Dictionary<string, string> expected = new(StringComparer.Ordinal)
		{
			["CheatEngineAobFluentExtensions.Aob(IPatternScanner, AobPattern)"] = nameof(AobScanBuilder),
			["CheatEngineAobFluentExtensions.Aob(IPatternScanner, String)"] = nameof(AobScanBuilder),
			["CheatEngineMemoryFluentExtensions.At(IMemoryClient, Address)"] = nameof(MemoryAddressBuilder),
			["CheatEngineMemoryFluentExtensions.Batch(IMemoryClient)"] = "MemoryPrimitiveBatchBuilder`1"
		};

		Type[] entryPointClasses = [.. FluentTypes.Where(static type => type.IsAbstract && type.IsSealed)];
		Dictionary<string, string> actual = entryPointClasses
			.SelectMany(static type =>
				type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
			.ToDictionary(Describe, static method => method.ReturnType.Name, StringComparer.Ordinal);

		Assert.Equal(expected, actual);
		Assert.Equal(FluentTypes.Length, entryPointClasses.Length + Builders.Length);
		Assert.All(entryPointClasses, static type => Assert.True(type.IsDefined(typeof(ExtensionAttribute), false)));
	}

	[Fact]
	public void BuildersCanOnlyComeFromABoundEntryPoint()
	{
		Assert.All(Builders, static builder =>
		{
			Assert.Contains(builder, FluentTypes);
			Assert.Empty(builder.GetConstructors());
			Assert.DoesNotContain(builder.GetMethods(BindingFlags.Public | BindingFlags.Static), IsBuilderFactory);
			Assert.DoesNotContain(builder.GetMethods(BindingFlags.Public | BindingFlags.Instance),
				static method => method.Name == "Using");
		});
	}

	private static bool IsBuilderFactory(MethodInfo method)
	{
		Type returned = method.ReturnType;
		return Builders.Contains(returned.IsGenericType ? returned.GetGenericTypeDefinition() : returned);
	}

	private static string Describe(MethodInfo method)
	{
		Assert.True(method.IsDefined(typeof(ExtensionAttribute), false), $"{method} is not an extension method.");
		return $"{method.DeclaringType!.Name}.{method.Name}(" +
			   string.Join(", ", method.GetParameters().Select(static parameter => parameter.ParameterType.Name)) + ")";
	}
}
