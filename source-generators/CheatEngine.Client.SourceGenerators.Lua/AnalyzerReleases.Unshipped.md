### New Rules

 Rule ID    | Category               | Severity | Notes
------------|------------------------|----------|--------------------------------------------------------------------------------------
 CECLUA1001 | CheatEngine.Client.Lua | Error    | A generated Lua module must be a supported partial class.
 CECLUA1002 | CheatEngine.Client.Lua | Error    | A generated Lua module requires static SDK bindings.
 CECLUA1003 | CheatEngine.Client.Lua | Error    | A generated Lua module requires at least one export.
 CECLUA1004 | CheatEngine.Client.Lua | Error    | Generated Lua module export names must be unique.
 CECLUA1005 | CheatEngine.Client.Lua | Error    | A generated Lua module identity cannot be blank.
 CECLUA1006 | CheatEngine.Client.Lua | Error    | A generated Lua module must have a public DI constructor.
 CECLUA1101 | CheatEngine.Client.Lua | Error    | A generated Lua operation must target a supported Lua global.
 CECLUA1102 | CheatEngine.Client.Lua | Error    | Generated Lua operation methods cannot be overloaded.
 CECLUA1103 | CheatEngine.Client.Lua | Error    | Generated Lua operation signatures must be bounded.
 CECLUA1104 | CheatEngine.Client.Lua | Error    | Non-scalar Lua operation results require a mapper.
 CECLUA1105 | CheatEngine.Client.Lua | Error    | A Lua operation mapper must match the SDK result.
 CECLUA1106 | CheatEngine.Client.Lua | Error    | A Lua mapper must project a safe, recursively closed Client result and source graph.
 CECLUA1201 | CheatEngine.Client.Lua | Error    | A Lua global has a single owning module per plugin assembly (Q16).
 CECLUA1202 | CheatEngine.Client.Lua | Error    | A Lua module cannot declare a member reserved by the generated registration.
 CECLUA1203 | CheatEngine.Client.Lua | Error    | A Lua module cannot derive from a Lua module implementation.
 CECLUA1204 | CheatEngine.Client.Lua | Error    | Lua module annotations must be the contract types, not look-alikes.
