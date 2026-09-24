using System.Reflection;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.Validation;

/// <summary>
///     The module contract, its public projection and the emitted code are compared separately (audit ch.19 §Validation,
///     DoD D.8): a formatting change is not a contract change, and a textually stable file can still change the contract.
/// </summary>
public sealed class ModuleContractTests
{
	private const string BindingsPrefix =
		"""
		using CheatEngine.Client.Lua;
		using CheatEngine.SDK.Annotations.Lua;
		using CheatEngine.SDK.Lua.Calls;
		using CheatEngine.SDK.Lua.Registration;
		using CheatEngine.SDK.Lua.State;
		namespace TestPlugin;
		internal static partial class PluginLuaBindings
		{
			[LuaFunction("status")]
			public static string Status() => "ok";

			[LuaFunction("ping")]
			public static int Ping() => 1;

			[LuaFunction("marker")]
			public static string Marker() => "m";

			public static LuaRegistrationResult TryRegisterLuaFunctions(LuaState state,
				LuaRegistrationCollisionPolicy collisionPolicy = LuaRegistrationCollisionPolicy.RejectExisting) => default;
		""";

	private const string ModuleDeclaration =
		"""

		[CheatEngineLuaModule(typeof(PluginLuaBindings), "plugin")]
		public sealed partial class PluginLuaModule : ILuaModule;
		""";

	private const string ModuleWithLegacyHelpers =
		BindingsPrefix + "\n\tpublic static LuaStatus RegisterLuaFunctions(LuaState state) => default;" +
		"\n\tpublic static LuaStatus UnregisterLuaFunctions(LuaState state) => default;\n}" + ModuleDeclaration;

	private const string ModuleWithoutLegacyHelpers = BindingsPrefix + "\n}" + ModuleDeclaration;

	[Fact]
	public void ModuleDescriptorContractIsUnchanged()
	{
		ILuaModule module = (ILuaModule) Activator.CreateInstance(LoadModuleType(ModuleWithLegacyHelpers))!;

		Assert.Equal("plugin", module.Descriptor.Name);
		Assert.Equal(["status", "ping", "marker"], module.Descriptor.Exports.Select(static export => export.Name));
	}

	[Fact]
	public void GeneratedModulePublicProjectionIsStable()
	{
		Type moduleType = LoadModuleType(ModuleWithLegacyHelpers);
		const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
									BindingFlags.DeclaredOnly;

		Assert.Equal(
			[".ctor()", "Descriptor", "Register()", "Unregister()", "get_Descriptor()"],
			moduleType.GetMembers(Public).Select(Describe).Order(StringComparer.Ordinal));
		Assert.Equal([typeof(ILuaModule)], moduleType.GetInterfaces());
		Assert.Equal(typeof(LuaModuleReleaseOutcome), moduleType.GetMethod("Unregister")!.ReturnType);
		foreach (MemberInfo member in moduleType.GetMembers(Public))
		{
			Assert.DoesNotContain(PublicSignatureTypes(member), static type =>
				type.Namespace?.StartsWith("CheatEngine.SDK", StringComparison.Ordinal) == true);
		}
	}

	[Fact]
	public void GeneratedModuleRegistersOnlyThroughTheOwnershipAwareSdkRegistration()
	{
		GeneratorRun run = GeneratorRun.Execute(ModuleWithLegacyHelpers);
		string generated = run.GeneratedText("PluginLuaModule.CheatEngineLuaModule.g.cs");
		Assert.DoesNotContain("UnregisterLuaFunctions", generated, StringComparison.Ordinal);
		Assert.Equal(1, Count(generated, "TryRegisterLuaFunctions("));
		Assert.Contains("LuaRegistrationCollisionPolicy.RejectExisting", generated, StringComparison.Ordinal);

		string[] invokedBindings =
		[
			.. IlCallScanner.CalledMethodNames(Emit(run))
				.Where(static name => name.EndsWith("LuaFunctions", StringComparison.Ordinal))
		];
		Assert.Equal(["TryRegisterLuaFunctions"], invokedBindings);
	}

	[Fact]
	public void GeneratedModuleCompilesWhenTheBindingsHaveNoLegacyHelpers()
	{
		GeneratorRun run = GeneratorRun.Execute(ModuleWithoutLegacyHelpers);

		Assert.Empty(run.Diagnostics);
		Diagnostic[] diagnostics =
		[
			.. run.OutputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
				.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
		];
		Assert.Empty(diagnostics);
		Assert.NotEmpty(Emit(run));
	}

	[Fact]
	public void GeneratedModuleCompilesInAConsumerThatDisallowsUnsafeCode()
	{
		GeneratorRun run = GeneratorRun.Execute(ModuleWithoutLegacyHelpers);
		Compilation consumer = run.OutputCompilation.WithOptions(
			((CSharpCompilationOptions) run.OutputCompilation.Options).WithAllowUnsafe(false));

		Assert.Empty(run.Diagnostics);
		Assert.Empty(consumer.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
	}

	[Fact]
	public void GeneratedModuleWithoutAnAttachedSdkRuntimeIsRefusedBeforeAnyLuaCall()
	{
		ILuaModule module = (ILuaModule) Activator.CreateInstance(LoadModuleType(ModuleWithoutLegacyHelpers))!;

		// No CheatEngine.SDK runtime is attached in this process: the real generated adapter asks for admission, the SDK
		// answers Detached, and the registrar refuses before any Lua call. A module that owns nothing releases nothing.
		CheatEngineOperationException refused = Assert.Throws<CheatEngineOperationException>(module.Register);
		LuaModuleReleaseOutcome outcome = module.Unregister();

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, refused.Failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, refused.Failure.HostEffect);
		Assert.Equal("Lua.RegisterModule", refused.Failure.Operation);
		Assert.Contains("Detached", refused.Failure.Message, StringComparison.Ordinal);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, outcome.Kind);
		Assert.Equal("plugin", outcome.ModuleName);
	}

	internal static Type LoadModuleType(string source)
	{
		GeneratorRun run = GeneratorRun.Execute(source);
		Assert.Empty(run.Diagnostics);
		return System.Reflection.Assembly.Load(Emit(run)).GetType("TestPlugin.PluginLuaModule", true)!;
	}

	private static byte[] Emit(GeneratorRun run)
	{
		using MemoryStream image = new();
		EmitResult result = run.OutputCompilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
		return image.ToArray();
	}

	private static string Describe(MemberInfo member)
	{
		return member switch
		{
			MethodBase method => method.Name + "(" + string.Join(",",
				method.GetParameters().Select(static parameter => parameter.ParameterType.Name)) + ")",
			_ => member.Name
		};
	}

	private static IEnumerable<Type> PublicSignatureTypes(MemberInfo member)
	{
		switch (member)
		{
			case PropertyInfo property:
				yield return property.PropertyType;
				break;
			case MethodBase method:
				if (method is MethodInfo info)
				{
					yield return info.ReturnType;
				}

				foreach (ParameterInfo parameter in method.GetParameters())
				{
					yield return parameter.ParameterType;
				}

				break;
			case FieldInfo field:
				yield return field.FieldType;
				break;
		}
	}

	private static int Count(string value, string fragment)
	{
		int count = 0;
		int start = 0;
		while ((start = value.IndexOf(fragment, start, StringComparison.Ordinal)) >= 0)
		{
			count++;
			start += fragment.Length;
		}

		return count;
	}
}
