using AudiobookManager.Database.Models;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;

namespace AudiobookManager.Services;

/// <summary>
/// What every <see cref="IConsistencyIssueDetector"/> needs to check one audiobook, computed once
/// up front (a single ATL parse and directory lookup) and shared across every detector so each
/// one only has to compare, not re-derive.
/// </summary>
public sealed record AudiobookCheckContext(
    Audiobook Audiobook,
    DomainAudiobook Parsed,
    string DirectoryPath,
    string LibraryPath);
