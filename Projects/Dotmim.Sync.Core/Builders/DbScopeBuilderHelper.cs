using System.Collections.Generic;
using System.Data;
using System.Text;

namespace Wormhole.Sync.Builders
{
    /// <summary>
    /// Helper class for building scope info client table with custom parameters.
    /// </summary>
    public static class DbScopeBuilderHelper
    {
        /// <summary>
        /// Generate SQL column definition for a ScopeInfoClientParameter (SQL Server syntax).
        /// </summary>
        public static string GetSqlServerColumnDefinition(ScopeInfoClientParameter parameter)
        {
            var sb = new StringBuilder();
            sb.Append($"[{parameter.Name}] ");

            sb.Append(parameter.DbType switch
            {
                DbType.Int64 => "BIGINT",
                DbType.Int32 => "INT",
                DbType.Int16 => "SMALLINT",
                DbType.Byte => "TINYINT",
                DbType.Boolean => "BIT",
                DbType.Guid => "UNIQUEIDENTIFIER",
                DbType.DateTime => "DATETIME",
                DbType.DateTime2 => "DATETIME2",
                DbType.Date => "DATE",
                DbType.DateTimeOffset => "DATETIMEOFFSET",
                DbType.Decimal or DbType.Currency => "DECIMAL(18, 2)",
                DbType.Double => "FLOAT",
                DbType.Single => "REAL",
                DbType.Binary => parameter.MaxLength > 0 && parameter.MaxLength <= 8000 ? $"VARBINARY({parameter.MaxLength})" : "VARBINARY(MAX)",
                DbType.Time => "TIME",
                DbType.String or DbType.StringFixedLength or DbType.AnsiString or DbType.AnsiStringFixedLength =>
                    parameter.MaxLength > 0 && parameter.MaxLength <= 4000 ? $"NVARCHAR({parameter.MaxLength})" : "NVARCHAR(MAX)",
                _ => "NVARCHAR(MAX)",
            });

            sb.Append(" NULL");
            return sb.ToString();
        }

        /// <summary>
        /// Generate SQL column definition for a ScopeInfoClientParameter (PostgreSQL syntax).
        /// </summary>
        public static string GetPostgreSqlColumnDefinition(ScopeInfoClientParameter parameter)
        {
            var sb = new StringBuilder();
            sb.Append($"\"{parameter.Name}\" ");

            sb.Append(parameter.DbType switch
            {
                DbType.Int64 => "BIGINT",
                DbType.Int32 => "INTEGER",
                DbType.Int16 => "SMALLINT",
                DbType.Byte => "SMALLINT",
                DbType.Boolean => "BOOLEAN",
                DbType.Guid => "UUID",
                DbType.DateTime or DbType.DateTime2 => "TIMESTAMP",
                DbType.Date => "DATE",
                DbType.DateTimeOffset => "TIMESTAMP WITH TIME ZONE",
                DbType.Decimal or DbType.Currency => "NUMERIC(18, 2)",
                DbType.Double => "DOUBLE PRECISION",
                DbType.Single => "REAL",
                DbType.Binary => "BYTEA",
                DbType.Time => "TIME",
                DbType.String or DbType.StringFixedLength or DbType.AnsiString or DbType.AnsiStringFixedLength =>
                    parameter.MaxLength > 0 ? $"VARCHAR({parameter.MaxLength})" : "TEXT",
                _ => "TEXT",
            });

            sb.Append(" NULL");
            return sb.ToString();
        }

        /// <summary>
        /// Generate SQL column definition for a ScopeInfoClientParameter (MySQL/MariaDB syntax).
        /// </summary>
        public static string GetMySqlColumnDefinition(ScopeInfoClientParameter parameter)
        {
            var sb = new StringBuilder();
            sb.Append($"`{parameter.Name}` ");

            sb.Append(parameter.DbType switch
            {
                DbType.Int64 => "BIGINT",
                DbType.Int32 => "INT",
                DbType.Int16 => "SMALLINT",
                DbType.Byte => "TINYINT",
                DbType.Boolean => "TINYINT(1)",
                DbType.Guid => "VARCHAR(36)",
                DbType.DateTime or DbType.DateTime2 => "DATETIME",
                DbType.Date => "DATE",
                DbType.DateTimeOffset => "DATETIME",
                DbType.Decimal or DbType.Currency => "DECIMAL(18, 2)",
                DbType.Double => "DOUBLE",
                DbType.Single => "FLOAT",
                DbType.Binary => parameter.MaxLength > 0 && parameter.MaxLength <= 65535 ? $"VARBINARY({parameter.MaxLength})" : "LONGBLOB",
                DbType.Time => "TIME",
                DbType.String or DbType.StringFixedLength or DbType.AnsiString or DbType.AnsiStringFixedLength =>
                    parameter.MaxLength > 0 && parameter.MaxLength <= 16383 ? $"VARCHAR({parameter.MaxLength})" : "LONGTEXT",
                _ => "LONGTEXT",
            });

            sb.Append(" NULL");
            return sb.ToString();
        }

        /// <summary>
        /// Generate column list for custom parameters in SQL SELECT statements (SQL Server syntax with brackets).
        /// </summary>
        public static string GetColumnListForSelect(ScopeInfoClientParameters parameters, string prefix = "")
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            var columns = new List<string>();
            foreach (var param in parameters)
            {
                columns.Add($"{prefix}[{param.Name}]");
            }

            return ", " + string.Join(", ", columns);
        }

        /// <summary>
        /// Generate column list for custom parameters in SQL SELECT statements (PostgreSQL syntax with quotes).
        /// </summary>
        public static string GetColumnListForSelectPostgreSql(ScopeInfoClientParameters parameters, string prefix = "")
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            var columns = new List<string>();
            foreach (var param in parameters)
            {
                columns.Add($"{prefix}\"{param.Name}\"");
            }

            return ", " + string.Join(", ", columns);
        }

        /// <summary>
        /// Generate column list for custom parameters in SQL INSERT statements (SQL Server syntax with brackets).
        /// </summary>
        public static string GetColumnListForInsert(ScopeInfoClientParameters parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            var columns = new List<string>();
            foreach (var param in parameters)
            {
                columns.Add($"[{param.Name}]");
            }

            return ", " + string.Join(", ", columns);
        }

        /// <summary>
        /// Generate column list for custom parameters in SQL INSERT statements (PostgreSQL syntax with quotes).
        /// </summary>
        public static string GetColumnListForInsertPostgreSql(ScopeInfoClientParameters parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            var columns = new List<string>();
            foreach (var param in parameters)
            {
                columns.Add($"\"{param.Name}\"");
            }

            return ", " + string.Join(", ", columns);
        }

        /// <summary>
        /// Generate parameter list for custom parameters in SQL INSERT statements.
        /// </summary>
        public static string GetParameterListForInsert(ScopeInfoClientParameters parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            var paramNames = new List<string>();
            foreach (var param in parameters)
            {
                paramNames.Add($"@{param.Name}");
            }

            return ", " + string.Join(", ", paramNames);
        }

        /// <summary>
        /// Generate SET clause for custom parameters in SQL UPDATE statements.
        /// </summary>
        public static string GetSetClauseForUpdate(ScopeInfoClientParameters parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            var setClauses = new List<string>();
            foreach (var param in parameters)
            {
                setClauses.Add($"[{param.Name}] = @{param.Name}");
            }

            return ", " + string.Join(", ", setClauses);
        }

        /// <summary>
        /// Generate SET clause for MERGE statement (SQL Server).
        /// </summary>
        public static string GetSetClauseForMerge(ScopeInfoClientParameters parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            var setClauses = new List<string>();
            foreach (var param in parameters)
            {
                setClauses.Add($"[{param.Name}] = [changes].[{param.Name}]");
            }

            return ",\n                                   " + string.Join(",\n                                   ", setClauses);
        }

        /// <summary>
        /// Generate SELECT clause for MERGE USING statement (SQL Server).
        /// </summary>
        public static string GetSelectForMergeUsing(ScopeInfoClientParameters parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            var selectClauses = new List<string>();
            foreach (var param in parameters)
            {
                selectClauses.Add($"@{param.Name} AS {param.Name}");
            }

            return ",\n	                                   " + string.Join(",\n	                                   ", selectClauses);
        }

        /// <summary>
        /// Generate SELECT clause for PostgreSQL WITH changes AS SELECT statement.
        /// </summary>
        public static string GetSelectForPostgreSqlWith(ScopeInfoClientParameters parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            var selectClauses = new List<string>();
            foreach (var param in parameters)
            {
                selectClauses.Add($"@{param.Name} AS \"{param.Name}\"");
            }

            return ",\n                                                " + string.Join(",\n                                                ", selectClauses);
        }

        /// <summary>
        /// Generate UPDATE SET clause for PostgreSQL DO UPDATE (using EXCLUDED).
        /// </summary>
        public static string GetUpdateSetForPostgreSql(ScopeInfoClientParameters parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return string.Empty;

            var setClauses = new List<string>();
            foreach (var param in parameters)
            {
                setClauses.Add($"\"{param.Name}\" = EXCLUDED.\"{param.Name}\"");
            }

            return ",\n                                                " + string.Join(",\n                                                ", setClauses);
        }
    }
}
