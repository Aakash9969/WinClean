using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinClean.App.Converters;
using WinClean.App.Services;
using WinClean.Core.Memory;
using ILogger = Serilog.ILogger;

namespace WinClean.App.ViewModels;

/// <summary>
/// The Memory page: live memory usage and a working-set optimizer. Trimming reclaims RAM from
/// running applications — nothing is ever terminated, and critical system processes are skipped
/// automatically and listed with a reason.
/// </summary>
public partial class MemoryViewModel : ViewModelBase
{
    private readonly IMemoryService _memoryService;
    private readonly IElevationService _elevationService;
    private readonly ILogger _logger;

    public MemoryViewModel(IMemoryService memoryService, IElevationService elevationService, ILogger logger)
    {
        _memoryService = memoryService;
        _elevationService = elevationService;
        _logger = logger;
    }

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private long _availableBytes;

    [ObservableProperty]
    private long _usedBytes;

    [ObservableProperty]
    private double _usedPercent;

    [ObservableProperty]
    private bool _isElevated;

    public bool ShowElevationHint => !IsElevated;

    partial void OnIsElevatedChanged(bool value) => OnPropertyChanged(nameof(ShowElevationHint));

    [ObservableProperty]
    private bool _hasOptimized;

    [ObservableProperty]
    private MemoryOptimizeResult? _lastResult;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [RelayCommand]
    public void Refresh()
    {
        MemoryInfo info = _memoryService.GetMemoryInfo();
        TotalBytes = info.TotalBytes;
        AvailableBytes = info.AvailableBytes;
        UsedBytes = info.UsedBytes;
        UsedPercent = info.UsedPercent;
        IsElevated = _elevationService.IsElevated;
    }

    [RelayCommand]
    private async Task OptimizeAsync(CancellationToken cancellationToken)
    {
        StatusText = "Optimizing memory…";

        try
        {
            MemoryOptimizeResult result = await _memoryService.OptimizeAsync(cancellationToken);

            LastResult = result;
            HasOptimized = true;
            Refresh();

            _logger.Information(
                "Memory optimize: {BytesFreed} bytes freed, {Trimmed} process(es) trimmed, {Skipped} skipped",
                result.BytesFreed,
                result.TrimmedCount,
                result.SkippedCount);

            string hint = result.SkippedCount > 0 && !IsElevated
                ? $" Restart elevated to also trim the {result.SkippedCount} skipped process(es)."
                : string.Empty;

            StatusText = $"Freed {ByteFormat.Bytes(result.BytesFreed)} — {result.TrimmedCount} process(es) trimmed, {result.SkippedCount} skipped.{hint}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Memory optimize cancelled.";
        }
    }

    [RelayCommand]
    private void RestartElevated() => _elevationService.TryRelaunchElevated(string.Empty);
}
