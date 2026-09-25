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
///     it did, the toggle steps are operator steps (<see cref="LuaDriverStepKind.Operator" />): the driver prompts the
///     operator and waits for the toggle's effect, and never toggles a plugin itself.
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

	/// <summary>
	///     A step the operator performs (a plugin toggle in Settings &gt; Plugins, plan A12). The driver shows the prompt
	///     in a window that leaves Cheat Engine usable and, each tick, runs the body, which returns a value once the
	///     step's effect is observed; a step without a body waits for the operator's Done instead. The value (or the
	///     confirmation) is recorded <c>ok</c>; the operator's Skip, or no effect within
	///     <see cref="LuaDriverStep.Attempts" /> ticks, is recorded <c>notexecuted</c> with the prompt.
	/// </summary>
	Operator
}

/// <summary>
///     One step of a driver: its transcript name, how it runs and its Lua body (the body of a function whose return
///     value the transcript records; for an operator step, the check of its effect), and an operator step's prompt.
/// </summary>
/// <param name="Name">The transcript step name, unique in the driver.</param>
/// <param name="Kind">How the driver runs it.</param>
/// <param name="Body">
///     The Lua function body, lines without indentation; empty for an operator step whose effect the driver cannot
///     observe, which then waits for the operator's confirmation.
/// </param>
/// <param name="Attempts">How many ticks a poll or operator step may take.</param>
/// <param name="Prompt">What the operator must do, for an operator step.</param>
internal sealed record LuaDriverStep(string Name, LuaDriverStepKind Kind, string Body, int Attempts = 0, string? Prompt = null);

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

	/// <summary>
	///     A step the operator performs (<see cref="LuaDriverStepKind.Operator" />): <paramref name="effect" /> returns a
	///     value once the driver observes it done, or is empty when only the operator's confirmation can end the step.
	/// </summary>
	internal static LuaDriverStep Operator(string step, string prompt, string effect)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
		ArgumentNullException.ThrowIfNull(effect);
		return new LuaDriverStep(step, LuaDriverStepKind.Operator, effect, LuaDriverScript.OperatorAttempts, prompt);
	}

	/// <summary>
	///     The operator toggle of a plugin through Settings &gt; Plugins, until the S0 spike proves that the driver can
	///     perform it: the step ends when <paramref name="global" />, a function the plugin exports, is gone after a
	///     disable or back after an enable.
	/// </summary>
	internal static LuaDriverStep Toggle(string step, bool enable, string pluginDisplayName, string global, string? note = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(global);
		string name = LuaLiteral.String(global);
		string effect = enable
			? $"""
			  if type(_G[{name}]) ~= "function" then return nil end
			  return "enabled"
			  """
			: $"""
			  if type(_G[{name}]) == "function" then return nil end
			  return "disabled"
			  """;
		return Operator(step, TogglePrompt(enable, pluginDisplayName, note) +
							  " The driver continues once the plugin's functions are " + (enable ? "back." : "gone."), effect);
	}

	/// <summary>
	///     An operator toggle whose effect the driver cannot observe (an enable that must fail): the step ends with the
	///     operator's Done.
	/// </summary>
	internal static LuaDriverStep ConfirmedToggle(string step, bool enable, string pluginDisplayName, string note)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(note);
		return Operator(step, TogglePrompt(enable, pluginDisplayName, note) + " Then press Done.", string.Empty);
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

	private static string TogglePrompt(bool enable, string pluginDisplayName, string? note)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginDisplayName);
		string prompt = $"Operator: in Edit > Settings > Plugins, {(enable ? "tick" : "untick")} '{pluginDisplayName}' and press OK.";
		return note is null ? prompt : prompt + " " + note;
	}
}

/// <summary>
///     Generates the autorun driver of one session: a <c>createTimer</c> state machine that runs on Cheat Engine's main
///     thread, one step per tick, each step under <c>pcall</c>. A session composes its steps from
///     <see cref="LuaDriverSteps" />: it waits for the main form, opens the targets through Cheat Engine, loads the
///     plugins, calls them, clears the address list so no save prompt can block, writes <c>DONE</c> and calls
///     <c>closeCE()</c>. Until the spike proves that the driver can toggle a plugin, each toggle is an operator step
///     (plan A12): a window that leaves Cheat Engine usable shows the prompt with a Skip button (and Done when the effect
///     cannot be observed), and the driver waits up to 90 seconds for the plugin's functions to disappear or come back.
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

	/// <summary>Ticks to wait for an operator step (90 seconds); five of them still fit a session's 10 minutes.</summary>
	internal const int OperatorAttempts = 360;

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
		steps.Add(LuaDriverSteps.Toggle("toggle-disable", false, plan.PluginDisplayName, HarnessFunctionPrefix + "status"));
		steps.Add(LuaDriverSteps.Toggle("toggle-enable", true, plan.PluginDisplayName, HarnessFunctionPrefix + "status"));
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

			-- The operator window: it does not block Cheat Engine, so the operator can open Settings > Plugins.
			local prompt = nil
			local answer = nil

			local function closePrompt()
			  local form = prompt
			  prompt = nil
			  if form ~= nil then
			    pcall(function() form.destroy() end)
			  end
			  answer = nil
			end

			local function showPrompt(step)
			  local form = createForm(false)
			  prompt = form
			  form.Caption = "CheatEngine.Client qualification: operator step " .. step.name
			  form.setSize(560, 180)
			  pcall(function() form.FormStyle = "fsStayOnTop" end)
			  local text = createMemo(form)
			  text.setPosition(12, 12)
			  text.setSize(536, 96)
			  text.WordWrap = true
			  pcall(function() text.ReadOnly = true end)
			  text.append(step.prompt)
			  local skip = createButton(form)
			  skip.Caption = "Skip"
			  skip.setPosition(460, 124)
			  skip.OnClick = function() answer = "skip" end
			  if step.run == nil then
			    local done = createButton(form)
			    done.Caption = "Done"
			    done.setPosition(372, 124)
			    done.OnClick = function() answer = "done" end
			  end
			  form.OnClose = function()
			    -- Closing the window skips the step; a window the driver destroys is no longer the prompt.
			    if prompt == form and answer == nil then answer = "skip" end
			    return 1 -- caHide
			  end
			  form.centerScreen()
			  form.show()
			end

			local function operatorStep(step)
			  local ok, value = true, nil
			  if step.run ~= nil then
			    ok, value = pcall(step.run)
			  elseif answer == "done" then
			    value = "confirmed by the operator"
			  end
			  if not ok then
			    record(step.name, "error", value)
			  elseif value ~= nil then
			    record(step.name, "ok", value)
			  elseif answer == "skip" then
			    record(step.name, "notexecuted", "skipped by the operator: " .. step.prompt)
			  elseif attempts + 1 >= step.attempts then
			    record(step.name, "notexecuted", "no operator action within {{OperatorAttempts * IntervalMilliseconds / 1000}} seconds: " .. step.prompt)
			  else
			    if prompt == nil then
			      local shown, failure = pcall(showPrompt, step)
			      if not shown then
			        closePrompt()
			        record(step.name, "error", failure)
			        return true
			      end
			    end
			    attempts = attempts + 1
			    return false
			  end
			  closePrompt()
			  return true
			end

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
			  if step.kind == "operator" then
			    if not operatorStep(step) then return end
			    index = index + 1
			    attempts = 0
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
			    pcall(closePrompt)
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
		StringBuilder text = new();
		if (step.Kind == LuaDriverStepKind.Operator)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(step.Prompt);
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step.Attempts);
			text.Append(CultureInfo.InvariantCulture,
				$"  {{ name = {name}, kind = \"operator\", attempts = {step.Attempts}, prompt = {LuaLiteral.String(step.Prompt)}");
			if (step.Body.Length == 0)
			{
				return text.Append(" },\n").ToString();
			}

			text.Append(", run = function()\n");
		}
		else if (step.Kind == LuaDriverStepKind.Poll)
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
