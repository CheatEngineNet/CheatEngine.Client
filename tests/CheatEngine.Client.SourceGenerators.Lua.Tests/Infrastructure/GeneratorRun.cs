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
	private GeneratorRun(GeneratorDriver driver, GeneratorDriverRunResult result, Compilation inputCompilation,
		Compilation outputCompilation, ImmutableArray<Diagnostic> diagnostics)
	{
		Driver = driver;
		InputCompilation = inputCompilation;
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

	/// <summary>Gets the compilation the generator ran on, without the generated trees.</summary>
	public Compilation InputCompilation
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
		return Execute([source], []);
	}

	/// <summary>Runs the generator over several source files, optionally with extra metadata references.</summary>
	public static GeneratorRun Execute(string[] sources, MetadataReference[] additionalReferences)
	{
		ArgumentNullException.ThrowIfNull(sources);
		ArgumentNullException.ThrowIfNull(additionalReferences);

		return ExecuteFiles(
			[.. sources.Select((source, index) => (sources.Length == 1 ? string.Empty : $"Source{index}.cs", source))],
			additionalReferences);
	}

	/// <summary>Runs the generator over source files with explicit paths, in the given syntax-tree order.</summary>
	public static GeneratorRun ExecuteFiles((string Path, string Source)[] files,
		MetadataReference[]? additionalReferences = null)
	{
		return ExecuteFiles(files, additionalReferences, []);
	}

	/// <summary>
	///     Runs the Client generator next to <paramref name="otherGenerators" /> over source files with explicit paths, so
	///     the output compilation contains what every generator emitted.
	/// </summary>
	public static GeneratorRun ExecuteFiles((string Path, string Source)[] files,
		MetadataReference[]? additionalReferences, ISourceGenerator[] otherGenerators)
	{
		ArgumentNullException.ThrowIfNull(files);
		ArgumentNullException.ThrowIfNull(otherGenerators);

		CSharpParseOptions parseOptions = new(LanguageVersion.CSharp14);
		SyntaxTree[] syntaxTrees =
		[
			.. files.Select(file => CSharpSyntaxTree.ParseText(SourceText.From(file.Source), parseOptions, file.Path))
		];
		CSharpCompilation compilation = CSharpCompilation.Create(
			"CheatEngineClientLuaGeneratorTests",
			syntaxTrees,
			[.. GetMetadataReferences(), .. additionalReferences ?? []],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			[new CheatEngineLuaGenerator().AsSourceGenerator(), .. otherGenerators],
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
		return new GeneratorRun(updated, updated.GetRunResult(), compilation, outputCompilation, diagnostics);
	}

	public string GeneratedText(string suffix)
	{
		GeneratedSourceResult source = Assert.Single(GeneratedSources.Where(source =>
			source.HintName.EndsWith(suffix, StringComparison.Ordinal)));
		return source.SourceText.ToString();
	}

	public static Compilation CreateConsumerCompilation(string source, byte[] generatedAssembly)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(generatedAssembly);

		CSharpParseOptions parseOptions = new(LanguageVersion.CSharp14);
		return CSharpCompilation.Create(
			"CheatEngineClientLuaGeneratedConsumerTests",
			[CSharpSyntaxTree.ParseText(SourceText.From(source), parseOptions)],
			[.. GetMetadataReferences(), MetadataReference.CreateFromImage(generatedAssembly)],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
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
