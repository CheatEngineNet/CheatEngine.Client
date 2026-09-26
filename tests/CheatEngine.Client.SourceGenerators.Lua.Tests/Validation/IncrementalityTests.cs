using System.Collections.Immutable;
using System.Reflection;

using CheatEngine.Client.SourceGenerators.Lua.Model;
using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.Validation;

/// <summary>
///     Incremental-pipeline hygiene (audit ch.19 §Validation, DoD D.3): the parsed models are equatable values that root no
///     compilation object, so an unrelated edit or an identical re-parse leaves the module pipeline cached.
/// </summary>
public sealed class IncrementalityTests
{
	private const string ModuleTrackingName = "CheatEngineLuaModule";
	private const string OperationTrackingName = "CheatEngineLuaOperation";

	private const string ModuleAndOperationSource =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;
		internal static partial class PluginLuaBindings
		{
			[LuaFunction("status")]
			public static string Status() => "ok";

			[LuaFunction("ping")]
			public static int Ping() => 1;
		}

		[CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
		internal sealed partial class PluginLuaModule : ILuaModule;

		internal static partial class Globals
		{
			[CheatEngineLuaOperation]
			[LuaGlobal("getVersion")]
			public static partial int ReadVersion(int address);
		}
		""";

	private static readonly Type[] ForbiddenModelFieldTypes =
	[
		typeof(ISymbol), typeof(SyntaxNode), typeof(SyntaxTree), typeof(Compilation), typeof(SemanticModel),
		typeof(Location), typeof(Diagnostic), typeof(AttributeData)
	];

	[Fact]
	public void UnrelatedEditLeavesTheModulePipelineCachedOrUnchanged()
	{
		GeneratorRun first = GeneratorRun.Execute(ModuleAndOperationSource);
		Assert.Empty(first.Diagnostics);
		Compilation edited = first.InputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
			"namespace TestPlugin; internal static class Unrelated { internal const int Value = 42; }",
			new CSharpParseOptions(LanguageVersion.CSharp14),
			cancellationToken: TestContext.Current.CancellationToken));

		GeneratorRun second = GeneratorRun.Execute(first.Driver, edited);

		GeneratorRunResult result = Assert.Single(second.Result.Results);
		AssertEveryOutputIsReused(result.TrackedSteps[ModuleTrackingName], ModuleTrackingName);
		AssertEveryOutputIsReused(result.TrackedSteps[OperationTrackingName], OperationTrackingName);
		Assert.NotEmpty(result.TrackedOutputSteps);
		foreach ((string name, ImmutableArray<IncrementalGeneratorRunStep> steps) in result.TrackedOutputSteps)
		{
			AssertEveryOutputIsReused(steps, name);
		}

		Assert.Equal(first.GeneratedSources.Select(static source => source.SourceText.ToString()),
			second.GeneratedSources.Select(static source => source.SourceText.ToString()));
	}

	[Fact]
	public void ReparsedIdenticalModuleProducesAnUnchangedModel()
	{
		GeneratorRun first = GeneratorRun.Execute(ModuleAndOperationSource);
		SyntaxTree original = Assert.Single(first.InputCompilation.SyntaxTrees);
		Compilation reparsed = first.InputCompilation.ReplaceSyntaxTree(original, CSharpSyntaxTree.ParseText(original.ToString(),
			(CSharpParseOptions) original.Options, original.FilePath,
			cancellationToken: TestContext.Current.CancellationToken));

		GeneratorRun second = GeneratorRun.Execute(first.Driver, reparsed);

		GeneratorRunResult result = Assert.Single(second.Result.Results);
		ImmutableArray<IncrementalGeneratorRunStep> moduleSteps = result.TrackedSteps[ModuleTrackingName];
		Assert.Contains(moduleSteps.SelectMany(static step => step.Outputs),
			static output => output.Reason == IncrementalStepRunReason.Unchanged);
		AssertEveryOutputIsReused(moduleSteps, ModuleTrackingName);
		foreach ((string name, ImmutableArray<IncrementalGeneratorRunStep> steps) in result.TrackedOutputSteps)
		{
			AssertEveryOutputIsReused(steps, name);
		}
	}

	[Fact]
	public void ModuleAndOperationModelsHoldNoRoslynObjects()
	{
		Type[] models =
		[
			typeof(ModuleModel), typeof(OperationModel), typeof(OperationParameter), typeof(DiagnosticInfo),
			typeof(LocationInfo), typeof(EquatableArray<string>), typeof(EquatableArray<DiagnosticInfo>),
			typeof(EquatableArray<OperationParameter>)
		];

		List<string> violations = [];
		HashSet<Type> visited = [];
		foreach (Type model in models)
		{
			Assert.Contains(typeof(IEquatable<>).MakeGenericType(model), model.GetInterfaces());
			CollectForbiddenFields(model, model.Name, visited, violations);
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	private static void AssertEveryOutputIsReused(ImmutableArray<IncrementalGeneratorRunStep> steps, string name)
	{
		Assert.NotEmpty(steps);
		foreach (IncrementalGeneratorRunStep step in steps)
		{
			foreach ((object _, IncrementalStepRunReason reason) in step.Outputs)
			{
				Assert.True(reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
					$"Step '{name}' produced a '{reason}' output for an input that did not change.");
			}
		}
	}

	private static void CollectForbiddenFields(Type type, string path, HashSet<Type> visited, List<string> violations)
	{
		if (type.IsPrimitive || type == typeof(string) || type.IsEnum || !visited.Add(type))
		{
			return;
		}

		if (type.IsArray)
		{
			CollectForbiddenFields(type.GetElementType()!, path + "[]", visited, violations);
			return;
		}

		foreach (Type forbidden in ForbiddenModelFieldTypes)
		{
			if (forbidden.IsAssignableFrom(type))
			{
				violations.Add($"{path} holds a {forbidden.Name}.");
			}
		}

		if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
		{
			return;
		}

		foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			CollectForbiddenFields(field.FieldType, path + "." + field.Name, visited, violations);
		}
	}
}
