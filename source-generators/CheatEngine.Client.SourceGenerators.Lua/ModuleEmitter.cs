using System.Text;

using CheatEngine.Client.SourceGenerators.Lua.Model;

namespace CheatEngine.Client.SourceGenerators.Lua;

/// <summary>Emits the generated partial part of one valid <c>[CheatEngineLuaModule]</c> declaration.</summary>
/// <remarks>
///     <para>
///         The module implements <c>ILuaModule</c> with three members and one field. <c>Register</c> hands the
///         SDK-generated <c>TryRegisterLuaFunctions</c> of the bindings type, with the <c>RejectExisting</c> collision
///         policy, to the assembly's <see cref="RegistrarEmitter" /> registrar, which admits the Lua work, publishes, and
///         keeps the CheatEngine.SDK registration lease in the field. <c>Unregister</c> asks the registrar to release that
///         lease and returns what CheatEngine.SDK observed. The module itself makes no CheatEngine.SDK call: the only SDK
///         member it names is the bindings type's registration method, which the registrar invokes.
///     </para>
///     <para>
///         The legacy SDK <c>RegisterLuaFunctions</c>/<c>UnregisterLuaFunctions</c> pair, which writes unconditionally, is
///         never called (F12, Q16).
///     </para>
/// </remarks>
internal static class ModuleEmitter
{
	/// <summary>The field that holds the registration lease; CECLUA1202 reserves its name in module declarations.</summary>
	internal const string RegistrationField = "_luaRegistration";

	private const string Registrar = "global::" + RegistrarEmitter.Namespace + "." + RegistrarEmitter.RegistrarType;

	public static string Emit(ModuleModel model)
	{
		SourceBuilder source = new();
		source.WriteGeneratedHeader();
		source.OpenNamespace(model.Namespace);
		source.WriteLine(model.TypeDeclaration + " : global::CheatEngine.Client.Lua.ILuaModule");
		source.OpenBlock();
		if (model.EmitsPublicParameterlessConstructor)
		{
			source.WriteLine(
				"/// <summary>Initializes a Lua module instance for activation-scoped dependency injection.</summary>");
			source.WriteLine("public " + model.ModuleTypeName + "()");
			source.OpenBlock();
			source.CloseBlock();
			source.WriteLine();
		}

		EmitDescriptor(source, model);
		EmitMembers(source, model);
		source.CloseBlock();

		return source.ToString();
	}

	private static void EmitDescriptor(SourceBuilder source, ModuleModel model)
	{
		source.WriteLine("private static readonly global::CheatEngine.Client.Lua.LuaModuleDescriptor s_descriptor =");
		source.Indent();
		source.WriteLine("new global::CheatEngine.Client.Lua.LuaModuleDescriptor(");
		source.Indent();
		source.WriteLine(CheatEngineLuaGenerator.CSharpLiteral(model.ModuleName) + ",");
		source.WriteLine(EmitExports(model.Exports) + ");");
		source.Unindent();
		source.Unindent();
		source.WriteLine();
		source.WriteLine(
			"// The CheatEngine.SDK registration lease this module owns while it is registered; null otherwise. Main thread only.");
		source.WriteLine("private object? " + RegistrationField + ";");
		source.WriteLine();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public global::CheatEngine.Client.Lua.LuaModuleDescriptor Descriptor => s_descriptor;");
		source.WriteLine();
	}

	private static void EmitMembers(SourceBuilder source, ModuleModel model)
	{
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public void Register()");
		source.OpenBlock();
		source.WriteLine(Registrar + ".Register(s_descriptor, ref " + RegistrationField + ",");
		source.Indent();
		source.WriteLine("static state => " + model.BindingsType + ".TryRegisterLuaFunctions(state,");
		source.Indent();
		source.WriteLine("global::CheatEngine.SDK.Lua.Registration.LuaRegistrationCollisionPolicy.RejectExisting));");
		source.Unindent();
		source.Unindent();
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public global::CheatEngine.Client.Lua.LuaModuleReleaseOutcome Unregister()");
		source.OpenBlock();
		source.WriteLine("return " + Registrar + ".Unregister(s_descriptor, ref " + RegistrationField + ");");
		source.CloseBlock();
	}

	private static string EmitExports(EquatableArray<string> exports)
	{
		if (exports.IsEmpty)
		{
			return
				"global::System.Collections.Immutable.ImmutableArray<global::CheatEngine.Client.Lua.LuaExportDescriptor>.Empty";
		}

		StringBuilder source = new("global::System.Collections.Immutable.ImmutableArray.Create(");
		for (int index = 0; index < exports.Length; index++)
		{
			if (index != 0)
			{
				source.Append(", ");
			}

			source.Append("new global::CheatEngine.Client.Lua.LuaExportDescriptor(");
			source.Append(CheatEngineLuaGenerator.CSharpLiteral(exports[index]));
			source.Append(')');
		}

		source.Append(')');
		return source.ToString();
	}
}
