using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>Renders C# values as Lua 5.3 source literals.</summary>
internal static class LuaLiteral
{
	/// <summary>A double-quoted Lua string: printable ASCII as is, everything else as <c>\ddd</c> UTF-8 byte escapes.</summary>
	internal static string String(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		StringBuilder literal = new("\"");
		foreach (byte current in Encoding.UTF8.GetBytes(value))
		{
			switch (current)
			{
				case (byte) '"':
					literal.Append("\\\"");
					break;
				case (byte) '\\':
					literal.Append("\\\\");
					break;
				case >= 0x20 and < 0x7F:
					literal.Append((char) current);
					break;
				default:
					literal.Append(CultureInfo.InvariantCulture, $"\\{current:D3}");
					break;
			}
		}

		return literal.Append('"').ToString();
	}

	/// <summary>A Lua integer.</summary>
	internal static string Integer(long value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}
}

/// <summary>One call of a harness Lua function, recorded under <paramref name="Step" />.</summary>
/// <param name="Step">The transcript step name.</param>
/// <param name="Function">The function name after <c>cheatengine_client_qualification_</c>.</param>
/// <param name="Arguments">Lua source expressions, built with <see cref="LuaLiteral" />.</param>
internal sealed record LuaHarnessCall(string Step, string Function, IReadOnlyList<string> Arguments);

/// <summary>What the S0 spike's driver does.</summary>
/// <param name="Session">The session id, for the header comment.</param>
/// <param name="TranscriptPath">Where the driver writes its transcript.</param>
/// <param name="TargetProcessId">The disposable target to open.</param>
/// <param name="PluginPath">The plugin assembly to <c>loadPlugin</c>.</param>
/// <param name="PluginDisplayName">The name Cheat Engine shows in Settings &gt; Plugins.</param>
/// <param name="Calls">The harness calls, in order.</param>
/// <param name="SettingsToggleProven">
///     Whether the S0 spike proved that the plugin can be disabled and enabled through <c>getSettingsForm()</c>. Until
///     it did, the toggle steps are recorded as <c>notexecuted</c> with the operator prompt, never attempted.
/// </param>
internal sealed record LuaDriverPlan(
	string Session,
	string TranscriptPath,
	int TargetProcessId,
	string PluginPath,
	string PluginDisplayName,
	IReadOnlyList<LuaHarnessCall> Calls,
	bool SettingsToggleProven);

/// <summary>How the driver runs one step.</summary>
internal enum LuaDriverStepKind
{
	/// <summary>The step runs once; whatever it returns (even <c>nil</c>) is recorded <c>ok</c>.</summary>
	Once,

	/// <summary>The step runs once per tick until it returns a value, at most <see cref="LuaDriverStep.Attempts" /> times.</summary>
	Poll,

	/// <summary>The driver never attempts the step: it records <c>notexecuted</c> with the operator prompt.</summary>
	NotExecuted
}

/// <summary>
///     One step of a driver: its transcript name, how it runs and its Lua body (the body of a function whose return
///     value the transcript records), or, for <see cref="LuaDriverStepKind.NotExecuted" />, the operator prompt.
/// </summary>
/// <param name="Name">The transcript step name, unique in the driver.</param>
/// <param name="Kind">How the driver runs it.</param>
/// <param name="Body">The Lua function body, lines without indentation; the operator prompt for a not-executed step.</param>
/// <param name="Attempts">How many ticks a poll step may take.</param>
internal sealed record LuaDriverStep(string Name, LuaDriverStepKind Kind, string Body, int Attempts = 0);

/// <summary>The reviewed building blocks of every driver, so a session plan only composes them.</summary>
[SupportedOSPlatform("windows")]
internal static class LuaDriverSteps
{
	/// <summary>Waits for Cheat Engine's main form.</summary>
	internal static LuaDriverStep MainForm()
	{
		return new LuaDriverStep("main-form", LuaDriverStepKind.Poll, """
			if getMainForm() == nil then return nil end
			return "ready"
			""", LuaDriverScript.MainFormAttempts);
	}

	/// <summary>Selects a process through Cheat Engine itself (<c>openProcess</c>), then waits until it is opened.</summary>
	internal static IEnumerable<LuaDriverStep> OpenProcess(string step, int processId)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
		string pid = LuaLiteral.Integer(processId);
		yield return new LuaDriverStep(step, LuaDriverStepKind.Once, $"return openProcess({pid})");
		yield return new LuaDriverStep("opened-" + TrimOpen(step), LuaDriverStepKind.Poll, $"""
			if getOpenedProcessID() ~= {pid} then return nil end
			return getOpenedProcessID()
			""", LuaDriverScript.ShortAttempts);
	}

	/// <summary>Loads a plugin assembly with <c>loadPlugin</c>.</summary>
	internal static LuaDriverStep LoadPlugin(string step, string pluginPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
		return new LuaDriverStep(step, LuaDriverStepKind.Once, $"return loadPlugin({LuaLiteral.String(pluginPath)})");
	}

	/// <summary>Waits until a Lua global is a function: the plugin that exports it is enabled.</summary>
	internal static LuaDriverStep GlobalReady(string step, string global)
	{
		return new LuaDriverStep(step, LuaDriverStepKind.Poll, $"""
			if type(_G[{LuaLiteral.String(global)}]) ~= "function" then return nil end
			return "ready"
			""", LuaDriverScript.ShortAttempts);
	}

	/// <summary>Calls one harness function once.</summary>
	internal static LuaDriverStep Call(string step, string function, params string[] arguments)
	{
		return new LuaDriverStep(step, LuaDriverStepKind.Once, CallBody(function, arguments));
	}

	/// <summary>Calls one harness function each tick until its observation is no longer <c>"pending":true</c>.</summary>
	internal static LuaDriverStep Poll(string step, string function, int attempts, params string[] arguments)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempts);
		string pending = LuaLiteral.String("\"pending\":true");
		return new LuaDriverStep(step, LuaDriverStepKind.Poll, CallBody(function, arguments, "local value = ") +
			"\n" + $"if string.find(value, {pending}, 1, true) ~= nil then return nil end" + "\nreturn value",
			attempts);
	}

	/// <summary>Runs a reviewed Lua body once (driver setup, a check of Lua state, a call of another plugin's global).</summary>
	internal static LuaDriverStep Lua(string step, string body)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(body);
		return new LuaDriverStep(step, LuaDriverStepKind.Once, body);
	}

	/// <summary>Calls a global of another plugin once and returns its value, or raises when it is not a function.</summary>
	internal static LuaDriverStep CallGlobal(string step, string global)
	{
		string name = LuaLiteral.String(global);
		return new LuaDriverStep(step, LuaDriverStepKind.Once, $"""
			local callee = _G[{name}]
			if type(callee) ~= "function" then error("no function " .. {name}) end
			return callee()
			""");
	}

	/// <summary>Records a step the operator must perform (a plugin toggle in Settings &gt; Plugins) as not executed.</summary>
	internal static LuaDriverStep Operator(string step, string prompt)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
		return new LuaDriverStep(step, LuaDriverStepKind.NotExecuted, prompt);
	}

	/// <summary>The operator toggle of a plugin, recorded as not executed until the S0 spike proves a toggle.</summary>
	internal static LuaDriverStep Toggle(string step, bool enable, string pluginDisplayName)
	{
		return Operator(step,
			$"Operator: in Edit > Settings > Plugins, {(enable ? "tick" : "untick")} '{pluginDisplayName}' and press OK.");
	}

	/// <summary>Inspects the settings form read-only: the check list boxes it holds.</summary>
	internal static LuaDriverStep SettingsProbe()
	{
		return new LuaDriverStep("settings-probe", LuaDriverStepKind.Once, """
			local form = getSettingsForm()
			if form == nil then return "no settings form" end
			local found = {}
			for index = 0, form.ComponentCount - 1 do
			  local component = form.Component[index]
			  if string.find(component.ClassName, "CheckListBox", 1, true) ~= nil then
			    found[#found + 1] = component.Name .. ":" .. component.ClassName .. ":" .. tostring(component.Items.Count)
			  end
			end
			return table.concat(found, ";")
			""");
	}

	/// <summary>Clears the address list, so no save prompt can block <c>closeCE()</c>.</summary>
	internal static LuaDriverStep ClearAddressList()
	{
		return new LuaDriverStep("clear-address-list", LuaDriverStepKind.Once, """
			getAddressList().clear()
			return getAddressList().Count
			""");
	}

	private static string CallBody(string function, string[] arguments, string resultPrefix = "return ")
	{
		ArgumentNullException.ThrowIfNull(arguments);
		string global = LuaLiteral.String(LuaDriverScript.HarnessFunctionPrefix + function);
		return $"""
			local harness = _G[{global}]
			if type(harness) ~= "function" then error("the harness does not define " .. {global}) end
			{resultPrefix}harness({string.Join(", ", arguments)})
			""";
	}

	private static string TrimOpen(string step)
	{
		return step.StartsWith("open-", StringComparison.Ordinal) ? step["open-".Length..] : step;
	}
}

/// <summary>
///     Generates the autorun driver of one session: a <c>createTimer</c> state machine that runs on Cheat Engine's main
///     thread, one step per tick, each step under <c>pcall</c>. A session composes its steps from
///     <see cref="LuaDriverSteps" />: it waits for the main form, opens the targets through Cheat Engine, loads the
///     plugins, calls them, records the plugin toggles as <c>notexecuted</c> with an operator prompt until the spike
///     proves them, clears the address list so no save prompt can block, writes <c>DONE</c> and calls <c>closeCE()</c>.
///     Every step appends one transcript line <c>R&lt;TAB&gt;step&lt;TAB&gt;ok|error|notexecuted&lt;TAB&gt;%q</c> and
///     flushes it, so a crash keeps what ran.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class LuaDriverScript
{
	/// <summary>The prefix of every harness Lua function.</summary>
	internal const string HarnessFunctionPrefix = "cheatengine_client_qualification_";

	/// <summary>The timer interval, in milliseconds.</summary>
	internal const int IntervalMilliseconds = 250;

	/// <summary>Ticks to wait for the main form (60 seconds).</summary>
	internal const int MainFormAttempts = 240;

	/// <summary>Ticks to wait for the target to be opened, or for the harness functions to appear (10 seconds).</summary>
	internal const int ShortAttempts = 40;

	/// <summary>Renders the S0 driver of <paramref name="plan" />.</summary>
	internal static string Render(LuaDriverPlan plan)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.PluginPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(plan.PluginDisplayName);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plan.TargetProcessId);
		if (plan.SettingsToggleProven)
		{
			throw new NotSupportedException("No settings toggle is proven yet: the S0 spike records whether getSettingsForm() " +
											"can toggle a plugin, and the change that records it adds the toggle steps.");
		}

		List<LuaDriverStep> steps = [LuaDriverSteps.MainForm(), .. LuaDriverSteps.OpenProcess("open-process", plan.TargetProcessId)];
		steps.Add(LuaDriverSteps.LoadPlugin("load-plugin", plan.PluginPath));
		steps.Add(LuaDriverSteps.GlobalReady("harness-ready", HarnessFunctionPrefix + "status"));
		foreach (LuaHarnessCall call in plan.Calls)
		{
			RequireName(call.Function, FunctionName(), nameof(plan.Calls));
			steps.Add(LuaDriverSteps.Call(call.Step, call.Function, [.. call.Arguments]));
		}

		steps.Add(LuaDriverSteps.SettingsProbe());
		steps.Add(LuaDriverSteps.Toggle("toggle-disable", false, plan.PluginDisplayName));
		steps.Add(LuaDriverSteps.Toggle("toggle-enable", true, plan.PluginDisplayName));
		steps.Add(LuaDriverSteps.ClearAddressList());
		return RenderSteps(plan.Session, plan.TranscriptPath, steps);
	}

	/// <summary>Renders a driver that runs <paramref name="steps" /> in order, then writes <c>DONE</c> and closes.</summary>
	internal static string RenderSteps(string session, string transcriptPath, IReadOnlyList<LuaDriverStep> steps)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(transcriptPath);
		ArgumentNullException.ThrowIfNull(steps);
		RequireName(session, StepName(), nameof(session));
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (LuaDriverStep step in steps)
		{
			RequireName(step.Name, StepName(), nameof(steps));
			if (!names.Add(step.Name))
			{
				throw new ArgumentException($"The step '{step.Name}' appears twice; transcript steps are unique.", nameof(steps));
			}
		}

		StringBuilder script = new();
		script.Append(CultureInfo.InvariantCulture, $$"""
			-- Generated by CheatEngine.Client.Tests (LiveQualification) for session {{session}}. Never committed: it
			-- lives in the run's sandboxed Cheat Engine only. One step per timer tick on the main thread, each under pcall;
			-- every step appends "R<TAB>step<TAB>ok|error|notexecuted<TAB>%q" to the transcript and flushes it.
			local transcript = assert(io.open({{LuaLiteral.String(transcriptPath)}}, "wb"))

			local function record(step, status, value)
			  transcript:write(string.format("R\t%s\t%s\t%q\n", step, status, tostring(value)))
			  transcript:flush()
			end

			local steps = {

			""");
		foreach (LuaDriverStep step in steps)
		{
			script.Append(RenderStep(step));
		}

		script.Append(CultureInfo.InvariantCulture, $$"""
			}

			local index = 1
			local attempts = 0
			local finished = false

			local function advance()
			  local step = steps[index]
			  if step == nil then
			    finished = true
			    pcall(function()
			      transcript:write("DONE\n")
			      transcript:flush()
			      transcript:close()
			    end)
			    closeCE()
			    return
			  end
			  if step.kind == "notexecuted" then
			    record(step.name, "notexecuted", step.prompt)
			    index = index + 1
			    return
			  end
			  local ok, value = pcall(step.run)
			  if not ok then
			    record(step.name, "error", value)
			  elseif value ~= nil or step.kind == "once" then
			    record(step.name, "ok", value)
			  else
			    attempts = attempts + 1
			    if attempts < step.attempts then return end
			    record(step.name, "error", "no result after " .. attempts .. " attempts")
			  end
			  index = index + 1
			  attempts = 0
			end

			local driver = createTimer(nil, false)
			driver.Interval = {{IntervalMilliseconds}}
			driver.OnTimer = function(sender)
			  sender.Enabled = false
			  local ok, failure = pcall(advance)
			  if not ok then
			    -- The driver itself failed: still clear the address list, then write DONE and close.
			    pcall(record, "driver", "error", failure)
			    index = index < #steps and #steps or #steps + 1
			  end
			  if not finished then sender.Enabled = true end
			end
			driver.Enabled = true

			""");
		return script.ToString().ReplaceLineEndings("\n");
	}

	/// <summary>The S0 plan: load the harness, call <c>status</c>, <c>runtime</c> and <c>capabilities(1)</c>.</summary>
	internal static LuaDriverPlan SpikePlan(string transcriptPath, int targetProcessId, string pluginPath, string pluginDisplayName)
	{
		return new LuaDriverPlan("S0", transcriptPath, targetProcessId, pluginPath, pluginDisplayName,
		[
			new LuaHarnessCall("status", "status", []),
			new LuaHarnessCall("runtime", "runtime", []),
			new LuaHarnessCall("capabilities", "capabilities", [LuaLiteral.Integer(1)])
		], false);
	}

	private static string RenderStep(LuaDriverStep step)
	{
		string name = LuaLiteral.String(step.Name);
		if (step.Kind == LuaDriverStepKind.NotExecuted)
		{
			return $"  {{ name = {name}, kind = \"notexecuted\", prompt = {LuaLiteral.String(step.Body)} }},\n";
		}

		StringBuilder text = new();
		if (step.Kind == LuaDriverStepKind.Poll)
		{
			text.Append(CultureInfo.InvariantCulture,
				$"  {{ name = {name}, kind = \"poll\", attempts = {step.Attempts}, run = function()\n");
		}
		else
		{
			text.Append(CultureInfo.InvariantCulture, $"  {{ name = {name}, kind = \"once\", run = function()\n");
		}

		foreach (string line in step.Body.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'))
		{
			text.Append("    ").Append(line).Append('\n');
		}

		return text.Append("  end },\n").ToString();
	}

	private static void RequireName(string value, Regex pattern, string parameter)
	{
		if (string.IsNullOrEmpty(value) || !pattern.IsMatch(value))
		{
			throw new ArgumentException($"'{value}' is not a valid name ({pattern}).", parameter);
		}
	}

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]*\z", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex StepName();

	[GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex FunctionName();
}
