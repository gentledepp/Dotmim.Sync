using Microsoft.SqlServer.Server;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Dotmim.Sync.Tests
{
    internal static class DatabaseExtensions
    {
        internal static Task EnsureCreatedAsync(this Database db)
        {
            EnsureCreated(db);
            return Task.CompletedTask;
        }

        internal static void EnsureCreated(this Database db)
        {
            // For SQLite, we need to use raw SQL to create the schema
            // because EF6 Code First doesn't properly create SQLite databases
            if (db.Connection.GetType().Name.Contains("SQLite"))
            {
                var wasOpen = db.Connection.State == System.Data.ConnectionState.Open;

                try
                {
                    // Ensure connection is open
                    if (!wasOpen)
                        db.Connection.Open();

                    // Check if tables already exist
                    var cmd = db.Connection.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Address'";
                    var tableExists = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                    if (!tableExists)
                    {
                        // Find the SQL script file
                        var scriptPath = Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "..", "..", "..", "..", "..",
                            "CreateSqliteAdventureWorks.sql");

                        scriptPath = Path.GetFullPath(scriptPath);

                        if (File.Exists(scriptPath))
                        {
                            var sqlScript = File.ReadAllText(scriptPath);

                            // Split the SQL script into individual commands
                            // Remove comments and split by semicolon
                            var commands = SqliteSqlParser.SplitIntoCommands(sqlScript);


                            // Execute each command separately
                            foreach (var commandText in commands)
                            {
                                if (!string.IsNullOrWhiteSpace(commandText))
                                {
                                    var sqlCmd = db.Connection.CreateCommand();
                                    sqlCmd.CommandText = commandText;
                                    sqlCmd.ExecuteNonQuery();
                                }
                            }
                        }
                        else
                        {
                            throw new FileNotFoundException($"CreateSqliteAdventureWorks.sql not found at {scriptPath}");
                        }
                    }
                }
                finally
                {
                    // Restore connection state
                    if (!wasOpen && db.Connection.State == System.Data.ConnectionState.Open)
                        db.Connection.Close();
                }
            }
            else
            {
                // For SQL Server, use the normal EF6 Code First approach
                db.CreateIfNotExists();
            }
        }

        // Extension methods to provide EF Core-like API for EF6
        internal static void OpenConnection(this Database db)
        {
            db.Connection.Open();
        }

        internal static void CloseConnection(this Database db)
        {
            db.Connection.Close();
        }

        internal static int ExecuteSqlRaw(this Database db, string sql)
        {
            return db.ExecuteSqlCommand(sql);
        }

        internal static Task<int> ExecuteSqlRawAsync(this Database db, string sql)
        {
            return db.ExecuteSqlCommandAsync(sql);
        }

        internal static T Add<T>(this DbContext ctx, T entity) where T : class
        {
            return ctx.Set<T>().Add(entity);
        }
    }

    /// <summary>
    /// Provides functionality to parse SQLite SQL files and split them into individual commands.
    /// Handles single-line comments, multi-line comments, and semicolon-delimited statements.
    /// </summary>
    public class SqliteSqlParser
    {
        /// <summary>
        /// Splits a SQLite SQL file content into individual executable commands.
        /// Removes full-line comments (lines starting with --) and handles string literals correctly.
        /// </summary>
        /// <param name="sqlContent">The complete SQL file content.</param>
        /// <returns>Array of individual SQL commands without comments.</returns>
        public static string[] SplitIntoCommands(string sqlContent)
        {
            // Remove full-line comments (lines that start with -- after trimming)
            var lines = sqlContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var cleanedLines = lines
                .Where(line => !line.TrimStart().StartsWith("--"))
                .Select(line => RemoveInlineComments(line));

            var cleanedContent = string.Join("\n", cleanedLines);

            // Split by semicolons that are not inside string literals
            var commands = SplitBySemicolon(cleanedContent);

            return commands
                .Select(cmd => cmd.Trim())
                .Where(cmd => !string.IsNullOrWhiteSpace(cmd))
                .ToArray();
        }

        /// <summary>
        /// Removes inline comments (-- comments at the end of lines) while preserving string literals.
        /// </summary>
        /// <param name="line">The line to process.</param>
        /// <returns>The line with inline comments removed.</returns>
        private static string RemoveInlineComments(string line)
        {
            bool inString = false;
            char stringChar = '\0';

            for (int i = 0; i < line.Length - 1; i++)
            {
                char current = line[i];
                char next = line[i + 1];

                // Handle string literal boundaries
                if ((current == '\'' || current == '"') && (i == 0 || line[i - 1] != '\\'))
                {
                    if (!inString)
                    {
                        inString = true;
                        stringChar = current;
                    }
                    else if (current == stringChar)
                    {
                        inString = false;
                    }
                }

                // Found comment start outside of string
                if (!inString && current == '-' && next == '-')
                {
                    return line.Substring(0, i).TrimEnd();
                }
            }

            return line;
        }

        /// <summary>
        /// Splits SQL content by semicolons while respecting string literals.
        /// Semicolons inside string literals are not treated as statement terminators.
        /// </summary>
        /// <param name="sql">The SQL content to split.</param>
        /// <returns>List of individual SQL statements.</returns>
        private static List<string> SplitBySemicolon(string sql)
        {
            var commands = new List<string>();
            var currentCommand = new System.Text.StringBuilder();
            bool inString = false;
            char stringChar = '\0';

            for (int i = 0; i < sql.Length; i++)
            {
                char current = sql[i];

                // Handle string literal boundaries
                if ((current == '\'' || current == '"') && (i == 0 || sql[i - 1] != '\\'))
                {
                    if (!inString)
                    {
                        inString = true;
                        stringChar = current;
                    }
                    else if (current == stringChar)
                    {
                        inString = false;
                    }
                    currentCommand.Append(current);
                }
                // Found statement terminator outside of string
                else if (!inString && current == ';')
                {
                    commands.Add(currentCommand.ToString());
                    currentCommand.Clear();
                }
                else
                {
                    currentCommand.Append(current);
                }
            }

            // Add any remaining content
            if (currentCommand.Length > 0)
            {
                commands.Add(currentCommand.ToString());
            }

            return commands;
        }
    }
}
