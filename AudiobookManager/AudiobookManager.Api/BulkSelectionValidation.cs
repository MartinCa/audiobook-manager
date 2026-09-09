using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api;

/// <summary>
/// The selection guard every bulk/selected-books endpoint shares (bulk edit + preview, selected
/// metadata refresh, selected consistency check). Beyond
/// <see cref="MaxSelection"/> ids a request stops being a deliberate selection and is more
/// likely a mis-addressed sweep - and each selected book is a full tag write plus a consistency
/// recheck, so an unbounded list is a foot-gun.
/// </summary>
public static class BulkSelectionValidation
{
    /// <summary>The largest selection the bulk endpoints accept; beyond it a request stops being an explicit selection.</summary>
    public const int MaxSelection = 100;

    /// <summary>InvalidRequest problem when the id list is empty, over the cap, or has duplicates; null when usable.</summary>
    public static ObjectResult? ValidateBulkSelection(this ControllerBase controller, List<long>? audiobookIds)
    {
        if (audiobookIds == null || audiobookIds.Count == 0)
        {
            return controller.InvalidRequest("At least one audiobook must be selected.");
        }

        if (audiobookIds.Count > MaxSelection)
        {
            return controller.InvalidRequest($"No more than {MaxSelection} audiobooks can be selected at once.");
        }

        if (new HashSet<long>(audiobookIds).Count != audiobookIds.Count)
        {
            return controller.InvalidRequest("AudiobookIds must not contain duplicates.");
        }

        return null;
    }
}