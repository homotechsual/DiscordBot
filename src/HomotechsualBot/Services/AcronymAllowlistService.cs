using System.Collections.ObjectModel;
using System.Data;
using System.Text.RegularExpressions;
using DiscordBot.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public sealed partial class AcronymAllowlistService
{
    private static readonly SeedAcronym[] SeedAcronyms =
    [
        new("AFK", "en"), new("ASAP", "en"), new("BBL", "en"), new("BRB", "en"), new("BTW", "en"),
        new("CMIIW", "en"), new("EOD", "en"), new("ETA", "en"), new("FOMO", "en"), new("FTW", "en"),
        new("FWIW", "en"), new("FYI", "en"), new("GG", "en"), new("GLHF", "en"), new("GTG", "en"),
        new("HBU", "en"), new("IDC", "en"), new("IDK", "en"), new("IKR", "en"), new("ILY", "en"),
        new("IMO", "en"), new("IMHO", "en"), new("IRL", "en"), new("IYKYK", "en"), new("JK", "en"),
        new("LMAO", "en"), new("LMFAO", "en"), new("LOL", "en"), new("NBD", "en"), new("NGL", "en"),
        new("NP", "en"), new("NSFW", "en"), new("OG", "en"), new("OMW", "en"), new("OOTD", "en"),
        new("OP", "en"), new("POV", "en"), new("ROFL", "en"), new("SMH", "en"), new("TBH", "en"),
        new("TBF", "en"), new("TBT", "en"), new("TIL", "en"), new("TLDR", "en"), new("TIA", "en"),
        new("TY", "en"), new("WIP", "en"), new("WTF", "en"), new("WTH", "en"), new("YMMV", "en"),
        new("BBC", "en"), new("NHS", "en"), new("NASA", "en"), new("UNESCO", "en"), new("WHO", "en"),
        new("UK", "en"), new("US", "en"), new("EU", "en"), new("UN", "en"), new("FBI", "en"),
        new("USA", "en"), new("CIA", "en"), new("IRS", "en"), new("CDC", "en"), new("FDA", "en"),
        new("EPA", "en"),
        new("BBC", "cy"), new("BWC", "cy"), new("CCC", "cy"), new("CGA", "cy"), new("CIT", "cy"),
        new("CLLC", "cy"), new("CNC", "cy"), new("CWTCH", "cy"), new("EWC", "cy"), new("HWB", "cy"),
        new("NHS", "cy"), new("NPTC", "cy"), new("S4C", "cy"), new("URDD", "cy"), new("WJEC", "cy"),
        new("WLGA", "cy"), new("SSIW", "cy"), new("GPC", "cy"), new("BBC CYMRU", "cy"), new("AS", "cy"),
        new("GIG", "cy"), new("DU", "cy"), new("UD", "cy"), new("PLAID", "cy"), new("CYM", "cy"),
        new("ACL", "any"), new("AES", "any"), new("AI", "any"), new("API", "any"), new("ARP", "any"),
        new("ASCII", "any"), new("AWS", "any"), new("BGP", "any"), new("BIOS", "any"), new("CDN", "any"),
        new("CI", "any"), new("CIAM", "any"), new("CLI", "any"), new("CPU", "any"), new("CRUD", "any"),
        new("CSS", "any"), new("CSV", "any"), new("DB", "any"), new("DDoS", "any"), new("DHCP", "any"),
        new("DNS", "any"), new("DOM", "any"), new("DOS", "any"), new("DR", "any"), new("DRY", "any"),
        new("DVD", "any"), new("EC2", "any"), new("E2E", "any"), new("ETL", "any"), new("FTP", "any"),
        new("GPU", "any"), new("GUI", "any"), new("HTML", "any"), new("HTTP", "any"), new("HTTPS", "any"),
        new("IAC", "any"), new("IAM", "any"), new("ICMP", "any"), new("IDE", "any"), new("IIS", "any"),
        new("IMAP", "any"), new("IP", "any"), new("IPV4", "any"), new("IPV6", "any"), new("ISO", "any"),
        new("JSON", "any"), new("JWT", "any"), new("KISS", "any"), new("KPI", "any"), new("KQL", "any"),
        new("LAN", "any"), new("LDAP", "any"), new("LLM", "any"), new("LTS", "any"), new("MFA", "any"),
        new("MQTT", "any"), new("MCP", "any"), new("NAT", "any"), new("NLP", "any"), new("NoSQL", "any"),
        new("NTP", "any"), new("OCR", "any"), new("OIDC", "any"), new("OOP", "any"), new("OS", "any"),
        new("OTP", "any"), new("PDF", "any"), new("PHP", "any"), new("PKI", "any"), new("POP3", "any"),
        new("POC", "any"), new("PWA", "any"), new("QA", "any"), new("RAG", "any"), new("RAM", "any"),
        new("RDP", "any"), new("REST", "any"), new("RFC", "any"), new("RGB", "any"), new("RPC", "any"),
        new("RSA", "any"), new("SDK", "any"), new("SLA", "any"), new("SLO", "any"), new("SMTP", "any"),
        new("SOA", "any"), new("SOC", "any"), new("SQL", "any"), new("SSH", "any"), new("SSL", "any"),
        new("SSO", "any"), new("TCP", "any"), new("TDD", "any"), new("TLS", "any"), new("UDP", "any"),
        new("UI", "any"), new("UID", "any"), new("URI", "any"), new("URL", "any"), new("USB", "any"),
        new("UX", "any"), new("UUID", "any"), new("VM", "any"), new("VPN", "any"), new("WAN", "any"),
        new("WAF", "any"), new("XML", "any"), new("XSS", "any"), new("YAML", "any")
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AcronymAllowlistService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private HashSet<string> _cachedNormalizedAcronyms = new(StringComparer.Ordinal);
    private DateTimeOffset _lastRefreshUtc = DateTimeOffset.MinValue;

    public AcronymAllowlistService(IServiceScopeFactory scopeFactory, ILogger<AcronymAllowlistService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<bool> IsAllowlistedMessageAsync(string content)
    {
        var tokens = TokenPattern().Matches(content)
            .Select(match => Normalize(match.Value))
            .Where(token => token.Length > 0)
            .ToArray();

        if (tokens.Length == 0)
        {
            return false;
        }

        await EnsureCacheFreshAsync();

        foreach (var token in tokens)
        {
            if (!_cachedNormalizedAcronyms.Contains(token))
            {
                return false;
            }
        }

        return true;
    }

    public async Task<IReadOnlyList<AllowedAcronymEntry>> ListAsync(int take = 100, string? language = null, string? search = null, bool includeDisabled = false)
    {
        await EnsureSchemaAndSeedAsync();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        var normalizedLanguage = NormalizeLanguageForFilter(language);
        var normalizedSearch = Normalize(search ?? string.Empty);

        cmd.CommandText = @"
SELECT Acronym, Language, IsEnabled, CreatedAt
FROM AllowedAcronyms
WHERE ($includeDisabled = 1 OR IsEnabled = 1)
  AND ($language IS NULL OR Language = $language)
  AND ($search = '' OR NormalizedAcronym LIKE $searchPattern)
ORDER BY Acronym ASC
LIMIT $take;";

        AddParam(cmd, "$take", Math.Max(1, take));
        AddParam(cmd, "$includeDisabled", includeDisabled ? 1 : 0);
        AddParam(cmd, "$language", (object?)normalizedLanguage ?? DBNull.Value);
        AddParam(cmd, "$search", normalizedSearch);
        AddParam(cmd, "$searchPattern", $"%{normalizedSearch}%");

        var rows = new List<AllowedAcronymEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AllowedAcronymEntry(
                Acronym: reader.GetString(0),
                Language: reader.GetString(1),
                IsEnabled: reader.GetBoolean(2),
                CreatedAt: DateTimeOffset.TryParse(reader.GetString(3), out var createdAt)
                    ? createdAt
                    : DateTimeOffset.MinValue));
        }

        return new ReadOnlyCollection<AllowedAcronymEntry>(rows);
    }

    public async Task<AllowlistMutationResult> AddOrEnableAsync(string acronym, string language, ulong createdByUserId)
    {
        var normalized = Normalize(acronym);
        if (normalized.Length == 0)
        {
            return new AllowlistMutationResult(false, "Acronym must contain at least one letter or number.");
        }

        language = NormalizeLanguage(language);

        await EnsureSchemaAndSeedAsync();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO AllowedAcronyms (Acronym, NormalizedAcronym, Language, IsEnabled, CreatedByUserId)
VALUES ($acronym, $normalized, $language, 1, $createdBy)
ON CONFLICT(NormalizedAcronym) DO UPDATE SET
    Acronym = excluded.Acronym,
    Language = excluded.Language,
    IsEnabled = 1;";

        AddParam(cmd, "$acronym", acronym.Trim().ToUpperInvariant());
        AddParam(cmd, "$normalized", normalized);
        AddParam(cmd, "$language", language);
        AddParam(cmd, "$createdBy", (long)createdByUserId);

        await cmd.ExecuteNonQueryAsync();
        await EnsureCacheFreshAsync(force: true);

        return new AllowlistMutationResult(true, $"Acronym '{normalized}' is now allowlisted.");
    }

    public async Task<AllowlistMutationResult> DisableAsync(string acronym)
    {
        var normalized = Normalize(acronym);
        if (normalized.Length == 0)
        {
            return new AllowlistMutationResult(false, "Acronym must contain at least one letter or number.");
        }

        await EnsureSchemaAndSeedAsync();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE AllowedAcronyms
SET IsEnabled = 0
WHERE NormalizedAcronym = $normalized;";

        AddParam(cmd, "$normalized", normalized);
        var affected = await cmd.ExecuteNonQueryAsync();

        if (affected == 0)
        {
            return new AllowlistMutationResult(false, $"Acronym '{normalized}' is not in the allowlist.");
        }

        await EnsureCacheFreshAsync(force: true);
        return new AllowlistMutationResult(true, $"Acronym '{normalized}' has been disabled.");
    }

    private async Task EnsureCacheFreshAsync(bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - _lastRefreshUtc < TimeSpan.FromMinutes(5))
        {
            return;
        }

        await _refreshLock.WaitAsync();
        try
        {
            if (!force && DateTimeOffset.UtcNow - _lastRefreshUtc < TimeSpan.FromMinutes(5))
            {
                return;
            }

            await EnsureSchemaAndSeedAsync();

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT NormalizedAcronym
FROM AllowedAcronyms
WHERE IsEnabled = 1;";

            var loaded = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                loaded.Add(reader.GetString(0));
            }

            _cachedNormalizedAcronyms = loaded;
            _lastRefreshUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Acronym allowlist cache refresh failed; keeping previous cache");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task EnsureSchemaAndSeedAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomotechsualBotContext>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        await using (var createCmd = conn.CreateCommand())
        {
            createCmd.CommandText = @"
CREATE TABLE IF NOT EXISTS AllowedAcronyms (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Acronym TEXT NOT NULL,
    NormalizedAcronym TEXT NOT NULL UNIQUE,
    Language TEXT NOT NULL DEFAULT 'any',
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CreatedByUserId INTEGER NULL
);";
            await createCmd.ExecuteNonQueryAsync();
        }

        foreach (var seed in SeedAcronyms)
        {
            await using var seedCmd = conn.CreateCommand();
            seedCmd.CommandText = @"
INSERT OR IGNORE INTO AllowedAcronyms (Acronym, NormalizedAcronym, Language, IsEnabled)
VALUES ($acronym, $normalized, $language, 1);";

            AddParam(seedCmd, "$acronym", seed.Acronym);
            AddParam(seedCmd, "$normalized", Normalize(seed.Acronym));
            AddParam(seedCmd, "$language", seed.Language);
            await seedCmd.ExecuteNonQueryAsync();
        }
    }

    private static void AddParam(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().ToUpperInvariant();
        return NonAlphaNumericPattern().Replace(trimmed, string.Empty);
    }

    private static string NormalizeLanguage(string? language)
    {
        var normalized = string.IsNullOrWhiteSpace(language) ? "any" : language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "any" or "en" or "cy" => normalized,
            _ => "any"
        };
    }

    private static string? NormalizeLanguageForFilter(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "all" => null,
            "any" or "en" or "cy" => normalized,
            _ => null
        };
    }

    [GeneratedRegex("[\\p{L}\\p{N}]+")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("[^\\p{L}\\p{N}]+")]
    private static partial Regex NonAlphaNumericPattern();
}

public readonly record struct AllowedAcronymEntry(string Acronym, string Language, bool IsEnabled, DateTimeOffset CreatedAt);
public readonly record struct AllowlistMutationResult(bool Success, string Message);
public readonly record struct SeedAcronym(string Acronym, string Language);
