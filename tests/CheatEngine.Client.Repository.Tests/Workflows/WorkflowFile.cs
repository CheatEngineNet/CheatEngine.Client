using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Workflows;

/// <summary>
/// A GitHub Actions workflow or composite action metadata file: its raw text (comments carry the pin versions and the
/// justifications, which the YAML model drops) and its YAML representation. The representation model keeps every
/// scalar as text, so the <c>on</c> key stays a plain key and <c>true</c> stays the string it was written as.
/// </summary>
internal sealed class WorkflowFile
{
	private const string WorkflowFolder = ".github/workflows";
	private const string ActionFolder = ".github/actions";

	private WorkflowFile(string relativePath, string text, YamlMappingNode root)
	{
		RelativePath = relativePath;
		Text = text;
		Root = root;
	}

	/// <summary>The repository-relative path with forward slashes.</summary>
	public string RelativePath
	{
		get;
	}

	/// <summary>The file name, for example <c>ci.yml</c>.</summary>
	public string FileName => Path.GetFileName(RelativePath);

	/// <summary>The raw text.</summary>
	public string Text
	{
		get;
	}

	/// <summary>The root mapping.</summary>
	public YamlMappingNode Root
	{
		get;
	}

	/// <summary>The jobs of a workflow; empty for composite action metadata.</summary>
	public IReadOnlyList<WorkflowJob> Jobs
	{
		get
		{
			if (Yaml.Mapping(Root, "jobs") is not YamlMappingNode jobs)
			{
				return [];
			}

			List<WorkflowJob> result = [];
			foreach (KeyValuePair<YamlNode, YamlNode> job in jobs.Children)
			{
				result.Add(new WorkflowJob(this, ((YamlScalarNode) job.Key).Value!, (YamlMappingNode) job.Value));
			}

			return result;
		}
	}

	/// <summary>The steps of a composite action (<c>runs.steps</c>); empty for workflows.</summary>
	public IReadOnlyList<WorkflowStep> CompositeSteps =>
		Yaml.Mapping(Root, "runs") is YamlMappingNode runs ? WorkflowStep.From(Yaml.Sequence(runs, "steps")) : [];

	/// <summary>Loads a file by repository-relative path.</summary>
	public static WorkflowFile Load(string relativePath)
	{
		string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath));
		YamlStream stream = [];
		stream.Load(new StringReader(text));
		YamlMappingNode root = stream.Documents[0].RootNode as YamlMappingNode
							   ?? throw new InvalidOperationException($"{relativePath} is not a YAML mapping.");
		return new WorkflowFile(relativePath.Replace('\\', '/'), text, root);
	}

	/// <summary>True when the repository-relative file exists.</summary>
	public static bool Exists(string relativePath)
	{
		return File.Exists(Path.Combine(RepositoryRoot.Path, relativePath));
	}

	/// <summary>Every workflow that exists today, so the rules also cover workflows added later.</summary>
	public static IReadOnlyList<WorkflowFile> Workflows()
	{
		return EnumerateYaml(WorkflowFolder, SearchOption.TopDirectoryOnly);
	}

	/// <summary>Every local composite action metadata file (<c>.github/actions/**/action.yml</c>).</summary>
	public static IReadOnlyList<WorkflowFile> Actions()
	{
		return EnumerateYaml(ActionFolder, SearchOption.AllDirectories)
			.Where(static file => file.FileName is "action.yml" or "action.yaml")
			.ToArray();
	}

	/// <summary>Workflows and action metadata together.</summary>
	public static IReadOnlyList<WorkflowFile> WorkflowsAndActions()
	{
		return [.. Workflows(), .. Actions()];
	}

	/// <summary>The job with the given id; fails the test when it does not exist.</summary>
	public WorkflowJob Job(string id)
	{
		return Jobs.SingleOrDefault(job => job.Id == id)
			   ?? throw new Xunit.Sdk.XunitException($"{RelativePath} has no job '{id}'.");
	}

	private static WorkflowFile[] EnumerateYaml(string folder, SearchOption option)
	{
		string path = Path.Combine(RepositoryRoot.Path, folder);
		if (!Directory.Exists(path))
		{
			return [];
		}

		return Directory.EnumerateFiles(path, "*.*", option)
			.Where(static file => file.EndsWith(".yml", StringComparison.Ordinal) ||
								  file.EndsWith(".yaml", StringComparison.Ordinal))
			.Select(static file => RepositoryRoot.ToRelative(file))
			.Order(StringComparer.Ordinal)
			.Select(Load)
			.ToArray();
	}
}

/// <summary>A job of a workflow.</summary>
internal sealed class WorkflowJob(WorkflowFile file, string id, YamlMappingNode node)
{
	/// <summary>The file that declares the job.</summary>
	public WorkflowFile File { get; } = file;

	/// <summary>The job id (its key under <c>jobs</c>).</summary>
	public string Id { get; } = id;

	/// <summary>The job mapping.</summary>
	public YamlMappingNode Node { get; } = node;

	/// <summary>The display name.</summary>
	public string? Name => Yaml.Scalar(Node, "name");

	/// <summary>The job-level condition, if any.</summary>
	public string? Condition => Yaml.Scalar(Node, "if");

	/// <summary>The reusable workflow a caller job invokes, if any.</summary>
	public string? Uses => Yaml.Scalar(Node, "uses");

	/// <summary>The runner label.</summary>
	public string? RunsOn => Yaml.Scalar(Node, "runs-on");

	/// <summary>The job timeout.</summary>
	public string? TimeoutMinutes => Yaml.Scalar(Node, "timeout-minutes");

	/// <summary>The steps; empty for a reusable-workflow caller.</summary>
	public IReadOnlyList<WorkflowStep> Steps => WorkflowStep.From(Yaml.Sequence(Node, "steps"));

	/// <summary>The job ids in <c>needs</c>, whether written as a scalar or a sequence.</summary>
	public IReadOnlyList<string> Needs => Yaml.Get(Node, "needs") switch
	{
		YamlScalarNode scalar => [scalar.Value!],
		YamlSequenceNode sequence => sequence.Children.Select(static item => ((YamlScalarNode) item).Value!).ToArray(),
		_ => []
	};

	/// <summary>The values of each matrix dimension (<c>strategy.matrix.&lt;name&gt;</c>).</summary>
	public IReadOnlyDictionary<string, string[]> Matrix
	{
		get
		{
			Dictionary<string, string[]> matrix = new(StringComparer.Ordinal);
			if (Yaml.Mapping(Node, "strategy") is YamlMappingNode strategy &&
				Yaml.Mapping(strategy, "matrix") is YamlMappingNode dimensions)
			{
				foreach (KeyValuePair<YamlNode, YamlNode> dimension in dimensions.Children)
				{
					if (dimension.Value is YamlSequenceNode values)
					{
						matrix[((YamlScalarNode) dimension.Key).Value!] =
							values.Children.Select(static value => ((YamlScalarNode) value).Value!).ToArray();
					}
				}
			}

			return matrix;
		}
	}

	/// <summary>
	/// Expands every <c>${{ matrix.&lt;name&gt; }}</c> of a value over the job's matrix, so reserved names such as
	/// <c>test-results-${{ matrix.configuration }}</c> are checked for each leg.
	/// </summary>
	public IReadOnlyList<string> ExpandMatrix(string value)
	{
		List<string> results = [value];
		foreach ((string name, string[] values) in Matrix)
		{
			Regex placeholder = new(@"\$\{\{\s*matrix\." + Regex.Escape(name) + @"\s*\}\}");
			results = results.SelectMany(result => placeholder.IsMatch(result)
					? values.Select(matrixValue => placeholder.Replace(result, matrixValue))
					: new[] { result })
				.ToList();
		}

		return results;
	}
}

/// <summary>A step of a job or of a composite action.</summary>
internal sealed class WorkflowStep(int index, YamlMappingNode node)
{
	/// <summary>The position of the step in its job.</summary>
	public int Index { get; } = index;

	/// <summary>The step mapping.</summary>
	public YamlMappingNode Node { get; } = node;

	/// <summary>The display name.</summary>
	public string? Name => Yaml.Scalar(Node, "name");

	/// <summary>The step id.</summary>
	public string? Id => Yaml.Scalar(Node, "id");

	/// <summary>The action reference.</summary>
	public string? Uses => Yaml.Scalar(Node, "uses");

	/// <summary>The script.</summary>
	public string Run => Yaml.Scalar(Node, "run") ?? string.Empty;

	/// <summary>The step condition, if any.</summary>
	public string? Condition => Yaml.Scalar(Node, "if");

	/// <summary>True when the step uses the given action (any version, any sub-path).</summary>
	public bool UsesAction(string ownerAndRepository)
	{
		return Uses is not null && Uses.StartsWith(ownerAndRepository, StringComparison.Ordinal) &&
			   (Uses.Length == ownerAndRepository.Length || Uses[ownerAndRepository.Length] is '@' or '/');
	}

	/// <summary>An input of <c>with</c>.</summary>
	public string? With(string key)
	{
		return Yaml.Mapping(Node, "with") is YamlMappingNode with ? Yaml.Scalar(with, key) : null;
	}

	/// <summary>A variable of the step <c>env</c>.</summary>
	public string? Env(string key)
	{
		return Yaml.Mapping(Node, "env") is YamlMappingNode env ? Yaml.Scalar(env, key) : null;
	}

	/// <summary>Wraps the items of a <c>steps</c> sequence.</summary>
	public static IReadOnlyList<WorkflowStep> From(YamlSequenceNode? steps)
	{
		if (steps is null)
		{
			return [];
		}

		return steps.Children.Select(static (step, index) => new WorkflowStep(index, (YamlMappingNode) step)).ToArray();
	}
}

/// <summary>Small accessors over the YamlDotNet representation model.</summary>
internal static partial class Yaml
{
	/// <summary>The value of a key, or null.</summary>
	public static YamlNode? Get(YamlMappingNode? mapping, string key)
	{
		if (mapping is null)
		{
			return null;
		}

		return mapping.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) ? value : null;
	}

	/// <summary>A scalar value, or null.</summary>
	public static string? Scalar(YamlMappingNode? mapping, string key)
	{
		return (Get(mapping, key) as YamlScalarNode)?.Value;
	}

	/// <summary>A mapping value, or null.</summary>
	public static YamlMappingNode? Mapping(YamlMappingNode? mapping, string key)
	{
		return Get(mapping, key) as YamlMappingNode;
	}

	/// <summary>A sequence value, or null.</summary>
	public static YamlSequenceNode? Sequence(YamlMappingNode? mapping, string key)
	{
		return Get(mapping, key) as YamlSequenceNode;
	}

	/// <summary>The keys of a mapping.</summary>
	public static IEnumerable<string> Keys(YamlMappingNode? mapping)
	{
		return mapping is null ? [] : mapping.Children.Keys.Select(static key => ((YamlScalarNode) key).Value!);
	}

	/// <summary>
	/// An expression as GitHub evaluates it, for textual comparison: the optional <c>${{ }}</c> wrapper removed and every
	/// run of whitespace (including the line breaks of a folded scalar) collapsed to one space.
	/// </summary>
	public static string NormalizeExpression(string? expression)
	{
		string text = (expression ?? string.Empty).Trim();
		Match wrapped = WrappedExpression().Match(text);
		if (wrapped.Success)
		{
			text = wrapped.Groups["body"].Value;
		}

		return Whitespace().Replace(text, " ").Trim();
	}

	/// <summary>
	/// The command-line tokens of a PowerShell script: quoted strings unquoted, separators (commas, parentheses, <c>@(</c>)
	/// dropped, so <c>'--hangdump-timeout', '15m'</c> and <c>--hangdump-timeout 15m</c> read the same.
	/// </summary>
	public static IReadOnlyList<string> Tokens(string script)
	{
		List<string> tokens = [];
		foreach (Match match in Token().Matches(script))
		{
			string token = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["bare"].Value;
			if (token.Length > 0)
			{
				tokens.Add(token);
			}
		}

		return tokens;
	}

	[GeneratedRegex(@"^\$\{\{(?<body>.*)\}\}$", RegexOptions.Singleline)]
	private static partial Regex WrappedExpression();

	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();

	[GeneratedRegex(@"'(?<quoted>[^']*)'|""(?<quoted>[^""]*)""|(?<bare>[^\s,()@'""]+)")]
	private static partial Regex Token();
}
