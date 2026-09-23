using System.Globalization;

using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using Microsoft.CodeAnalysis;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.Diagnostics;

/// <summary>
///     Q16 shape diagnostics CECLUA1201-1204 (DoD D.1, D.7): one owner per Lua global, reserved generated members, no
///     inherited module implementation, and look-alike annotation types are refused, each located and deterministic.
/// </summary>
public sealed class ModuleShapeDiagnosticTests
{
	private const string AlphaFile =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;
		internal static partial class AlphaLuaBindings
		{
			[LuaFunction("shared_status")]
			public static string Status() => "alpha";

			[LuaFunction("alpha_only")]
			public static int AlphaOnly() => 1;
		}

		[CheatEngineLuaModule(typeof(AlphaLuaBindings), "alpha")]
		internal sealed partial class AlphaLuaModule : ILuaModule;
		""";

	private const string BetaFile =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;
		internal static partial class BetaLuaBindings
		{
			[LuaFunction("shared_status")]
			public static string Status() => "beta";
		}

		[CheatEngineLuaModule(typeof(BetaLuaBindings), "beta")]
		internal sealed partial class BetaLuaModule : ILuaModule;
		""";

	private const string BindingsPrefix =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		namespace TestPlugin;
		internal static partial class PluginLuaBindings
		{
			[LuaFunction("status")]
			public static string Status() => "ok";
		}

		""";

	[Fact]
	public void DuplicateExportAcrossModulesReportsCECLUA1201AtTheLaterModule()
	{
		GeneratorRun run = GeneratorRun.ExecuteFiles([("A.cs", AlphaFile), ("B.cs", BetaFile)]);

		Diagnostic diagnostic = Assert.Single(run.Diagnostics);
		Assert.Equal("CECLUA1201", diagnostic.Id);
		Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
		Assert.Equal(
			"Lua global 'shared_status' is exported by Lua module 'TestPlugin.AlphaLuaModule' and again by Lua module 'TestPlugin.BetaLuaModule'; a Lua global has a single owning module per plugin assembly, so remove the export from one of the bindings types",
			diagnostic.GetMessage(CultureInfo.InvariantCulture));
		FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
		Assert.Equal("B.cs", span.Path);
		Assert.Equal(LineOf(BetaFile, "[CheatEngineLuaModule("), span.StartLinePosition.Line);
		// A duplicate owner is refused at build time; both adapters are still emitted so no cascading error hides it.
		Assert.Equal(2, run.GeneratedSources.Length);
	}

	[Fact]
	public void DuplicateExportDiagnosticIsIndependentOfDeclarationOrder()
	{
		GeneratorRun alphaFirst = GeneratorRun.ExecuteFiles([("A.cs", AlphaFile), ("B.cs", BetaFile)]);
		GeneratorRun betaFirst = GeneratorRun.ExecuteFiles([("B.cs", BetaFile), ("A.cs", AlphaFile)]);
		GeneratorRun sameFile = GeneratorRun.ExecuteFiles([("One.cs", BetaFile + "\n" + WithoutHeader(AlphaFile))]);

		Diagnostic first = Assert.Single(alphaFirst.Diagnostics);
		Diagnostic second = Assert.Single(betaFirst.Diagnostics);
		Assert.Equal(first.GetMessage(CultureInfo.InvariantCulture), second.GetMessage(CultureInfo.InvariantCulture));
		Assert.Equal(first.Location.GetLineSpan(), second.Location.GetLineSpan());

		// Inside one file the later declaration is the one reported, whatever the module names are.
		Diagnostic inOneFile = Assert.Single(sameFile.Diagnostics);
		Assert.Contains("'TestPlugin.BetaLuaModule' and again by Lua module 'TestPlugin.AlphaLuaModule'",
			inOneFile.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("public void Register() { }", "Register")]
	[InlineData("public void Unregister(int reason) { }", "Unregister")]
	[InlineData("public int Descriptor => 0;", "Descriptor")]
	[InlineData("public string LastReleaseOutcome => string.Empty;", "LastReleaseOutcome")]
	[InlineData("private static readonly int s_descriptor = 0;", "s_descriptor")]
	[InlineData("private int __CheatEngineLuaState;", "__CheatEngineLuaState")]
	[InlineData("private sealed class __CheatEngineLuaHelper { }", "__CheatEngineLuaHelper")]
	[InlineData("void ILuaModule.Unregister() { }", "ILuaModule.Unregister")]
	public void ReservedMemberDeclarationReportsCECLUA1202AndGeneratesNothing(string member, string reportedName)
	{
		string source = BindingsPrefix +
						"[CheatEngineLuaModule(typeof(PluginLuaBindings), \"plugin\")]\n" +
						"internal sealed partial class PluginLuaModule : ILuaModule\n{\n\t" + member + "\n}\n";

		GeneratorRun run = GeneratorRun.Execute(source);

		Diagnostic diagnostic = Assert.Single(run.Diagnostics);
		Assert.Equal("CECLUA1202", diagnostic.Id);
		Assert.Equal(
			"Lua module 'TestPlugin.PluginLuaModule' declares '" + reportedName +
			"', which is reserved by the generated ownership-aware registration; rename or remove the member",
			diagnostic.GetMessage(CultureInfo.InvariantCulture));
		Assert.Equal(LineOf(source, member), diagnostic.Location.GetLineSpan().StartLinePosition.Line);
		Assert.Empty(run.GeneratedSources);
	}

	[Theory]
	[InlineData(
		"internal abstract class ModuleBase : ILuaModule { public void Register() { } public void Unregister() { } }",
		"TestPlugin.ModuleBase")]
	[InlineData(
		"[CheatEngineLuaModule(typeof(PluginLuaBindings), \"base\")] internal partial class GeneratedBase { }",
		"TestPlugin.GeneratedBase")]
	public void InheritedLuaModuleImplementationReportsCECLUA1203(string baseDeclaration, string baseName)
	{
		string baseType = baseName[(baseName.LastIndexOf('.') + 1)..];
		string source = BindingsPrefix + baseDeclaration + "\n" +
						"internal static partial class DerivedLuaBindings\n{\n\t[LuaFunction(\"derived\")]\n\tpublic static int Derived() => 1;\n}\n" +
						"[CheatEngineLuaModule(typeof(DerivedLuaBindings), \"derived\")]\n" +
						"internal sealed partial class DerivedLuaModule : " + baseType + ";\n";

		GeneratorRun run = GeneratorRun.Execute(source);

		Diagnostic diagnostic = Assert.Single(run.Diagnostics);
		Assert.Equal("CECLUA1203", diagnostic.Id);
		Assert.Equal(
			"Lua module 'TestPlugin.DerivedLuaModule' derives from '" + baseName +
			"', which already implements a Lua module; the generated ownership state of one of them would be bypassed, so derive the module from object",
			diagnostic.GetMessage(CultureInfo.InvariantCulture));
		Assert.Equal(LineOf(source, "internal sealed partial class DerivedLuaModule"),
			diagnostic.Location.GetLineSpan().StartLinePosition.Line);
		Assert.DoesNotContain(run.GeneratedSources,
			static generated => generated.HintName.Contains("DerivedLuaModule", StringComparison.Ordinal));
	}

	[Fact]
	public void LookAlikeModuleAttributeReportsCECLUA1204()
	{
		string source = BindingsPrefix.Replace("namespace TestPlugin;", string.Empty, StringComparison.Ordinal) +
						"""
						namespace CheatEngine.Client.Lua
						{
							[System.AttributeUsage(System.AttributeTargets.Class)]
							internal sealed class CheatEngineLuaModuleAttribute(System.Type bindingsType, string? name = null) : System.Attribute
							{
								public System.Type BindingsType { get; } = bindingsType;
								public string? Name { get; } = name;
							}
						}

						namespace TestPlugin
						{
							[CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
							internal sealed partial class PluginLuaModule : ILuaModule;
						}
						""";

		GeneratorRun run = GeneratorRun.Execute(source);

		Diagnostic diagnostic = Assert.Single(run.Diagnostics);
		Assert.Equal("CECLUA1204", diagnostic.Id);
		Assert.Equal(
			"'CheatEngine.Client.Lua.CheatEngineLuaModuleAttribute' is declared in assembly 'CheatEngineClientLuaGeneratorTests' instead of the contract assembly 'CheatEngine.Client.Abstractions'; the annotation is not a Lua module contract and no module code is generated",
			diagnostic.GetMessage(CultureInfo.InvariantCulture));
		Assert.Equal(LineOf(source, "[CheatEngineLuaModule(typeof(PluginLuaBindings)"),
			diagnostic.Location.GetLineSpan().StartLinePosition.Line);
		Assert.Empty(run.GeneratedSources);
		// The look-alike shadows the contract type (CS0436), which the diagnostic makes actionable.
		Assert.Contains(run.OutputCompilation.GetDiagnostics(TestContext.Current.CancellationToken),
			static compilerDiagnostic => compilerDiagnostic.Id == "CS0436");
	}

	[Fact]
	public void LookAlikeLuaFunctionAttributeReportsCECLUA1204AndIsNotExported()
	{
		string source =
			"""
			using CheatEngine.Client.Lua;
			using CheatEngine.SDK.Annotations.Lua;

			namespace CheatEngine.SDK.Annotations.Lua
			{
				[System.AttributeUsage(System.AttributeTargets.Method)]
				internal sealed class LuaFunctionAttribute(string name) : System.Attribute
				{
					public string Name { get; } = name;
				}
			}

			namespace TestPlugin
			{
				internal static partial class PluginLuaBindings
				{
					[LuaFunction("status")]
					public static string Status() => "ok";

					[LuaFunction("ping")]
					public static int Ping() => 1;
				}

				[CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
				internal sealed partial class PluginLuaModule : ILuaModule;
			}
			""";

		GeneratorRun run = GeneratorRun.Execute(source);

		Assert.Equal(["CECLUA1204", "CECLUA1204"], run.Diagnostics.Select(static diagnostic => diagnostic.Id));
		Assert.All(run.Diagnostics, static diagnostic => Assert.Contains(
			"'CheatEngine.SDK.Annotations.Lua.LuaFunctionAttribute' is declared in assembly 'CheatEngineClientLuaGeneratorTests' instead of the contract assembly 'CheatEngine.SDK.Annotations'",
			diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal));
		Assert.Equal(
			[LineOf(source, "[LuaFunction(\"status\")]"), LineOf(source, "[LuaFunction(\"ping\")]")],
			run.Diagnostics.Select(static diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line));
		Assert.Empty(run.GeneratedSources);
	}

	[Fact]
	public void NewModuleDiagnosticsAreLocatedAndDeterministic()
	{
		string[] sources =
		[
			AlphaFile + "\n" + WithoutHeader(BetaFile),
			BindingsPrefix + "[CheatEngineLuaModule(typeof(PluginLuaBindings), \"plugin\")]\n" +
			"internal sealed partial class PluginLuaModule : ILuaModule\n{\n\tpublic void Register() { }\n}\n",
			BindingsPrefix + "internal abstract class ModuleBase : ILuaModule { public void Register() { } public void Unregister() { } }\n" +
			"[CheatEngineLuaModule(typeof(PluginLuaBindings), \"plugin\")]\n" +
			"internal sealed partial class PluginLuaModule : ModuleBase;\n"
		];
		string[] expectedIds = ["CECLUA1201", "CECLUA1202", "CECLUA1203"];

		for (int index = 0; index < sources.Length; index++)
		{
			Diagnostic first = Assert.Single(GeneratorRun.Execute(sources[index]).Diagnostics);
			Diagnostic second = Assert.Single(GeneratorRun.Execute(sources[index]).Diagnostics);

			Assert.Equal(expectedIds[index], first.Id);
			Assert.NotEqual(Location.None, first.Location);
			Assert.Equal(first.GetMessage(CultureInfo.InvariantCulture), second.GetMessage(CultureInfo.InvariantCulture));
			Assert.Equal(first.Location.GetLineSpan(), second.Location.GetLineSpan());
			Assert.Equal(first.Location.SourceSpan, second.Location.SourceSpan);
		}
	}

	/// <summary>Drops the two usings and the file-scoped namespace so a second file can be appended to the first.</summary>
	private static string WithoutHeader(string source)
	{
		string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		Assert.StartsWith("namespace TestPlugin;", lines[2], StringComparison.Ordinal);
		return string.Join('\n', lines[3..]);
	}

	private static int LineOf(string source, string fragment)
	{
		string normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
		int index = normalized.IndexOf(fragment, StringComparison.Ordinal);
		Assert.True(index >= 0, $"'{fragment}' is not in the source.");
		return normalized[..index].Count(static character => character == '\n');
	}
}
