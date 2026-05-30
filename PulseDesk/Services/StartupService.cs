using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace PulseDesk.Services;

/// <summary>
/// Manages the app's startup task registration with Windows via the MSIX StartupTask API.
/// </summary>
public sealed class StartupService
{
    private const string TaskId = "PulseDesk";

    /// <summary>
    /// Returns whether the startup task is currently enabled.
    /// </summary>
    public async Task<bool> IsEnabledAsync()
    {
        var state = await GetStateAsync();
        return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    /// <summary>
    /// Returns whether the startup task was disabled by the user via Task Manager
    /// and cannot be re-enabled programmatically.
    /// </summary>
    public async Task<bool> IsDisabledByUserAsync()
    {
        var state = await GetStateAsync();
        return state is StartupTaskState.DisabledByUser;
    }

    /// <summary>
    /// Attempts to enable the startup task. Returns <c>true</c> if the task is now enabled.
    /// Returns <c>false</c> when the user previously disabled it via Task Manager or Group Policy blocks it.
    /// </summary>
    public async Task<bool> EnableAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            var newState = await task.RequestEnableAsync();
            return newState is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch
        {
            // Unpackaged or unsupported scenario.
            return false;
        }
    }

    /// <summary>
    /// Disables the startup task.
    /// </summary>
    public async Task DisableAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            task.Disable();
        }
        catch
        {
            // Unpackaged or unsupported scenario.
        }
    }

    private static async Task<StartupTaskState> GetStateAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return task.State;
        }
        catch
        {
            return StartupTaskState.DisabledByPolicy;
        }
    }
}
