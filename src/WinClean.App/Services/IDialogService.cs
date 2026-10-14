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
    IReadOnlyList<CleanRuleLine> Rules);

/// <summary>Shows modal dialogs. Implemented in the view layer; view models depend only on this interface.</summary>
public interface IDialogService
{
    /// <summary>Shows the "confirm clean" dialog. True only when the user explicitly approves.</summary>
    bool ConfirmClean(CleanConfirmation confirmation);
}
