namespace FileRedact.Core.Model;

/// <summary>
/// Categories of personally identifiable information. The set follows the CJIS Security Policy
/// definition of PII (section 4.3): information that can be used to distinguish or trace an
/// individual's identity, alone or in combination with other information linked to that individual.
/// </summary>
public enum PiiCategory
{
    Name,
    DateOfBirth,
    Date,
    SocialSecurityNumber,
    DriverLicense,
    PassportNumber,
    Address,
    PhoneNumber,
    EmailAddress,
    FinancialAccount,
    VehicleIdentifier,
    CriminalJusticeIdentifier,
    PlaceOfBirth,
    MaidenName,
    IpAddress,
    CustomTerm,
    Manual,
}

public static class PiiCategoryInfo
{
    public static string DisplayName(this PiiCategory c) => c switch
    {
        PiiCategory.Name => "Name",
        PiiCategory.DateOfBirth => "Date of birth",
        PiiCategory.Date => "Other date",
        PiiCategory.SocialSecurityNumber => "Social Security number",
        PiiCategory.DriverLicense => "Driver's license / state ID",
        PiiCategory.PassportNumber => "Passport number",
        PiiCategory.Address => "Address",
        PiiCategory.PhoneNumber => "Phone number",
        PiiCategory.EmailAddress => "Email address",
        PiiCategory.FinancialAccount => "Financial account",
        PiiCategory.VehicleIdentifier => "Vehicle ID / plate",
        PiiCategory.CriminalJusticeIdentifier => "Criminal justice ID (FBI/SID/booking)",
        PiiCategory.PlaceOfBirth => "Place of birth",
        PiiCategory.MaidenName => "Mother's maiden name",
        PiiCategory.IpAddress => "IP address",
        PiiCategory.CustomTerm => "Custom term",
        PiiCategory.Manual => "Manual selection",
        _ => c.ToString(),
    };

    /// <summary>
    /// Whether a finding of this category should be pre-selected for redaction. Categories where
    /// the detector has meaningful false-positive rates (generic dates, IP addresses) are shown but
    /// left for the reviewer to opt in.
    /// </summary>
    public static bool AcceptedByDefault(this PiiCategory c) => c switch
    {
        PiiCategory.Date => false,
        PiiCategory.IpAddress => false,
        _ => true,
    };

    public static IReadOnlyList<PiiCategory> All { get; } = Enum.GetValues<PiiCategory>();
}
