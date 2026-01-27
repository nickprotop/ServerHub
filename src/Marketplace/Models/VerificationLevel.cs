namespace ServerHub.Marketplace.Models;

/// <summary>
/// Represents the verification status of a marketplace widget
/// </summary>
public enum VerificationLevel
{
    /// <summary>
    /// Code has been reviewed and approved by ServerHub maintainers
    /// </summary>
    Verified,

    /// <summary>
    /// Multiple successful installations with no reported issues, but not fully reviewed
    /// </summary>
    Community,

    /// <summary>
    /// New or untested widget, requires user review before installation
    /// </summary>
    Unverified
}
