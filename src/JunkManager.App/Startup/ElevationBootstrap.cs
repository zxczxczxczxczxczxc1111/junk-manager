using JunkManager.Safety;

namespace JunkManager.App.Startup;

internal enum ElevationDecision
{
    NotRequested,
    AlreadyElevated,
    Relaunched,
    UserDeclined,
}

/// <summary>
/// Runs before any window exists. That is the whole point: one UAC prompt at
/// startup instead of a relaunch in the middle of work, which throws away the
/// selection the person just made. Spec section 11.
/// </summary>
/// <remarks>
/// <para>
/// Everything factual is asked of <see cref="Elevation"/>: whether we are
/// administrator, how the original identity travels, how the elevated copy is
/// started. Two implementations of elevation in one product are two different
/// answers to "whose profile are we cleaning", and the spec allows one.
/// </para>
/// <para>
/// What lives here and nowhere else is the switch. Ui tests start the built exe
/// and cannot dismiss a UAC dialog, because it is a modal window belonging to
/// another process, so they need a documented way to say "do not even ask".
/// </para>
/// </remarks>
internal static class ElevationBootstrap
{
    /// <summary>Passed to the elevated copy so it cannot ask for elevation again.</summary>
    internal const string NoElevateSwitch = "--no-elevate";

    /// <summary>The name the original SID travels under. One name per product.</summary>
    internal const string ArgumentSid = Elevation.ArgumentSid;

    /// <summary>The name the original profile path travels under.</summary>
    internal const string ArgumentProfile = Elevation.ArgumentProfile;

    /// <summary>Whether this process runs with administrator rights.</summary>
    internal static bool IsElevated => Elevation.IsElevated;

    /// <summary>
    /// Pure decision, so the whole policy can be tested without a UAC dialog,
    /// a second process, or an administrator account.
    /// </summary>
    /// <param name="relaunch">
    /// Starts the elevated copy and answers whether it went up. Takes only the
    /// arguments: the executable to start is not this type's decision, it is
    /// <see cref="Elevation"/>'s, and passing a path through here would be a
    /// second place that can name the wrong file.
    /// </param>
    internal static ElevationDecision Decide(
        bool wantElevation,
        bool alreadyElevated,
        IReadOnlyList<string> args,
        Func<string[], bool> relaunch)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(relaunch);

        if (!wantElevation || args.Contains(NoElevateSwitch, StringComparer.OrdinalIgnoreCase))
        {
            return ElevationDecision.NotRequested;
        }

        if (alreadyElevated)
        {
            return ElevationDecision.AlreadyElevated;
        }

        var forwarded = args.Append(NoElevateSwitch).ToArray();

        return relaunch(forwarded)
            ? ElevationDecision.Relaunched
            : ElevationDecision.UserDeclined;
    }

    /// <summary>
    /// The production relaunch. Hands the work to <see cref="Elevation"/>,
    /// which attaches the original SID and profile and knows that a refusal in
    /// the UAC dialog is an ordinary outcome rather than a fault.
    /// </summary>
    internal static bool Relaunch(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!Elevation.TryElevate(wanted: true, args, out var ishod, out _))
        {
            return false;
        }

        return ishod == ElevationOutcome.Relaunched;
    }
}
