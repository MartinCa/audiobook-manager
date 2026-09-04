namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One page of consistency issues plus the total number matching the filter, so the client can
/// size its pager without asking again.
///
/// Paged because the unpaged endpoint this replaces returned every issue with every field inline,
/// and <c>SidecarFilesDetector</c> stores whole generated metadata.opf documents and whole
/// description bodies in ExpectedValue/ActualValue. A library with a few thousand issues - the
/// client's own comment records groups of ~3,700 - made that a multi-megabyte response, parsed and
/// then held for the session, to render fifty rows.
/// </summary>
public record ConsistencyIssuePageDto(
    List<ConsistencyIssueDto> Items,
    int TotalCount
);
