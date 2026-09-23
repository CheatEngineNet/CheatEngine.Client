using System.Text;

using CheatEngine.Client.SourceGenerators.Lua.Model;

namespace CheatEngine.Client.SourceGenerators.Lua;

/// <summary>Emits the ownership-aware Client adapter of one valid <c>[CheatEngineLuaModule]</c> declaration.</summary>
/// <remarks>
///     <para>
///         The adapter wraps the SDK-generated <c>RegisterLuaFunctions</c> (called exactly once) and never the legacy
///         SDK 1.0.0 unregistration helper, which writes <c>nil</c> unconditionally (F12, Q16). After a successful
///         registration it pins the value it published under each export; at release it clears a global only while the
///         global still holds that value (primitive identity, <c>lua_rawequal</c>), so a third-party replacement survives.
///     </para>
///     <para>
///         The algorithm is emitted once as private generic methods over a private port interface. The only port that
///         touches the Lua state is the nested SDK port struct: it is the single, frozen ADR-01 exception of generated
///         Client code, bounded by <c>GeneratedLuaSurfaceRatchetTests</c> and removed by the SDK 2.0 registration leases.
///         The same algorithm runs in the EndToEnd tests against a managed Lua-globals double.
///     </para>
/// </remarks>
internal static class ModuleEmitter
{
	/// <summary>The prefix of every generated non-interface member; CECLUA1202 reserves it in module declarations.</summary>
	internal const string ReservedPrefix = "__CheatEngineLua";

	private const string Exception = "global::System.Exception";
	private const string ExceptionList = "global::System.Collections.Generic.List<global::System.Exception>";
	private const string InvalidOperationException = "global::System.InvalidOperationException";
	private const string LuaException = "global::CheatEngine.SDK.Lua.Calls.LuaException";
	private const string LuaError = "global::CheatEngine.SDK.Lua.Calls.LuaError";
	private const string LuaStatus = "global::CheatEngine.SDK.Lua.Calls.LuaStatus";
	private const string LuaRef = "global::CheatEngine.SDK.Lua.References.LuaRef";
	private const string LuaState = "global::CheatEngine.SDK.Lua.State.LuaState";
	private const string ReleaseStatus = "global::CheatEngine.Client.Lua.LuaExportReleaseStatus";
	private const string ExportOutcome = "global::CheatEngine.Client.Lua.LuaExportReleaseOutcome";
	private const string ModuleOutcome = "global::CheatEngine.Client.Lua.LuaModuleReleaseOutcome";

	private const string ModuleNameField = ReservedPrefix + "ModuleName";
	private const string ExportNamesField = ReservedPrefix + "ExportNames";
	private const string OwnedExportsField = ReservedPrefix + "OwnedExports";
	private const string LastReleaseOutcomeField = ReservedPrefix + "LastReleaseOutcome";
	private const string RegisterCore = ReservedPrefix + "RegisterCore";
	private const string UnregisterCore = ReservedPrefix + "UnregisterCore";
	private const string Rollback = ReservedPrefix + "Rollback";
	private const string ReleaseExport = ReservedPrefix + "ReleaseExport";
	private const string AddFailure = ReservedPrefix + "AddFailure";
	private const string Ownership = ReservedPrefix + "Ownership";
	private const string Port = ReservedPrefix + "IGlobalPort";
	private const string SdkPort = ReservedPrefix + "SdkPort";

	private const string PortConstraints = "where TPort : " + Port + "<TToken>";

	public static string Emit(ModuleModel model)
	{
		SourceBuilder source = new();
		source.WriteGeneratedHeader();
		source.OpenNamespace(model.Namespace);
		source.WriteLine(model.TypeDeclaration +
						 " : global::CheatEngine.Client.Lua.IDescribedLuaModule, global::CheatEngine.Client.Lua.IOwnershipAwareLuaModule");
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
		EmitState(source, model);
		EmitPublicMembers(source);
		EmitRegisterCore(source);
		EmitUnregisterCore(source);
		EmitRollback(source);
		EmitReleaseExport(source);
		EmitAddFailure(source);
		EmitPortContract(source);
		EmitSdkPort(source, model);
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
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public global::CheatEngine.Client.Lua.LuaModuleDescriptor Descriptor => s_descriptor;");
		source.WriteLine();
	}

	private static void EmitState(SourceBuilder source, ModuleModel model)
	{
		source.WriteLine("private const string " + ModuleNameField + " = " +
						 CheatEngineLuaGenerator.CSharpLiteral(model.ModuleName) + ";");
		source.WriteLine();
		StringBuilder names = new();
		for (int index = 0; index < model.Exports.Length; index++)
		{
			names.Append(index == 0 ? " " : ", ");
			names.Append(CheatEngineLuaGenerator.CSharpLiteral(model.Exports[index]));
		}

		source.WriteLine("private static readonly string[] " + ExportNamesField + " = new string[] {" + names +
						 " };");
		source.WriteLine();
		source.WriteLine("// One pin per export, in export order, while a registration is owned; null otherwise.");
		source.WriteLine("private object[]? " + OwnedExportsField + ";");
		source.WriteLine();
		source.WriteLine("private " + ModuleOutcome + "? " + LastReleaseOutcomeField + ";");
		source.WriteLine();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public " + ModuleOutcome + "? LastReleaseOutcome =>");
		source.Indent();
		source.WriteLine("global::System.Threading.Volatile.Read(ref " + LastReleaseOutcomeField + ");");
		source.Unindent();
		source.WriteLine();
	}

	private static void EmitPublicMembers(SourceBuilder source)
	{
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public void Register()");
		source.OpenBlock();
		WriteAcquireSdkPort(source);
		source.WriteLine(RegisterCore + "<" + SdkPort + ", " + LuaRef + ">(ref port);");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public void Unregister()");
		source.OpenBlock();
		source.WriteLine("if (" + OwnedExportsField + " is null)");
		source.OpenBlock();
		source.WriteLine("return;");
		source.CloseBlock();
		source.WriteLine();
		WriteAcquireSdkPort(source);
		source.WriteLine(UnregisterCore + "<" + SdkPort + ", " + LuaRef + ">(ref port);");
		source.CloseBlock();
		source.WriteLine();
	}

	private static void WriteAcquireSdkPort(SourceBuilder source)
	{
		source.WriteLine("using global::CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation operation =");
		source.Indent();
		source.WriteLine("global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireOperation();");
		source.Unindent();
		source.WriteLine(SdkPort + " port = new " + SdkPort + "(operation.State);");
	}

	private static void EmitRegisterCore(SourceBuilder source)
	{
		source.WriteLine("private void " + RegisterCore + "<TPort, TToken>(ref TPort port)");
		WriteConstraints(source);
		source.OpenBlock();
		source.WriteLine("object[]? previous = " + OwnedExportsField + ";");
		source.WriteLine("if (previous is not null)");
		source.OpenBlock();
		source.WriteLine("for (int export = 0; export < previous.Length; export++)");
		source.OpenBlock();
		source.WriteLine("if (port.IsCurrent((TToken) previous[export]))");
		source.OpenBlock();
		source.WriteLine("throw new " + InvalidOperationException + "(\"Client Lua module '\" + " + ModuleNameField +
						 " + \"' is already registered in the current Lua state.\");");
		source.CloseBlock();
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine(
			"// The previous registration belongs to an earlier Lua state or attachment: releasing its pins cannot reach");
		source.WriteLine("// the current state, it only marks them released.");
		source.WriteLine(OwnedExportsField + " = null;");
		source.WriteLine("for (int export = 0; export < previous.Length; export++)");
		source.OpenBlock();
		source.WriteLine("_ = port.Release((TToken) previous[export]);");
		source.CloseBlock();
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("int count = " + ExportNamesField + ".Length;");
		source.WriteLine("for (int export = 0; export < count; export++)");
		source.OpenBlock();
		source.WriteLine(Exception + "? probeFailure = port.ProbeVacant(export, out bool vacant);");
		source.WriteLine("if (probeFailure is not null)");
		source.OpenBlock();
		source.WriteLine("throw probeFailure;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("if (!vacant)");
		source.OpenBlock();
		source.WriteLine("throw new " + InvalidOperationException + "(\"Lua global '\" + " + ExportNamesField +
						 "[export] + \"' is already defined and cannot be replaced by Client module '\" + " +
						 ModuleNameField + " + \"'.\");");
		source.CloseBlock();
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("TToken?[] captured = new TToken?[count];");
		source.WriteLine(Exception + "? primary = port.Publish();");
		source.WriteLine("for (int export = 0; primary is null && export < count; export++)");
		source.OpenBlock();
		source.WriteLine("primary = port.Capture(export, out TToken? token);");
		source.WriteLine("captured[export] = token;");
		source.WriteLine("if (primary is null && token is null)");
		source.OpenBlock();
		source.WriteLine("primary = new " + InvalidOperationException + "(\"Lua global '\" + " + ExportNamesField +
						 "[export] + \"' could not be confirmed after Client module '\" + " + ModuleNameField +
						 " + \"' registered it.\");");
		source.CloseBlock();
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("if (primary is not null)");
		source.OpenBlock();
		source.WriteLine("throw " + Rollback + "<TPort, TToken>(ref port, captured, primary);");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("object[] owned = new object[count];");
		source.WriteLine("for (int export = 0; export < count; export++)");
		source.OpenBlock();
		source.WriteLine("owned[export] = captured[export]!;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine(OwnedExportsField + " = owned;");
		source.CloseBlock();
		source.WriteLine();
	}

	private static void EmitUnregisterCore(SourceBuilder source)
	{
		source.WriteLine("private void " + UnregisterCore + "<TPort, TToken>(ref TPort port)");
		WriteConstraints(source);
		source.OpenBlock();
		source.WriteLine("object[]? owned = " + OwnedExportsField + ";");
		source.WriteLine("if (owned is null)");
		source.OpenBlock();
		source.WriteLine("return;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine(
			"// Ownership is consumed before the first Lua call: a release is attempted once and never retried.");
		source.WriteLine(OwnedExportsField + " = null;");
		source.WriteLine("bool stale = false;");
		source.WriteLine("for (int export = 0; export < owned.Length; export++)");
		source.OpenBlock();
		source.WriteLine("stale |= !port.IsCurrent((TToken) owned[export]);");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine(ExportOutcome + "[] exports = new " + ExportOutcome + "[owned.Length];");
		source.WriteLine(ExceptionList + "? failures = null;");
		source.WriteLine("for (int export = 0; export < owned.Length; export++)");
		source.OpenBlock();
		source.WriteLine("TToken token = (TToken) owned[export];");
		source.WriteLine(ReleaseStatus + " status;");
		source.WriteLine("if (stale)");
		source.OpenBlock();
		source.WriteLine("status = " + ReleaseStatus + ".NotAttempted;");
		source.WriteLine(AddFailure + "(ref failures, port.Release(token));");
		source.CloseBlock();
		source.WriteLine("else");
		source.OpenBlock();
		source.WriteLine("status = " + ReleaseExport + "<TPort, TToken>(ref port, export, token, ref failures);");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("exports[export] = new " + ExportOutcome + "(" + ExportNamesField + "[export], status);");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("global::System.Threading.Volatile.Write(ref " + LastReleaseOutcomeField + ",");
		source.Indent();
		source.WriteLine("new " + ModuleOutcome + "(" + ModuleNameField +
						 ", global::System.Collections.Immutable.ImmutableArray.Create(exports)));");
		source.Unindent();
		source.WriteLine("if (failures is null)");
		source.OpenBlock();
		source.WriteLine("return;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("if (failures.Count == 1)");
		source.OpenBlock();
		source.WriteLine("throw failures[0];");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("throw new global::System.AggregateException(\"Client Lua module '\" + " + ModuleNameField +
						 " + \"' could not release every export.\", failures);");
		source.CloseBlock();
		source.WriteLine();
	}

	private static void EmitRollback(SourceBuilder source)
	{
		source.WriteLine("private static " + Exception + " " + Rollback +
						 "<TPort, TToken>(ref TPort port, TToken?[] captured, " + Exception + " primary)");
		WriteConstraints(source);
		source.OpenBlock();
		source.WriteLine(
			"// Captured exports are released ownership-aware. The preflight proved the others were nil inside this admitted");
		source.WriteLine(
			"// operation, so a non-nil value there was published by this registration (a _G metamethod that publishes under");
		source.WriteLine("// the module's names during registration is unsupported).");
		source.WriteLine(ExceptionList + "? secondary = null;");
		source.WriteLine("for (int export = 0; export < captured.Length; export++)");
		source.OpenBlock();
		source.WriteLine("TToken? token = captured[export];");
		source.WriteLine("if (token is not null)");
		source.OpenBlock();
		source.WriteLine("_ = " + ReleaseExport + "<TPort, TToken>(ref port, export, token, ref secondary);");
		source.WriteLine("continue;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine(Exception + "? failure = port.ProbeVacant(export, out bool vacant);");
		source.WriteLine("if (failure is null && !vacant)");
		source.OpenBlock();
		source.WriteLine("failure = port.Clear(export);");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine(AddFailure + "(ref secondary, failure);");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("if (secondary is null)");
		source.OpenBlock();
		source.WriteLine("return primary;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("global::System.Text.StringBuilder message = new global::System.Text.StringBuilder(primary.Message);");
		source.WriteLine("message.Append(\" The rollback also failed:\");");
		source.WriteLine("foreach (" + Exception + " failure in secondary)");
		source.OpenBlock();
		source.WriteLine("message.Append(' ').Append(failure.Message);");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("secondary.Insert(0, primary);");
		source.WriteLine("global::System.AggregateException inner = new global::System.AggregateException(secondary);");
		source.WriteLine("return primary is " + LuaException);
		source.Indent();
		source.WriteLine("? new " + LuaException + "(message.ToString(), inner)");
		source.WriteLine(": new " + InvalidOperationException + "(message.ToString(), inner);");
		source.Unindent();
		source.CloseBlock();
		source.WriteLine();
	}

	private static void EmitReleaseExport(SourceBuilder source)
	{
		source.WriteLine("private static " + ReleaseStatus + " " + ReleaseExport +
						 "<TPort, TToken>(ref TPort port, int export, TToken token,");
		source.Indent();
		source.WriteLine("ref " + ExceptionList + "? failures)");
		source.Unindent();
		WriteConstraints(source);
		source.OpenBlock();
		source.WriteLine(ReleaseStatus + " status;");
		source.WriteLine(Exception + "? failure;");
		source.WriteLine("switch (port.Observe(export, token, out " + Exception + "? readFailure))");
		source.OpenBlock();
		source.WriteLine("case " + Ownership + ".Owned:");
		source.Indent();
		source.WriteLine("failure = port.Clear(export);");
		source.WriteLine("status = failure is null ? " + ReleaseStatus + ".Removed : " + ReleaseStatus + ".Failed;");
		source.WriteLine("break;");
		source.Unindent();
		source.WriteLine("case " + Ownership + ".Replaced:");
		source.Indent();
		source.WriteLine("failure = null;");
		source.WriteLine("status = " + ReleaseStatus + ".Replaced;");
		source.WriteLine("break;");
		source.Unindent();
		source.WriteLine("case " + Ownership + ".Absent:");
		source.Indent();
		source.WriteLine("failure = null;");
		source.WriteLine("status = " + ReleaseStatus + ".Absent;");
		source.WriteLine("break;");
		source.Unindent();
		source.WriteLine("case " + Ownership + ".OwnershipUnresolvable:");
		source.Indent();
		source.WriteLine("failure = new " + InvalidOperationException + "(\"Client Lua module '\" + " + ModuleNameField +
						 " + \"' could not resolve the ownership reference of Lua global '\" + " + ExportNamesField +
						 "[export] + \"'; the global was left untouched.\");");
		source.WriteLine("status = " + ReleaseStatus + ".Failed;");
		source.WriteLine("break;");
		source.Unindent();
		source.WriteLine("default:");
		source.Indent();
		source.WriteLine("failure = readFailure ?? new " + InvalidOperationException + "(\"Client Lua module '\" + " +
						 ModuleNameField + " + \"' could not read Lua global '\" + " + ExportNamesField +
						 "[export] + \"'.\");");
		source.WriteLine("status = " + ReleaseStatus + ".Failed;");
		source.WriteLine("break;");
		source.Unindent();
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine(AddFailure + "(ref failures, failure);");
		source.WriteLine("// A pin that cannot be released is a secondary failure; it does not change what happened to the global.");
		source.WriteLine(AddFailure + "(ref failures, port.Release(token));");
		source.WriteLine("return status;");
		source.CloseBlock();
		source.WriteLine();
	}

	private static void EmitAddFailure(SourceBuilder source)
	{
		source.WriteLine("private static void " + AddFailure + "(ref " + ExceptionList + "? failures, " + Exception +
						 "? failure)");
		source.OpenBlock();
		source.WriteLine("if (failure is not null)");
		source.OpenBlock();
		source.WriteLine("(failures ??= new " + ExceptionList + "()).Add(failure);");
		source.CloseBlock();
		source.CloseBlock();
		source.WriteLine();
	}

	private static void EmitPortContract(SourceBuilder source)
	{
		source.WriteLine("private enum " + Ownership);
		source.OpenBlock();
		source.WriteLine("ReadFailed,");
		source.WriteLine("Absent,");
		source.WriteLine("Owned,");
		source.WriteLine("Replaced,");
		source.WriteLine("OwnershipUnresolvable");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("// Every observation the ownership algorithm needs; each member restores the Lua stack it used.");
		source.WriteLine("private interface " + Port + "<TToken>");
		source.Indent();
		source.WriteLine("where TToken : class");
		source.Unindent();
		source.OpenBlock();
		source.WriteLine(Exception + "? ProbeVacant(int export, out bool vacant);");
		source.WriteLine();
		source.WriteLine(Exception + "? Publish();");
		source.WriteLine();
		source.WriteLine(Exception + "? Capture(int export, out TToken? token);");
		source.WriteLine();
		source.WriteLine("bool IsCurrent(TToken token);");
		source.WriteLine();
		source.WriteLine(Ownership + " Observe(int export, TToken token, out " + Exception + "? failure);");
		source.WriteLine();
		source.WriteLine(Exception + "? Clear(int export);");
		source.WriteLine();
		source.WriteLine(Exception + "? Release(TToken token);");
		source.CloseBlock();
		source.WriteLine();
	}

	private static void EmitSdkPort(SourceBuilder source, ModuleModel model)
	{
		source.WriteLine(
			"// ADR-01 registered exception (Q16 on CheatEngine.SDK 1.0.0): the only raw Lua access in generated Client code. Removal: SDK 2.0 registration leases (docs/migration/sdk-2.0.md).");
		source.WriteLine("private readonly struct " + SdkPort + " : " + Port + "<" + LuaRef + ">");
		source.OpenBlock();
		source.WriteLine("private readonly " + LuaState + " _state;");
		source.WriteLine();
		source.WriteLine("public " + SdkPort + "(" + LuaState + " state)");
		source.OpenBlock();
		source.WriteLine("_state = state;");
		source.CloseBlock();
		source.WriteLine();

		source.WriteLine("public " + Exception + "? ProbeVacant(int export, out bool vacant)");
		source.OpenBlock();
		source.WriteLine("vacant = false;");
		WriteStackGuardOpen(source);
		WriteGetGlobal(source);
		source.WriteLine("vacant = _state.IsNil(-1);");
		source.WriteLine("return null;");
		WriteStackGuardClose(source, false);
		source.CloseBlock();
		source.WriteLine();

		source.WriteLine("public " + Exception + "? Publish()");
		source.OpenBlock();
		WriteStackGuardOpen(source);
		source.WriteLine(LuaStatus + " status = " + model.BindingsType + ".RegisterLuaFunctions(_state);");
		WriteStatusResult(source);
		WriteStackGuardClose(source, false);
		source.CloseBlock();
		source.WriteLine();

		source.WriteLine("public " + Exception + "? Capture(int export, out " + LuaRef + "? token)");
		source.OpenBlock();
		source.WriteLine("token = null;");
		WriteStackGuardOpen(source);
		WriteGetGlobal(source);
		source.WriteLine("if (_state.IsNil(-1))");
		source.OpenBlock();
		source.WriteLine("return null;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("token = _state.CreateRef();");
		source.WriteLine("return null;");
		WriteStackGuardClose(source, true);
		source.CloseBlock();
		source.WriteLine();

		source.WriteLine("public bool IsCurrent(" + LuaRef + " token)");
		source.OpenBlock();
		source.WriteLine("return token.IsCurrent;");
		source.CloseBlock();
		source.WriteLine();

		source.WriteLine("public " + Ownership + " Observe(int export, " + LuaRef + " token, out " + Exception +
						 "? failure)");
		source.OpenBlock();
		source.WriteLine("failure = null;");
		WriteStackGuardOpen(source);
		source.WriteLine(LuaStatus + " status = _state.TryGetGlobal(ExportName(export));");
		source.WriteLine("if (!status.IsOk)");
		source.OpenBlock();
		source.WriteLine("failure = new " + LuaException + "(" + LuaError + ".FromStack(_state, status));");
		source.WriteLine("return " + Ownership + ".ReadFailed;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("if (_state.IsNil(-1))");
		source.OpenBlock();
		source.WriteLine("return " + Ownership + ".Absent;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("if (!_state.TryPushRef(token))");
		source.OpenBlock();
		source.WriteLine("return " + Ownership + ".OwnershipUnresolvable;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("return _state.RawEquals(-2, -1) ? " + Ownership + ".Owned : " + Ownership + ".Replaced;");
		WriteStackGuardClose(source, false);
		source.CloseBlock();
		source.WriteLine();

		source.WriteLine("public " + Exception + "? Clear(int export)");
		source.OpenBlock();
		WriteStackGuardOpen(source);
		source.WriteLine("_state.PushNil();");
		source.WriteLine(LuaStatus + " status = _state.TrySetGlobal(ExportName(export));");
		WriteStatusResult(source);
		WriteStackGuardClose(source, false);
		source.CloseBlock();
		source.WriteLine();

		source.WriteLine("public " + Exception + "? Release(" + LuaRef + " token)");
		source.OpenBlock();
		WriteStackGuardOpen(source);
		source.WriteLine("token.Release(_state);");
		source.WriteLine("return null;");
		WriteStackGuardClose(source, true);
		source.CloseBlock();
		source.WriteLine();

		source.WriteLine("private static global::System.ReadOnlySpan<byte> ExportName(int export)");
		source.OpenBlock();
		source.WriteLine("switch (export)");
		source.OpenBlock();
		for (int index = 0; index < model.Exports.Length; index++)
		{
			source.WriteLine("case " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":");
			source.Indent();
			source.WriteLine("return " + CheatEngineLuaGenerator.CSharpLiteral(model.Exports[index]) + "u8;");
			source.Unindent();
		}

		source.WriteLine("default:");
		source.Indent();
		source.WriteLine("throw new global::System.ArgumentOutOfRangeException(nameof(export));");
		source.Unindent();
		source.CloseBlock();
		source.CloseBlock();
		source.CloseBlock();
	}

	private static void WriteConstraints(SourceBuilder source)
	{
		source.Indent();
		source.WriteLine(PortConstraints);
		source.WriteLine("where TToken : class");
		source.Unindent();
	}

	private static void WriteStackGuardOpen(SourceBuilder source)
	{
		source.WriteLine("int top = _state.Top;");
		source.WriteLine("try");
		source.OpenBlock();
	}

	private static void WriteGetGlobal(SourceBuilder source)
	{
		source.WriteLine(LuaStatus + " status = _state.TryGetGlobal(ExportName(export));");
		source.WriteLine("if (!status.IsOk)");
		source.OpenBlock();
		source.WriteLine("return new " + LuaException + "(" + LuaError + ".FromStack(_state, status));");
		source.CloseBlock();
		source.WriteLine();
	}

	private static void WriteStatusResult(SourceBuilder source)
	{
		source.WriteLine("return status.IsOk ? null : new " + LuaException + "(" + LuaError + ".FromStack(_state, status));");
	}

	private static void WriteStackGuardClose(SourceBuilder source, bool catchLuaException)
	{
		source.CloseBlock();
		if (catchLuaException)
		{
			source.WriteLine("catch (" + LuaException + " exception)");
			source.OpenBlock();
			source.WriteLine("return exception;");
			source.CloseBlock();
		}

		source.WriteLine("finally");
		source.OpenBlock();
		source.WriteLine("_state.SetTop(top);");
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
