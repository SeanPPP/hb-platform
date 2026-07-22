using Microsoft.Data.SqlClient;

namespace BlazorApp.Api.Services.React;

internal static class AttendancePunchPersistenceException
{
    internal static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
            if (current.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException"
                && current.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
