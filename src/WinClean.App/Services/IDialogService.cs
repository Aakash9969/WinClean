using WinClean.Core.Models;

namespace WinClean.App.Services;

/// <summary>One rule line shown in the clean confirmation dialog.</summary>
public sealed record CleanRuleLine(
    string RuleId,
    string RuleName,
    RiskLevel RiskLevel,
    bool RequiresAdmin,
    long TotalBytes,
    int FileCount);

/// <summary>What the user is about to approve in the confirmation dialog.</summary>
public sealed record CleanConfirmation(
    long TotalBytes,
    int TotalFiles,
    bool ShowAdminNote,
    IReadOnlyList<CleanRuleLine> Rules);

/// <summary>The user's decision on the elevation prompt.</summary>
public enum ElevationDecision
{
    /// <summary>Relaunch WinClean elevated (the current instance exits).</summary>
    Elevate,

    /// <summary>Keep running without admin rights; protected locations are skipped.</summary>
    ContinueWithout,

    /// <summary>Abort the action entirely.</summary>
    Cancel,
}

/// <summary>Shows modal dialogs. Implemented in the view layer; view models depend only on this interface.</summary>
public interface IDialogService
{
    /// <summary>Shows the "confirm clean" dialog. True only when the user explicitly approves.</summary>
    bool ConfirmClean(CleanConfirmation confirmation);

    /// <summary>Asks whether to relaunch elevated because admin-requiring rules are enabled.</summary>
    ElevationDecision PromptForElevation(IReadOnlyList<string> adminRuleNames);
}
