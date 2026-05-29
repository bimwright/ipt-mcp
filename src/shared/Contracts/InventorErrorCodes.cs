namespace Bimwright.Inventor.Shared.Contracts;

/// <summary>
/// Frozen integration contract: canonical error codes returned in
/// <see cref="InventorError.Code"/> across the server and add-in.
/// </summary>
public static class InventorErrorCodes
{
    public const string NO_TARGET = "NO_TARGET";
    public const string TARGET_UNAVAILABLE = "TARGET_UNAVAILABLE";
    public const string NO_DOCUMENT = "NO_DOCUMENT";
    public const string WRONG_DOCUMENT_TYPE = "WRONG_DOCUMENT_TYPE";
    public const string INVALID_ARGUMENT = "INVALID_ARGUMENT";
    public const string UNSUPPORTED_HOST = "UNSUPPORTED_HOST";
    public const string API_ERROR = "API_ERROR";
    public const string TIMEOUT = "TIMEOUT";
    public const string RESPONSE_TOO_LARGE = "RESPONSE_TOO_LARGE";
    public const string READ_ONLY = "READ_ONLY";
    public const string SEND_CODE_DISABLED = "SEND_CODE_DISABLED";
    public const string UNAUTHORIZED = "UNAUTHORIZED";
}
