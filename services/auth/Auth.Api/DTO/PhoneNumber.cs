namespace Auth.Api.DTO;

// One rule for every DTO that accepts a phone number: optional leading +, then
// 7-15 digits (E.164 allows at most 15). Spaces and dashes are not accepted so
// the stored value is always safe to dial or pass to a tel: link.
public static class PhoneNumber
{
    public const string Pattern = @"^\+?[0-9]{7,15}$";
    public const string ErrorMessage = "Phone must be 7-15 digits, with an optional leading +.";
}
