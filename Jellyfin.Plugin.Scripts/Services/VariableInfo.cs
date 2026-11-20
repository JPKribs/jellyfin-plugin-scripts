using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// A variable a script receives, documented for the editor.
/// </summary>
/// <param name="Name">The name without the <c>JF_</c> prefix, as used in a <c>{{NAME}}</c> placeholder.</param>
/// <param name="DescriptionKey">The translation key of what the variable holds.</param>
public sealed record VariableInfo(string Name, string DescriptionKey)
{
    /// <summary>Gets what the variable holds, in US English.</summary>
    public string Description => BaseTranslations.Get(DescriptionKey);

    /// <summary>
    /// Gets what the variable holds in a language.
    /// </summary>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The description.</returns>
    public string DescriptionIn(string? language) => BaseTranslations.Get(DescriptionKey, language);
}
