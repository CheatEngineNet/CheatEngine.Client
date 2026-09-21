using System.Collections.Immutable;

using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Lua;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Runtime;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

internal sealed class GeneratorRun
{
	private GeneratorRun(GeneratorDriver driver, GeneratorDriverRunResult result, Compilation outputCompilation,
		ImmutableArray<Diagnostic> diagnostics)
	{
		Driver = driver;
		Result = result;
		OutputCompilation = outputCompilation;
		Diagnostics = diagnostics;
	}

	public GeneratorDriver Driver
	{
		get;
	}

	public GeneratorDriverRunResult Result
	{
		get;
	}

	public Compilation OutputCompilation
	{
		get;
	}

	public ImmutableArray<Diagnostic> Diagnostics
	{
		get;
	}

	public ImmutableArray<GeneratedSourceResult> GeneratedSources => Assert.Single(Result.Results).GeneratedSources;

	public static GeneratorRun Execute(string source)
	{
		CSharpParseOptions parseOptions = new(LanguageVersion.CSharp14);
		SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(source), parseOptions);
		CSharpCompilation compilation = CSharpCompilation.Create(
			"CheatEngineClientLuaGeneratorTests",
			[syntaxTree],
			GetMetadataReferences(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			[new CheatEngineLuaGenerator().AsSourceGenerator()],
			parseOptions: parseOptions,
			driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None,
				true));
		return Execute(driver, compilation);
	}

	public static GeneratorRun Execute(GeneratorDriver driver, Compilation compilation)
	{
		GeneratorDriver updated = driver.RunGeneratorsAndUpdateCompilation(
			compilation,
			out Compilation outputCompilation,
			out ImmutableArray<Diagnostic> diagnostics,
			TestContext.Current.CancellationToken);
		return new GeneratorRun(updated, updated.GetRunResult(), outputCompilation, diagnostics);
	}

	public string GeneratedText(string suffix)
	{
		GeneratedSourceResult source = Assert.Single(GeneratedSources.Where(source =>
			source.HintName.EndsWith(suffix, StringComparison.Ordinal)));
		return source.SourceText.ToString();
	}

	private static ImmutableArray<MetadataReference> GetMetadataReferences()
	{
		HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
		string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
		if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
		{
			foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator))
			{
				paths.Add(path);
			}
		}

		AddAssembly(paths, typeof(CheatEngineLuaModuleAttribute).Assembly);
		AddAssembly(paths, typeof(CheatEngineClientBuilder).Assembly);
		AddAssembly(paths, typeof(LuaFunctionAttribute).Assembly);
		AddAssembly(paths, typeof(LuaRuntime).Assembly);
		AddAssembly(paths, typeof(ServiceCollection).Assembly);
		return [.. paths.Select(static path => MetadataReference.CreateFromFile(path))];
	}

	private static void AddAssembly(HashSet<string> paths, System.Reflection.Assembly assembly)
	{
		if (!string.IsNullOrWhiteSpace(assembly.Location))
		{
			paths.Add(assembly.Location);
		}
	}
}
