namespace AudiobookManager.Api.Dtos;

public record SystemInfoDto(string Version, string? CommitHash, string DotNetVersion);
