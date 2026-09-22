using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Reads the committed governance files (workflows, Dependabot, issue forms, scripts) for the Governance tests. YAML is
/// loaded with YamlDotNet's representation model, so the key <c>on</c> stays the string "on" and quoted scalars stay
/// strings. Comments are dropped by YAML parsing; tests that need them read the raw text.
/// </summary>
internal static class GovernanceFile
{
	/// <summary>Absolute path of a repository-relative file.</summary>
	public static string FullPath(string relativePath)
	{
		return Path.Combine(RepositoryRoot.Path, relativePath);
	}

	/// <summary>The raw text of a repository-relative file.</summary>
	public static string ReadText(string relativePath)
	{
		string path = FullPath(relativePath);
		Assert.True(File.Exists(path), $"'{relativePath}' does not exist.");
		return File.ReadAllText(path);
	}

	/// <summary>The root mapping of a repository-relative YAML file.</summary>
	public static YamlMappingNode LoadYaml(string relativePath)
	{
		YamlStream stream = new();
		using (StringReader reader = new(ReadText(relativePath)))
		{
			stream.Load(reader);
		}

		Assert.True(stream.Documents.Count == 1, $"'{relativePath}' must contain exactly one YAML document.");
		return Assert.IsType<YamlMappingNode>(stream.Documents[0].RootNode);
	}

	/// <summary>The child mapping at <paramref name="key"/>, or null when absent or not a mapping.</summary>
	public static YamlMappingNode? Mapping(YamlMappingNode node, string key)
	{
		return Child(node, key) as YamlMappingNode;
	}

	/// <summary>The child sequence at <paramref name="key"/>, or null when absent or not a sequence.</summary>
	public static YamlSequenceNode? Sequence(YamlMappingNode node, string key)
	{
		return Child(node, key) as YamlSequenceNode;
	}

	/// <summary>The scalar value at <paramref name="key"/>, or null when absent or not a scalar.</summary>
	public static string? Scalar(YamlMappingNode node, string key)
	{
		return (Child(node, key) as YamlScalarNode)?.Value;
	}

	/// <summary>Whether the mapping has <paramref name="key"/>, whatever its value (a bare trigger is null).</summary>
	public static bool Has(YamlMappingNode node, string key)
	{
		return node.Children.ContainsKey(new YamlScalarNode(key));
	}

	/// <summary>The child node at <paramref name="key"/>, or null.</summary>
	public static YamlNode? Child(YamlMappingNode node, string key)
	{
		return node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) ? value : null;
	}

	/// <summary>The strings of a sequence, or a one-element list for a scalar (keys such as <c>needs</c>).</summary>
	public static IReadOnlyList<string> Strings(YamlNode? node)
	{
		return node switch
		{
			YamlScalarNode scalar when scalar.Value is not null => [scalar.Value],
			YamlSequenceNode sequence => [.. sequence.Children.OfType<YamlScalarNode>().Select(item => item.Value ?? "")],
			_ => []
		};
	}

	/// <summary>The keys of a mapping, in file order.</summary>
	public static IReadOnlyList<string> Keys(YamlMappingNode node)
	{
		return [.. node.Children.Keys.Select(key => ((YamlScalarNode) key).Value ?? "")];
	}

	/// <summary>The single step that uses <paramref name="action"/> (<c>owner/repo[/path]</c> or <c>./path</c>).</summary>
	public static YamlMappingNode StepUsing(IReadOnlyList<YamlMappingNode> steps, string action)
	{
		return Assert.Single(steps, step => ActionName(step) == action);
	}

	/// <summary>The index of the first step that uses <paramref name="action"/>, or -1.</summary>
	public static int IndexOfAction(IReadOnlyList<YamlMappingNode> steps, string action)
	{
		return IndexOf(steps, step => ActionName(step) == action);
	}

	/// <summary>The index of the first <c>run</c> step whose script contains <paramref name="fragment"/>, or -1.</summary>
	public static int IndexOfRun(IReadOnlyList<YamlMappingNode> steps, string fragment)
	{
		return IndexOf(steps, step => Scalar(step, "run")?.Contains(fragment, StringComparison.Ordinal) == true);
	}

	/// <summary>The branch filter of the <c>push</c> trigger.</summary>
	public static IReadOnlyList<string> PushBranches(YamlMappingNode workflow)
	{
		YamlMappingNode push = Mapping(Triggers(workflow), "push") ?? throw new InvalidOperationException("No push trigger.");
		return Strings(Child(push, "branches"));
	}

	/// <summary>The <c>if</c> condition of a job or step with its whitespace normalized (empty when absent).</summary>
	public static string Condition(YamlMappingNode jobOrStep)
	{
		return NormalizeWhitespace(Scalar(jobOrStep, "if") ?? "");
	}

	/// <summary>The <c>jobs</c> mapping of a workflow, keyed by job id.</summary>
	public static IReadOnlyDictionary<string, YamlMappingNode> Jobs(YamlMappingNode workflow)
	{
		YamlMappingNode jobs = Mapping(workflow, "jobs") ?? throw new InvalidOperationException("The workflow has no jobs.");
		Dictionary<string, YamlMappingNode> result = new(StringComparer.Ordinal);
		foreach (KeyValuePair<YamlNode, YamlNode> job in jobs.Children)
		{
			result.Add(((YamlScalarNode) job.Key).Value!, (YamlMappingNode) job.Value);
		}

		return result;
	}

	/// <summary>The steps of a job (empty for a job that calls a reusable workflow).</summary>
	public static IReadOnlyList<YamlMappingNode> Steps(YamlMappingNode job)
	{
		return [.. (Sequence(job, "steps")?.Children ?? []).Cast<YamlMappingNode>()];
	}

	/// <summary>The <c>on</c> mapping of a workflow (a bare trigger list is not used in this repository).</summary>
	public static YamlMappingNode Triggers(YamlMappingNode workflow)
	{
		return Mapping(workflow, "on") ?? throw new InvalidOperationException("The workflow has no 'on' mapping.");
	}

	/// <summary>The permission map of a workflow or job (empty when absent or <c>{}</c>).</summary>
	public static IReadOnlyDictionary<string, string> Permissions(YamlMappingNode workflowOrJob)
	{
		Dictionary<string, string> result = new(StringComparer.Ordinal);
		if (Mapping(workflowOrJob, "permissions") is { } permissions)
		{
			foreach (KeyValuePair<YamlNode, YamlNode> entry in permissions.Children)
			{
				result.Add(((YamlScalarNode) entry.Key).Value!, ((YamlScalarNode) entry.Value).Value ?? string.Empty);
			}
		}

		return result;
	}

	/// <summary>The <c>owner/repo[/path]</c> part of a step's <c>uses</c>, or null for a <c>run</c> step.</summary>
	public static string? ActionName(YamlMappingNode step)
	{
		string? uses = Scalar(step, "uses");
		if (uses is null)
		{
			return null;
		}

		int at = uses.IndexOf('@', StringComparison.Ordinal);
		return at < 0 ? uses : uses[..at];
	}

	/// <summary>The <c>with</c> input of a step, or null.</summary>
	public static string? With(YamlMappingNode step, string input)
	{
		return Mapping(step, "with") is { } with ? Scalar(with, input) : null;
	}

	/// <summary>Collapses every whitespace run to one space (folded <c>if: &gt;-</c> scalars).</summary>
	public static string NormalizeWhitespace(string value)
	{
		return Regex.Replace(value, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
	}

	private static int IndexOf(IReadOnlyList<YamlMappingNode> steps, Func<YamlMappingNode, bool> predicate)
	{
		for (int index = 0; index < steps.Count; index++)
		{
			if (predicate(steps[index]))
			{
				return index;
			}
		}

		return -1;
	}
}
