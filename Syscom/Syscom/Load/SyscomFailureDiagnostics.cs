namespace Syscom.Load;

internal enum SyscomFailureReason
{
    MissingProducts,
    UnexpectedPage,
    InvalidPageCount,
    EmptyPage,
    PageCountChanged,
    ProductTotalChanged,
    ProductCountExceeded,
    ProductCountMismatch,
    ModelCodeCollision,
    CodeSchemaMigrationRequired,
    LegacyCodeIdentityAmbiguous,
    DownloadIncomplete,
    DuplicateExchangeRates,
    InvalidPriceCandidates
}

// Only application-owned reasons and numeric context may cross the logging boundary.
internal static class SyscomFailureDiagnostics
{
    private static readonly object DiagnosticKey = new();
    private sealed record Diagnostic(SyscomFailureReason Reason, long? Category, int? RequestedPage,
        int? ReturnedPage, int? ExpectedPages, int? ReturnedPages, long? ExpectedCount, long? ActualCount, long? ExistingProductId, long? IncomingProductId, long? ExistingCategory, long? InvalidCount);

    internal static InvalidOperationException Validation(SyscomFailureReason reason,
        long? category = null, int? requestedPage = null, int? returnedPage = null,
        int? expectedPages = null, int? returnedPages = null, long? expectedCount = null, long? actualCount = null,
        long? existingProductId = null, long? incomingProductId = null, long? existingCategory = null, long? invalidCount = null)
    {
        var error = new InvalidOperationException($"SYSCOM validation failed: {reason}.");
        error.Data[DiagnosticKey] = new Diagnostic(reason, category, requestedPage, returnedPage,
            expectedPages, returnedPages, expectedCount, actualCount, existingProductId, incomingProductId, existingCategory, invalidCount);
        return error;
    }

    internal static void AddPageContext(Exception error, long category, int page)
    {
        if (error.Data[DiagnosticKey] is Diagnostic detail)
            error.Data[DiagnosticKey] = detail with { Category = category, RequestedPage = page };
    }

    internal static string Describe(Exception error)
    {
        if (error.Data[DiagnosticKey] is Diagnostic detail)
            return $"{detail.Reason}; category={detail.Category}; requested_page={detail.RequestedPage}; " +
                $"returned_page={detail.ReturnedPage}; expected_pages={detail.ExpectedPages}; " +
                $"returned_pages={detail.ReturnedPages}; expected_count={detail.ExpectedCount}; actual_count={detail.ActualCount}; " +
                $"existing_product_id={detail.ExistingProductId}; incoming_product_id={detail.IncomingProductId}; existing_category={detail.ExistingCategory}; invalid_count={detail.InvalidCount}";
        // Never render Message, InnerException, Data, HTTP bodies or connection details.
        return error switch
        {
            HttpRequestException http => $"HTTP request failed; status={(int?)http.StatusCode}",
            System.Text.Json.JsonException => "Invalid JSON response",
            Npgsql.PostgresException pg => DescribePostgres(pg),
            System.Data.Common.DbException => "Database operation failed",
            OperationCanceledException => "Operation cancelled or timed out",
            _ => "Unexpected failure; inspect the exception in the debugger"
        };
    }
    private static string DescribePostgres(Npgsql.PostgresException error)
    {
        var state = error.SqlState;
        if (state.Length != 5 || state.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'Z')))
            return "Database operation failed";
        var reason = state switch
        {
            "21000" => "CardinalityViolation",
            "23505" => "UniqueViolation",
            "23503" => "ForeignKeyViolation",
            "23502" => "NotNullViolation",
            "22001" => "StringTooLong",
            "42P10" => "MissingConflictConstraint",
            _ => "DatabaseError"
        };
        return $"Database operation failed; sqlstate={state}; reason={reason}";
    }

}
