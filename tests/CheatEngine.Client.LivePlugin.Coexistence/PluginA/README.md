# CheatEngine.Client.LivePlugin.Coexistence.PluginA

Plugin A is one half of the opt-in two-plugin fixture described by the parent
[coexistence protocol](../README.md). It uses the public Client hosting and generated Lua-module path, retains the
positive Lua owner marker used by the collision contender, and must remain in its own complete build output directory.
It is not a unit test or evidence that a Cheat Engine loader isolates Plugin A from Plugin B.
