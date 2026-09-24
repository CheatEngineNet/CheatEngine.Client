using System.Reflection;

using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.Composition;

/// <summary>
///     CRIT-07: the Client generator and the real CheatEngine.SDK LuaBindings generator of the pinned package run on the
///     same compilation, and what each emits must compile against what the other emits. The other generator tests replace
///     the SDK output with hand-written stubs; this is the proof of the contract between the two generators.
/// </summary>
/// <remarks>
///     The SDK generator is loaded from
///     <c>$(NuGetPackageRoot)cheatengine.sdk/$(CheatEngineSdkVersion)/analyzers/dotnet/cs</c>, which the test project
///     passes as assembly metadata with the pin itself: never a user path.
/// </remarks>
public sealed class RealSdkGeneratorCompositionTests
{
	private const string DirectoryMetadataKey = "CheatEngine.Client.Tests.SdkGeneratorDirectory";

	private const string VersionMetadataKey = "CheatEngine.Client.Tests.SdkVersion";

	private const string Source =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;

		internal static partial class PluginLuaBindings
		{
			[LuaFunction("composition_status")]
			public static string Status() => "ok";

			[LuaFunction("composition_add")]
			public static long Add(long left, long right) => left + right;
		}

		[CheatEngineLuaModule(typeof(PluginLuaBindings), "composition")]
		internal sealed partial class PluginLuaModule : ILuaModule;

		internal static partial class Globals
		{
			[CheatEngineLuaOperation]
			[LuaGlobal("getVersion")]
			public static partial int ReadVersion(int address);

			[CheatEngineLuaOperation]
			[LuaGlobal("tryGetVersion")]
			public static partial bool TryReadVersion(long address, out long version);
		}

		internal static class Consumer
		{
			internal static long Run(ILuaClient client)
			{
				int version = client.Execute(Globals.CreateReadVersionLuaOperation(1));
				return client.TryExecute(Globals.CreateTryReadVersionLuaOperation(2), out long value, out _)
					? value + version
					: version;
			}
		}
		""";

	private static readonly Lazy<GeneratorRun> SRun = new(Run, LazyThreadSafetyMode.ExecutionAndPublication);

	[Fact]
	public void TheSdkGeneratorIsLoadedFromThePinnedPackageFolder()
	{
		string directory = SdkGeneratorDirectory();

		string[] segments = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
			.Split(Path.DirectorySeparatorChar);
		Assert.True(Path.IsPathFullyQualified(directory), directory);
		Assert.Equal(["cheatengine.sdk", Metadata(VersionMetadataKey), "analyzers", "dotnet", "cs"], segments[^5..]);
		Assert.True(File.Exists(Path.Combine(directory, "CheatEngine.SDK.SourceGenerators.LuaBindings.dll")));
		Assert.True(File.Exists(Path.Combine(directory, "CheatEngine.SDK.SourceGenerators.Shared.dll")));
	}

	[Fact]
	public void BothGeneratorsRunAndTheirOutputCompilesWithoutDiagnostics()
	{
		GeneratorRun run = SRun.Value;

		Assert.Equal(2, run.Result.Results.Length);
		Assert.All(run.Result.Results, static result => Assert.Null(result.Exception));
		Assert.Empty(run.Diagnostics.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
		Diagnostic[] compilerDiagnostics =
		[
			.. run.OutputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
				.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
		];
		Assert.True(compilerDiagnostics.Length == 0, string.Join(Environment.NewLine, compilerDiagnostics));
		Assert.NotEmpty(Emit(run));
	}

	[Fact]
	public void TheModuleRegistersOnlyThroughTheSdkRegistrationSet()
	{
		GeneratorRun run = SRun.Value;
		string sdkRegistration = SdkGeneratedText(run, "TryRegisterLuaFunctions(");

		// The SDK-generated ownership-aware registration publishes through LuaRegistrationSet.Register, which wraps every
		// thunk in the closure that captures the attachment and Lua state identity: a function a script kept after disable
		// or reset raises an ordinary Lua error instead of entering the plugin (CRIT-07).
		Assert.Contains("LuaRegistrationSet.Register(", sdkRegistration, StringComparison.Ordinal);
		string[] called = [.. IlCallScanner.CalledMethodNames(Emit(run))];
		Assert.Contains("TryRegisterLuaFunctions", called);
		Assert.DoesNotContain("RegisterLuaFunctions", called);
		Assert.DoesNotContain("UnregisterLuaFunctions", called);
	}

	[Fact]
	public void IntegerResultsAreReadWithTheSdkMarshallersThatRefuseAFloatAtOrAbove2Pow53()
	{
		GeneratorRun run = SRun.Value;
		string globals = SdkGeneratedText(run, "ReadVersion(int address)");

		// CRIT-07: the SDK reads an integer result with its integer marshaller, which refuses a Lua float at or above
		// 2^53 instead of rounding it. The throwing form then raises a LuaException, which the generated Client operation
		// reports as a LuaError failure (see OperationRefusalTests); the Try form returns false, reported the same way.
		Assert.Contains("Int32Marshaller", globals, StringComparison.Ordinal);
		Assert.Contains("Int64Marshaller", globals, StringComparison.Ordinal);
		Assert.Contains("ThrowUnexpectedResult", globals, StringComparison.Ordinal);
	}

	private static GeneratorRun Run()
	{
		ISourceGenerator sdkGenerator = LoadSdkLuaBindingsGenerator();
		return GeneratorRun.ExecuteFiles([("Composition.cs", Source)], SdkAssemblyReferences(), [sdkGenerator]);
	}

	private static ISourceGenerator LoadSdkLuaBindingsGenerator()
	{
		string directory = SdkGeneratorDirectory();
		// The shared model assembly must be loadable before the generator that depends on it.
		_ = System.Reflection.Assembly.LoadFrom(Path.Combine(directory, "CheatEngine.SDK.SourceGenerators.Shared.dll"));
		System.Reflection.Assembly generators =
			System.Reflection.Assembly.LoadFrom(Path.Combine(directory, "CheatEngine.SDK.SourceGenerators.LuaBindings.dll"));
		Type generatorType = Assert.Single(generators.GetTypes(), static type =>
			typeof(IIncrementalGenerator).IsAssignableFrom(type) && type.GetCustomAttribute<GeneratorAttribute>() is not null);
		return ((IIncrementalGenerator) Activator.CreateInstance(generatorType)!).AsSourceGenerator();
	}

	private static string SdkGeneratorDirectory()
	{
		return Metadata(DirectoryMetadataKey);
	}

	private static string Metadata(string key)
	{
		AssemblyMetadataAttribute metadata = Assert.Single(
			typeof(RealSdkGeneratorCompositionTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>(),
			attribute => attribute.Key == key);
		Assert.False(string.IsNullOrWhiteSpace(metadata.Value), $"{key} is not set by the test project.");
		return metadata.Value!;
	}

	// Every CheatEngine.SDK assembly next to the tests: the SDK-generated code uses the Lua and interop assemblies.
	private static MetadataReference[] SdkAssemblyReferences()
	{
		return
		[
			.. Directory.EnumerateFiles(AppContext.BaseDirectory, "CheatEngine.SDK*.dll")
				.Order(StringComparer.Ordinal)
				.Select(static path => MetadataReference.CreateFromFile(path))
		];
	}

	private static string SdkGeneratedText(GeneratorRun run, string fragment)
	{
		GeneratorRunResult sdk = Assert.Single(run.Result.Results, static result =>
			result.Generator.GetGeneratorType().Assembly.GetName().Name == "CheatEngine.SDK.SourceGenerators.LuaBindings");
		return Assert.Single(sdk.GeneratedSources, source =>
			source.SourceText.ToString().Contains(fragment, StringComparison.Ordinal)).SourceText.ToString();
	}

	private static byte[] Emit(GeneratorRun run)
	{
		using MemoryStream image = new();
		EmitResult result = run.OutputCompilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
		return image.ToArray();
	}
}
