namespace Avalon.Balance.Service.Export;

/// <summary>GitHub refused or could not be reached. The message names the step and the status only. Answers 502.</summary>
public sealed class ExportFailedException(string message, Exception? inner = null) : Exception(message, inner);
