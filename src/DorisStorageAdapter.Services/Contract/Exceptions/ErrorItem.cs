namespace DorisStorageAdapter.Services.Contract.Exceptions;

public record ErrorItem(
    string Message,
    string? Target = null);
