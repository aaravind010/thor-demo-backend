namespace Thor.Api.Constants;

/// <summary>Bounds for the <c>limit</c> query parameter on keyset-paged list endpoints.</summary>
public static class PagingConstants
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 500;

    public const string InvalidLimitMessage = "limit must be between 1 and 500.";
}
