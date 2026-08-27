namespace BlazorApp.Api.Data.SchemaMigrations;

internal enum SchemaCommandMode
{
    Server,
    Check,
    Migrate,
    Invalid,
}

internal sealed record SchemaCommand(SchemaCommandMode Mode, string? Error)
{
    private const string CheckArgument = "--schema=check";
    private const string MigrateArgument = "--schema=migrate";

    public static SchemaCommand Parse(IEnumerable<string> args)
    {
        var schemaArguments = args
            .Where(argument =>
                argument.StartsWith("--schema", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();

        if (schemaArguments.Length == 0)
        {
            return new SchemaCommand(SchemaCommandMode.Server, null);
        }

        // schema 参数必须唯一且精确，防止拼写错误时误启动 HTTP 服务。
        if (schemaArguments.Length != 1)
        {
            return Invalid("SCHEMA_COMMAND_DUPLICATE");
        }

        return schemaArguments[0] switch
        {
            CheckArgument => new SchemaCommand(SchemaCommandMode.Check, null),
            MigrateArgument => new SchemaCommand(SchemaCommandMode.Migrate, null),
            _ => Invalid("SCHEMA_COMMAND_INVALID"),
        };
    }

    private static SchemaCommand Invalid(string diagnosticCode) =>
        new(SchemaCommandMode.Invalid, diagnosticCode);
}
