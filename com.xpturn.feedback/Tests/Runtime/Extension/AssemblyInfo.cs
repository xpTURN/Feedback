// a user's assembly as the package sees it: no InternalsVisibleTo from Core, only the public surface. Fixtures only —
// nothing references it in a player, so a Minimal player would strip it (OptionalAssemblyTests keeps it)
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
