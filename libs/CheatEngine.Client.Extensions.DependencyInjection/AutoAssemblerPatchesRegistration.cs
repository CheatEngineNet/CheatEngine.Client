namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Records the builder-only opt-in required to enable Auto Assembler patches for an activation.</summary>
internal sealed class AutoAssemblerPatchesRegistration
{
	internal bool IsEnabled
	{
		get;
	} = true;
}
