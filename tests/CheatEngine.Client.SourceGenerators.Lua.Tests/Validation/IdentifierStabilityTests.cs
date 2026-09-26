using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using Microsoft.CodeAnalysis;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.Validation;

/// <summary>
///     Identifier stability (DoD D.2): hint names and generated text do not depend on declaration order, and unusual but
///     legal module declarations (global namespace, keyword identifiers) produce compiling code.
/// </summary>
public sealed class IdentifierStabilityTests
{
	private const string Bindings =
		"""
		internal static partial class AlphaLuaBindings
		{
			[CheatEngine.SDK.Annotations.Lua.LuaFunction("alpha_status")]
			public static string Status() => "a";

			public static CheatEngine.SDK.Lua.Registration.LuaRegistrationResult TryRegisterLuaFunctions(
				CheatEngine.SDK.Lua.State.LuaState state,
				CheatEngine.SDK.Lua.Registration.LuaRegistrationCollisionPolicy collisionPolicy =
					CheatEngine.SDK.Lua.Registration.LuaRegistrationCollisionPolicy.RejectExisting) => default;
		}

		internal static partial class BetaLuaBindings
		{
			[CheatEngine.SDK.Annotations.Lua.LuaFunction("beta_status")]
			public static string Status() => "b";

			public static CheatEngine.SDK.Lua.Registration.LuaRegistrationResult TryRegisterLuaFunctions(
				CheatEngine.SDK.Lua.State.LuaState state,
				CheatEngine.SDK.Lua.Registration.LuaRegistrationCollisionPolicy collisionPolicy =
					CheatEngine.SDK.Lua.Registration.LuaRegistrationCollisionPolicy.RejectExisting) => default;
		}
		""";

	private const string AlphaModule =
		"""
		[CheatEngine.Client.Lua.CheatEngineLuaModule(typeof(AlphaLuaBindings), "alpha")]
		internal sealed partial class AlphaLuaModule : CheatEngine.Client.Lua.ILuaModule;
		""";

	private const string BetaModule =
		"""
		[CheatEngine.Client.Lua.CheatEngineLuaModule(typeof(BetaLuaBindings), "beta")]
		internal sealed partial class BetaLuaModule : CheatEngine.Client.Lua.ILuaModule;
		""";

	[Fact]
	public void HintNamesAndGeneratedTextAreStableAcrossDeclarationOrder()
	{
		GeneratorRun alphaFirst = GeneratorRun.Execute("namespace TestPlugin;\n" + Bindings + "\n" + AlphaModule + "\n" + BetaModule);
		GeneratorRun betaFirst = GeneratorRun.Execute("namespace TestPlugin;\n" + BetaModule + "\n" + AlphaModule + "\n" + Bindings);

		Dictionary<string, string> first = Sources(alphaFirst);
		Dictionary<string, string> second = Sources(betaFirst);

		Assert.Equal(
			[
				RegistrarEmitter.RegistrarHintName, RegistrarEmitter.AdapterHintName,
				"TestPlugin_AlphaLuaModule.CheatEngineLuaModule.g.cs", "TestPlugin_BetaLuaModule.CheatEngineLuaModule.g.cs"
			],
			first.Keys.Order(StringComparer.Ordinal));
		Assert.Equal(first.Keys.Order(StringComparer.Ordinal), second.Keys.Order(StringComparer.Ordinal));
		foreach ((string hintName, string text) in first)
		{
			Assert.Equal(text, second[hintName]);
		}
	}

	[Fact]
	public void GlobalNamespaceModuleCompiles()
	{
		GeneratorRun run = GeneratorRun.Execute(Bindings + "\n" + AlphaModule);

		Assert.Empty(run.Diagnostics);
		Assert.Equal("AlphaLuaModule.CheatEngineLuaModule.g.cs", Assert.Single(ModuleSources(run)).HintName);
		AssertCompilesWithoutWarnings(run);
	}

	[Fact]
	public void KeywordNamedModuleCompiles()
	{
		GeneratorRun run = GeneratorRun.Execute("namespace TestPlugin;\n" + Bindings +
												"""

												[CheatEngine.Client.Lua.CheatEngineLuaModule(typeof(AlphaLuaBindings))]
												internal sealed partial class @event : CheatEngine.Client.Lua.ILuaModule;
												""");

		Assert.Empty(run.Diagnostics);
		GeneratedSourceResult source = Assert.Single(ModuleSources(run));
		Assert.EndsWith(".CheatEngineLuaModule.g.cs", source.HintName, StringComparison.Ordinal);
		Assert.Contains("internal partial class @event", source.SourceText.ToString(), StringComparison.Ordinal);
		Assert.Contains("public @event()", source.SourceText.ToString(), StringComparison.Ordinal);
		AssertCompilesWithoutWarnings(run);
	}

	private static IEnumerable<GeneratedSourceResult> ModuleSources(GeneratorRun run)
	{
		return run.GeneratedSources.Where(static source =>
			source.HintName.EndsWith(".CheatEngineLuaModule.g.cs", StringComparison.Ordinal));
	}

	private static Dictionary<string, string> Sources(GeneratorRun run)
	{
		Assert.Empty(run.Diagnostics);
		return run.GeneratedSources.ToDictionary(static source => source.HintName,
			static source => source.SourceText.ToString(), StringComparer.Ordinal);
	}

	private static void AssertCompilesWithoutWarnings(GeneratorRun run)
	{
		Diagnostic[] diagnostics =
		[
			.. run.OutputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
				.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
		];
		Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics));
		using MemoryStream image = new();
		Assert.True(run.OutputCompilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);
	}
}
