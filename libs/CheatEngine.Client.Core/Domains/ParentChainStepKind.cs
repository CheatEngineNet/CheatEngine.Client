namespace CheatEngine.Client.Core.Domains;

/// <summary>Represents the outcome of reading one candidate-parent link.</summary>
internal enum ParentChainStepKind
{
	Root,
	Parent,
	HostRejected
}
