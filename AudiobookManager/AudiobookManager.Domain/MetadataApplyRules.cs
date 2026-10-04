namespace AudiobookManager.Domain;

/// <summary>
/// What an interactive review (a single-book search or refresh, where the user confirms the
/// change) does with a field that differs from the source, i.e. whether its checkbox starts
/// ticked. The user can always override it - this only sets the default.
/// </summary>
public enum InteractiveApplyRule
{
    /// <summary>Ticked whenever the field differs. The behaviour before these rules existed.</summary>
    AlwaysSelect,

    /// <summary>Never ticked by default.</summary>
    NeverSelect,

    /// <summary>Ticked only when the book has no value and the source has one.</summary>
    SelectIfEmpty,

    /// <summary>Ticked only when the source has a value, so an empty source never proposes blanking.</summary>
    SelectIfSourceHasValue,
}

/// <summary>
/// What an automated run (bulk refresh, scheduled refresh) does with a field that differs from the
/// source. Only <see cref="AskMe"/> needs a person; every other rule settles the field by itself.
/// </summary>
public enum AutomatedApplyRule
{
    /// <summary>
    /// The field needs a decision: the book's whole changeset is held for review instead of being
    /// applied, and none of the other fields' automated rules take effect. The behaviour before
    /// these rules existed.
    /// </summary>
    AskMe,

    /// <summary>Apply the source's value whatever either side holds - including an empty source.</summary>
    AlwaysOverwrite,

    /// <summary>Apply the source's value only when the book has none and the source has one.</summary>
    FillBlanksOnly,

    /// <summary>Apply the source's value whenever it has one; an empty source never blanks the book.</summary>
    OverwriteUnlessSourceEmpty,

    /// <summary>Never apply the source's value, whatever the book holds.</summary>
    KeepCurrent,
}

/// <summary>One field's pair of rules.</summary>
public record FieldApplyRule(InteractiveApplyRule Interactive, AutomatedApplyRule Automated);
