using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Refactoring;

public sealed record CompiledIndexDdl(string IndexName, string Target, string Create, string Rollback);

public interface IIndexDdlValidator
{
    void Validate(string sql);
}

public sealed class ScriptDomIndexDdlValidator : IIndexDdlValidator
{
    public void Validate(string sql)
    {
        new TSql160Parser(true).Parse(new StringReader(sql), out var errors);
        if (errors.Count != 0) throw new InvalidOperationException("INDEX_DDL_SYNTAX: 生成的索引脚本未通过语法验证。");
    }
}

public static class IndexDdlCompiler
{
    public static string Generate(MissingIndexSuggestion suggestion, IndexDdlOptions options) => Compile(suggestion, options).Create;
    public static string GenerateRollback(MissingIndexSuggestion suggestion) => Compile(suggestion, new()).Rollback;

    public static CompiledIndexDdl Compile(MissingIndexSuggestion suggestion, IndexDdlOptions options,
        IIndexDdlValidator? validator = null, IUnexpectedErrorReporter? unexpectedErrors = null)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            if (suggestion.KeyColumns == null || suggestion.IncludeColumns == null)
                throw Invalid("INDEX_COLUMNS_MISSING");
            if (suggestion.KeyColumns.Count == 0)
            {
                Logger.Warning("IMP-15: INDEX_KEYS_MISSING；未生成创建或回滚脚本。");
                return new("", "", "", "");
            }
            var identity = ResolveTarget(suggestion);
            if (identity.Schema == null || identity.Object == null) throw Invalid("INDEX_TARGET_UNRESOLVED");
            string target = QualifyName(identity.Database, identity.Schema, identity.Object);
            var keys = ReadColumns(suggestion.KeyColumns, false);
            var includes = ReadColumns(suggestion.IncludeColumns, true);
            if (keys.Concat(includes).GroupBy(c => c.Name, StringComparer.Ordinal).Any(g => g.Count() > 1))
                throw Invalid("INDEX_DUPLICATE_COLUMN");

            string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new { Version = 1, Target = identity, Keys = keys, Includes = includes }))));
            string prefix = new(identity.Object.Where(c => char.IsLetterOrDigit(c) || c == '_').Take(24).ToArray());
            string indexName = string.IsNullOrEmpty(options.IndexName) ? $"IX_{prefix}_{fingerprint}" : options.IndexName;
            QuoteIdentifier(indexName);
            string compression = options.DataCompression?.ToUpperInvariant() ?? "";
            if (compression is not ("" or "NONE" or "ROW" or "PAGE")) throw Invalid("INDEX_COMPRESSION_INVALID");
            if (options.MaxDop is < 0 or > 64) throw Invalid("INDEX_MAXDOP_INVALID");
            var settings = new List<string> { options.Online ? "ONLINE = ON" : "ONLINE = OFF" };
            if (compression.Length != 0) settings.Add("DATA_COMPRESSION = " + compression);
            settings.Add(options.SortInTempDb ? "SORT_IN_TEMPDB = ON" : "SORT_IN_TEMPDB = OFF");
            if (options.MaxDop.HasValue) settings.Add("MAXDOP = " + options.MaxDop.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

            string precondition = "";
            if (identity.Database == null)
                precondition = "-- INDEX_DATABASE_UNSPECIFIED: 请在已核验的目标数据库中执行。\n";
            if (identity.Server != null)
            {
                QuoteIdentifier(identity.Server);
                precondition += "IF ISNULL(CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')), N'') COLLATE Latin1_General_100_BIN2 <> N'"
                    + identity.Server.Replace("'", "''", StringComparison.Ordinal) + "' COLLATE Latin1_General_100_BIN2\n"
                    + "    THROW 50001, 'INDEX_TARGET_SERVER_MISMATCH', 1;\n";
            }
            string includeClause = includes.Length == 0 ? "" : "\nINCLUDE (" + string.Join(", ", includes.Select(c => QuoteIdentifier(c.Name))) + ")";
            string create = precondition + $"CREATE NONCLUSTERED INDEX {QuoteIdentifier(indexName)}\nON {target} ("
                + string.Join(", ", keys.Select(c => QuoteIdentifier(c.Name))) + ")" + includeClause
                + "\nWITH (" + string.Join(", ", settings) + ");";
            string rollback = precondition + $"DROP INDEX {QuoteIdentifier(indexName)} ON {target};";
            validator ??= new ScriptDomIndexDdlValidator();
            validator.Validate(create);
            validator.Validate(rollback);
            Logger.Debug($"IMP-15: 索引脚本语法验证通过；键列 {keys.Length}，包含列 {includes.Length}；既有索引目录未核验。");
            return new(indexName, target, create, rollback);
        }
        catch (Exception exception)
        {
            ExceptionPolicy.Describe(exception, "IndexDdlCompiler.Compile", unexpectedErrors);
            throw;
        }
    }

    // Compatibility fields contain a single raw or delimited name, never a multipart path.
    public static SqlObjectIdentity ResolveTarget(MissingIndexSuggestion suggestion)
    {
        string? Resolve(string? authoritative, string? legacy)
        {
            if (authoritative != null && authoritative == legacy) return authoritative;
            string? decoded = string.IsNullOrEmpty(legacy) ? null : DecodeName(legacy);
            if (authoritative != null && decoded != null && authoritative != decoded)
                throw Invalid("INDEX_TARGET_CONFLICT");
            return authoritative ?? decoded;
        }
        var identity = suggestion.ObjectIdentity;
        return new(Resolve(identity?.Server, suggestion.Server), Resolve(identity?.Database, suggestion.Database),
            Resolve(identity?.Schema, suggestion.Schema), Resolve(identity?.Object, suggestion.Table));
    }

    private sealed record Column(string Name, string Usage);
    private static Column[] ReadColumns(IEnumerable<IndexColumn> columns, bool include) => columns.Select(column =>
    {
        if (column == null) throw Invalid("INDEX_COLUMN_INVALID");
        string usage = column.Usage ?? "";
        if (include ? usage is not ("" or "INCLUDE") : usage is not ("" or "EQUALITY" or "INEQUALITY"))
            throw Invalid("INDEX_COLUMN_ROLE_INVALID");
        string name = DecodeName(column.Name);
        QuoteIdentifier(name);
        return new Column(name, usage.Length == 0 ? include ? "INCLUDE" : "KEY" : usage);
    }).ToArray();

    public static string QuoteIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 128 || name.Contains('\0')) throw Invalid("INDEX_IDENTIFIER_INVALID");
        return SqlObjectIdentity.Quote(name);
    }

    public static string QualifyName(params string?[] parts)
    {
        var present = parts.SkipWhile(part => part == null).ToArray();
        if (present.Length == 0 || present.Any(part => part == null)) throw Invalid("INDEX_TARGET_UNRESOLVED");
        return string.Join(".", present.Select(part => QuoteIdentifier(part!)));
    }

    public static string EscapeName(string name) => QuoteIdentifier(DecodeName(name));

    public static string DecodeName(string name)
    {
        if (string.IsNullOrEmpty(name)) throw Invalid("INDEX_IDENTIFIER_INVALID");
        if (name[0] is '[' or '"')
        {
            char closing = name[0] == '[' ? ']' : '"';
            if (name.Length < 2 || name[^1] != closing) throw Invalid("INDEX_IDENTIFIER_INVALID");
            var value = new StringBuilder();
            for (int i = 1; i < name.Length - 1; i++)
            {
                if (name[i] == closing && (i + 1 >= name.Length - 1 || name[++i] != closing))
                    throw Invalid("INDEX_IDENTIFIER_INVALID");
                value.Append(name[i]);
            }
            return value.ToString();
        }
        return name;
    }

    private static InvalidDataException Invalid(string code) => new(code + ": 索引目标、列或选项无效；未生成可执行脚本。");
}
