namespace AudiobookManager.Api.Dtos;

public record OperationStatusDto(bool IsRunning, int Processed, int Total);
