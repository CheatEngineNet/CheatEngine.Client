using CheatEngine.Client.SourceGenerators.Lua.Model;

namespace CheatEngine.Client.SourceGenerators.Lua;

/// <summary>
///     Emits the handle-free readonly value operation, its factory, and its inferable <c>ILuaClient</c> extension methods
///     for one <c>[CheatEngineLuaOperation]</c>.
/// </summary>
internal static class OperationEmitter
{
	private const string LuaClient = "global::CheatEngine.Client.Lua.ILuaClient";

	private const string CancellationTokenParameter =
		"global::System.Threading.CancellationToken cancellationToken = default";

	public static string Emit(OperationModel model)
	{
		SourceBuilder source = new();
		source.WriteGeneratedHeader();
		source.OpenNamespace(model.Namespace);
		source.WriteLine(model.ContainingTypeDeclaration);
		source.OpenBlock();
		source.WriteLine("/// <summary>Creates a handle-free Client operation for <c>" + model.MethodName +
						 "</c>.</summary>");
		source.WriteLine("public static " + model.OperationTypeName + " Create" + model.OperationTypeName + "(" +
						 EmitParameterList(model.Parameters) + ")");
		source.OpenBlock();
		source.WriteLine("return new " + model.OperationTypeName + "(" + EmitArgumentList(model.Parameters, false) +
						 ");");
		source.CloseBlock();
		source.WriteLine();
		EmitClientExtensions(source, model);
		source.WriteLine("/// <summary>Generated readonly value operation for <c>" + model.MethodName +
						 "</c>.</summary>");
		source.WriteLine(model.OperationVisibility + " readonly record struct " + model.OperationTypeName + "(" +
						 EmitRecordParameterList(model.Parameters) +
						 ") : global::CheatEngine.Client.Lua.ILuaOperation<" + model.ResultType + ">");
		source.OpenBlock();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public bool TryExecute(global::CheatEngine.Client.Lua.ILuaExecutionContext context, out " +
						 model.ResultType +
						 " result, out global::CheatEngine.Client.Results.CheatEngineFailure failure)");
		source.OpenBlock();
		source.WriteLine("global::System.ArgumentNullException.ThrowIfNull(context);");
		source.WriteLine("context.ThrowIfExpired();");
		source.WriteLine("try");
		source.OpenBlock();
		if (model.HasOutResult)
		{
			source.WriteLine(model.SourceResultType + " source;");
			source.WriteLine("if (!" + model.BindingsType + "." + model.MethodName + "(" +
							 EmitOutArgumentList(model.Parameters) + "))");
			source.OpenBlock();
			source.WriteLine("result = default!;");
			source.WriteLine("failure = new global::CheatEngine.Client.Results.CheatEngineFailure(");
			source.Indent();
			source.WriteLine("global::CheatEngine.Client.Results.CheatEngineFailureKind.LuaError,");
			source.WriteLine(CheatEngineLuaGenerator.CSharpLiteral(model.FailureOperation) + ",");
			source.WriteLine(CheatEngineLuaGenerator.CSharpLiteral(
				"The SDK Lua Try binding returned false without exposing an unsafe Lua handle.") + ");");
			source.Unindent();
			source.WriteLine("return false;");
			source.CloseBlock();
		}
		else
		{
			source.WriteLine(model.SourceResultType + " source = " + model.BindingsType + "." + model.MethodName + "(" +
							 EmitArgumentList(model.Parameters, true) + ");");
		}

		source.WriteLine(model.MapperType is null
			? "result = source;"
			: "result = " + model.MapperType + ".Map(source);");
		source.WriteLine("failure = default;");
		source.WriteLine("return true;");
		source.CloseBlock();
		source.WriteLine("catch (global::CheatEngine.SDK.Lua.Calls.LuaException exception)");
		source.OpenBlock();
		source.WriteLine("result = default!;");
		source.WriteLine("failure = new global::CheatEngine.Client.Results.CheatEngineFailure(");
		source.Indent();
		source.WriteLine("global::CheatEngine.Client.Results.CheatEngineFailureKind.LuaError,");
		source.WriteLine(CheatEngineLuaGenerator.CSharpLiteral(model.FailureOperation) + ",");
		source.WriteLine("exception.Message,");
		source.WriteLine("exception);");
		source.Unindent();
		source.WriteLine("return false;");
		source.CloseBlock();
		source.WriteLine("catch (global::System.Exception exception)");
		source.OpenBlock();
		source.WriteLine("result = default!;");
		source.WriteLine("failure = new global::CheatEngine.Client.Results.CheatEngineFailure(");
		source.Indent();
		source.WriteLine("global::CheatEngine.Client.Results.CheatEngineFailureKind.BindingError,");
		source.WriteLine(CheatEngineLuaGenerator.CSharpLiteral(model.FailureOperation) + ",");
		source.WriteLine("exception.Message,");
		source.WriteLine("exception);");
		source.Unindent();
		source.WriteLine("return false;");
		source.CloseBlock();
		source.CloseBlock();
		source.CloseBlock();
		source.CloseBlock();

		return source.ToString();
	}

	// The ILuaClient pair is generic over the operation and its result; these overloads infer both from the operation
	// type, so a call reads client.Execute(operation), passes the operation by reference and never boxes it.
	private static void EmitClientExtensions(SourceBuilder source, OperationModel model)
	{
		string typeArguments = "<" + model.OperationTypeName + ", " + model.ResultType + ">";
		source.WriteLine("/// <summary>Executes the generated <c>" + model.MethodName +
						 "</c> operation on Cheat Engine's main thread.</summary>");
		source.WriteLine("/// <param name=\"client\">The Lua client of the current activation.</param>");
		source.WriteLine("/// <param name=\"operation\">The operation to execute.</param>");
		source.WriteLine("/// <param name=\"cancellationToken\">Cancellation observed before dispatch admission.</param>");
		source.WriteLine("/// <returns>The copied result.</returns>");
		source.WriteLine("public static " + model.ResultType + " Execute(this " + LuaClient + " client, in " +
						 model.OperationTypeName + " operation,");
		source.Indent();
		source.WriteLine(CancellationTokenParameter + ")");
		source.Unindent();
		source.OpenBlock();
		source.WriteLine("global::System.ArgumentNullException.ThrowIfNull(client);");
		source.WriteLine("return client.Execute" + typeArguments + "(in operation, cancellationToken);");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("/// <summary>Tries to execute the generated <c>" + model.MethodName +
						 "</c> operation on Cheat Engine's main thread.</summary>");
		source.WriteLine("/// <param name=\"client\">The Lua client of the current activation.</param>");
		source.WriteLine("/// <param name=\"operation\">The operation to execute.</param>");
		source.WriteLine("/// <param name=\"result\">The copied result on success.</param>");
		source.WriteLine("/// <param name=\"failure\">The mapped Client failure on failure.</param>");
		source.WriteLine("/// <param name=\"cancellationToken\">Cancellation observed before dispatch admission.</param>");
		source.WriteLine("/// <returns><see langword=\"true\" /> when the operation completed successfully.</returns>");
		source.WriteLine("public static bool TryExecute(this " + LuaClient + " client, in " + model.OperationTypeName +
						 " operation,");
		source.Indent();
		source.WriteLine("[global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out " + model.ResultType +
						 " result, out global::CheatEngine.Client.Results.CheatEngineFailure failure,");
		source.WriteLine(CancellationTokenParameter + ")");
		source.Unindent();
		source.OpenBlock();
		source.WriteLine("global::System.ArgumentNullException.ThrowIfNull(client);");
		source.WriteLine("return client.TryExecute" + typeArguments +
						 "(in operation, out result, out failure, cancellationToken);");
		source.CloseBlock();
		source.WriteLine();
	}

	private static string EmitParameterList(EquatableArray<OperationParameter> parameters)
	{
		return string.Join(", ", parameters.Select(static parameter => parameter.Type + " " + parameter.Name));
	}

	private static string EmitRecordParameterList(EquatableArray<OperationParameter> parameters)
	{
		return string.Join(", ", parameters.Select(static parameter => parameter.Type + " " + parameter.FieldName));
	}

	private static string EmitArgumentList(EquatableArray<OperationParameter> parameters, bool fields)
	{
		return string.Join(", ", parameters.Select(parameter => fields ? parameter.FieldName : parameter.Name));
	}

	private static string EmitOutArgumentList(EquatableArray<OperationParameter> parameters)
	{
		string arguments = EmitArgumentList(parameters, true);
		return arguments.Length == 0 ? "out source" : arguments + ", out source";
	}
}
