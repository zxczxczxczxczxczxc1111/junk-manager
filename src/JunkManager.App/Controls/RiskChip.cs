using System.Windows;
using System.Windows.Controls;
using JunkManager.Core;

namespace JunkManager.App.Controls;

/// <summary>
/// Two tiers, each with its own outline, its own word and its own colour.
/// </summary>
/// <remarks>
/// Shape and word are not decoration next to the colour: the spec requires a
/// tier to be readable without colour at all, and the copper pair was chosen
/// against protanopia precisely so the fallback is never needed. It is still
/// here, because a colour-only badge is one accessibility setting away from
/// meaning nothing.
/// </remarks>
internal sealed class RiskChip : Control
{
    public static readonly DependencyProperty TierProperty =
        DependencyProperty.Register(
            nameof(Tier), typeof(RiskTier), typeof(RiskChip),
            new FrameworkPropertyMetadata(RiskTier.Safe));

    public RiskTier Tier
    {
        get => (RiskTier)GetValue(TierProperty);
        set => SetValue(TierProperty, value);
    }
}
