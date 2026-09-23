using System.Reflection;

using CheatEngine.Client.Lua;
using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.SdkPort;

/// <summary>
///     The SDK port emitted for one real three-export module, with the semantic model of its compilation. The SDK port is
///     the only generated code that runs against the real Lua state, and no test in this repository can execute it (no Lua
///     fixture), so the checks of this folder read it through Roslyn.
/// </summary>
internal sealed class GeneratedSdkPort
{
	internal const string PortName = "__CheatEngineLuaSdkPort";

	private const string ExportNamesField = "__CheatEngineLuaExportNames";

	private const string GeneratedFileSuffix = "PluginLuaModule.CheatEngineLuaModule.g.cs";

	private const string ModuleSource =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		using CheatEngine.SDK.Lua.Calls;
		using CheatEngine.SDK.Lua.State;
		namespace TestPlugin;
		internal static partial class PluginLuaBindings
		{
			[LuaFunction("status")]
			public static string Status() => "ok";

			[LuaFunction("ping")]
			public static int Ping() => 1;

			[LuaFunction("marker")]
			public static string Marker() => "marker";

			// Stands for the SDK LuaBindings output, which the Client generator cannot see in a test compilation.
			public static LuaStatus RegisterLuaFunctions(LuaState state) => default;
		}

		[CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
		internal sealed partial class PluginLuaModule : ILuaModule;
		""";

	private static readonly Lazy<GeneratedSdkPort> SShared = new(Generate, LazyThreadSafetyMode.ExecutionAndPublication);

	private GeneratedSdkPort(Compilation compilation, SyntaxTree tree)
	{
		Compilation = compilation;
		Tree = tree;
		Model = compilation.GetSemanticModel(tree);
		Declaration = Assert.Single(
			tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<StructDeclarationSyntax>(),
			static node => node.Identifier.ValueText == PortName);
	}

	/// <summary>Delegate shape of the generated <c>ExportName</c>, whose span result cannot cross reflection invoke.</summary>
	internal delegate ReadOnlySpan<byte> ExportNameFunction(int export);

	/// <summary>Gets the unmutated port, generated once per test run.</summary>
	internal static GeneratedSdkPort Shared => SShared.Value;

	internal Compilation Compilation
	{
		get;
	}

	internal SyntaxTree Tree
	{
		get;
	}

	internal SemanticModel Model
	{
		get;
	}

	internal StructDeclarationSyntax Declaration
	{
		get;
	}

	internal MethodDeclarationSyntax Method(string name)
	{
		return Assert.Single(Declaration.Members.OfType<MethodDeclarationSyntax>(),
			method => method.Identifier.ValueText == name);
	}

	/// <summary>Returns the port of a compilation in which one node of the generated file was replaced.</summary>
	/// <remarks>The mutated compilation must still compile: a mutation the compiler rejects would prove nothing.</remarks>
	internal GeneratedSdkPort Mutate(Func<GeneratedSdkPort, (SyntaxNode Original, SyntaxNode Replacement)> mutation)
	{
		ArgumentNullException.ThrowIfNull(mutation);

		(SyntaxNode original, SyntaxNode replacement) = mutation(this);
		SyntaxNode root = Tree.GetRoot(TestContext.Current.CancellationToken).ReplaceNode(original, replacement);
		SyntaxTree mutated = Tree.WithRootAndOptions(root, Tree.Options);
		Compilation compilation = Compilation.ReplaceSyntaxTree(Tree, mutated);
		Diagnostic[] errors =
		[
			.. compilation.GetDiagnostics(TestContext.Current.CancellationToken)
				.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
		];
		Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
		return new GeneratedSdkPort(compilation, mutated);
	}

	/// <summary>
	///     Emits and loads the module, then returns its descriptor exports, the generated export-name table and the
	///     generated <c>ExportName</c> of the SDK port bound as a delegate (it touches no Lua state).
	/// </summary>
	internal (string[] DescriptorExports, string[] ExportNames, ExportNameFunction ExportName) Load()
	{
		using MemoryStream image = new();
		EmitResult result = Compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
		Type moduleType = System.Reflection.Assembly.Load(image.ToArray()).GetType("TestPlugin.PluginLuaModule", true)!;
		IDescribedLuaModule module = (IDescribedLuaModule) Activator.CreateInstance(moduleType)!;

		const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
		string[] exportNames = Assert.IsType<string[]>(moduleType.GetField(ExportNamesField, Private)!.GetValue(null));
		Type port = moduleType.GetNestedType(PortName, BindingFlags.NonPublic)!;
		ExportNameFunction exportName = port.GetMethod("ExportName", Private)!.CreateDelegate<ExportNameFunction>();
		return ([.. module.Descriptor.Exports.Select(static export => export.Name)], exportNames, exportName);
	}

	private static GeneratedSdkPort Generate()
	{
		GeneratorRun run = GeneratorRun.Execute(ModuleSource);
		Assert.Empty(run.Diagnostics);
		SyntaxTree generated = Assert.Single(run.OutputCompilation.SyntaxTrees,
			static tree => tree.FilePath.EndsWith(GeneratedFileSuffix, StringComparison.Ordinal));
		return new GeneratedSdkPort(run.OutputCompilation, generated);
	}
}
