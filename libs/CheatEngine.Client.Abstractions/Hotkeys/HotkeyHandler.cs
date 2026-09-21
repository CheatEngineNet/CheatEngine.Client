namespace CheatEngine.Client.Hotkeys;

/// <summary>Handles a copied hotkey event synchronously without blocking the host callback thread.</summary>
public delegate void HotkeyHandler(HotkeyEvent hotkeyEvent);
