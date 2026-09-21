# CheatEngine.Client.LivePlugin.Coexistence.PluginB

Plugin B is one half of the opt-in two-plugin fixture described by the parent
[coexistence protocol](../README.md). It uses the public Client hosting and generated Lua-module path, with distinct
Lua globals, and must remain in its own complete build output directory. It is not a unit test or evidence that a
Cheat Engine loader isolates Plugin B from Plugin A.
