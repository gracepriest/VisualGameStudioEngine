namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Service for executing commands.
/// </summary>
public interface ICommandService
{
    /// <summary>
    /// Executes a command asynchronously.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="args">Optional command arguments.</param>
    /// <returns>A task representing the command execution.</returns>
    Task ExecuteAsync(string commandId, object? args = null);

    /// <summary>
    /// Registers a command handler.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="handler">The command handler.</param>
    void RegisterCommand(string commandId, Func<object?, Task> handler);

    /// <summary>
    /// Unregisters a command handler.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    void UnregisterCommand(string commandId);

    /// <summary>
    /// Checks if a command is registered.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <returns>True if the command is registered.</returns>
    bool IsCommandRegistered(string commandId);

    /// <summary>
    /// Gets a list of all registered command IDs.
    /// </summary>
    /// <returns>A list of command IDs.</returns>
    IReadOnlyList<string> GetRegisteredCommands();
}
