using Npgsql;
using Thor.DataConnectionManager.Exceptions;

namespace Thor.DataConnectionManager.Validation;

public sealed class TenantConnectionValidator : ITenantConnectionValidator
{
    public async Task ValidateAsync(Guid tenantId, NpgsqlConnection connection, string expectedDatabase, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_database()";

        var actualDatabase = (string)(await command.ExecuteScalarAsync(cancellationToken))!;

        if (!string.Equals(actualDatabase, expectedDatabase, StringComparison.Ordinal))
        {
            throw new TenantConnectionMismatchException(tenantId, expectedDatabase, actualDatabase);
        }
    }
}
