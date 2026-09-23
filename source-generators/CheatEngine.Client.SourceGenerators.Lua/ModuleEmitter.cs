using System.Text;

using CheatEngine.Client.SourceGenerators.Lua.Model;

namespace CheatEngine.Client.SourceGenerators.Lua;

/// <summary>Emits the Client adapter of one valid <c>[CheatEngineLuaModule]</c> declaration.</summary>
internal static class ModuleEmitter
{
	public static string Emit(ModuleModel model)
	{
		SourceBuilder source = new();
		source.WriteGeneratedHeader();
		source.OpenNamespace(model.Namespace);
		source.WriteLine(model.TypeDeclaration + " : global::CheatEngine.Client.Lua.IDescribedLuaModule");
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

		source.WriteLine("private static readonly global::CheatEngine.Client.Lua.LuaModuleDescriptor s_descriptor =");
		source.Indent();
		source.WriteLine("new global::CheatEngine.Client.Lua.LuaModuleDescriptor(");
		source.Indent();
		source.WriteLine(CheatEngineLuaGenerator.CSharpLiteral(model.ModuleName) + ",");
		source.WriteLine(EmitExports(model.Exports) + ");");
		source.Unindent();
		source.Unindent();
		source.WriteLine();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public global::CheatEngine.Client.Lua.LuaModuleDescriptor Descriptor => s_descriptor;");
		source.WriteLine();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public void Register()");
		source.OpenBlock();
		source.WriteLine("using global::CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation operation =");
		source.Indent();
		source.WriteLine("global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireOperation();");
		source.Unindent();
		source.WriteLine("EnsureExportsAreVacant(operation.State);");
		source.WriteLine("global::CheatEngine.SDK.Lua.Calls.LuaStatus status = " + model.BindingsType +
						 ".RegisterLuaFunctions(operation.State);");
		source.WriteLine("if (status.IsOk)");
		source.OpenBlock();
		source.WriteLine("return;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("try");
		source.OpenBlock();
		source.WriteLine("status.ThrowIfFailed(operation.State);");
		source.CloseBlock();
		source.WriteLine("finally");
		source.OpenBlock();
		source.WriteLine("_ = " + model.BindingsType + ".UnregisterLuaFunctions(operation.State);");
		source.CloseBlock();
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public void Unregister()");
		source.OpenBlock();
		source.WriteLine("using global::CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation operation =");
		source.Indent();
		source.WriteLine("global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireOperation();");
		source.Unindent();
		source.WriteLine("global::CheatEngine.SDK.Lua.Calls.LuaStatus status = " + model.BindingsType +
						 ".UnregisterLuaFunctions(operation.State);");
		source.WriteLine("if (!status.IsOk)");
		source.OpenBlock();
		source.WriteLine("status.ThrowIfFailed(operation.State);");
		source.CloseBlock();
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine(
			"private static void EnsureExportsAreVacant(global::CheatEngine.SDK.Lua.State.LuaState state)");
		source.OpenBlock();
		source.WriteLine("int top = state.Top;");
		source.WriteLine("try");
		source.OpenBlock();
		foreach (string export in model.Exports)
		{
			source.OpenBlock();
			source.WriteLine("global::CheatEngine.SDK.Lua.Calls.LuaStatus status = state.TryGetGlobal(" +
							 CheatEngineLuaGenerator.CSharpLiteral(export) + "u8);");
			source.WriteLine("if (!status.IsOk)");
			source.OpenBlock();
			source.WriteLine("status.ThrowIfFailed(state);");
			source.CloseBlock();
			source.WriteLine("if (!state.IsNil(-1))");
			source.OpenBlock();
			source.WriteLine("throw new global::System.InvalidOperationException(" +
							 CheatEngineLuaGenerator.CSharpLiteral("Lua global '" + export +
															   "' is already defined and cannot be replaced by Client module '" +
															   model.ModuleName + "'.") + ");");
			source.CloseBlock();
			source.WriteLine("state.Pop(1);");
			source.CloseBlock();
		}

		source.CloseBlock();
		source.WriteLine("finally");
		source.OpenBlock();
		source.WriteLine("state.SetTop(top);");
		source.CloseBlock();
		source.CloseBlock();
		source.CloseBlock();

		return source.ToString();
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
