namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// A variable a script receives, documented for the editor.
/// </summary>
/// <param name="Name">The name without the <c>JF_</c> prefix, as used in a <c>{{NAME}}</c> placeholder.</param>
/// <param name="Description">What the variable holds.</param>
public sealed record VariableInfo(string Name, string Description);
