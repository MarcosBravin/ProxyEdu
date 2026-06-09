using ProxyEdu.Shared.Models;
using System.Text.RegularExpressions;

namespace ProxyEdu.Server.Services;

public class FilterService
{
    private readonly DatabaseService _db;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, (StudentInfo? Student, DateTime ExpiresAtUtc)> _studentCache = new(StringComparer.OrdinalIgnoreCase);
    private RuleSnapshot? _cachedSnapshot;

    public FilterService(DatabaseService db)
    {
        _db = db;
    }

    public bool IsUrlAllowed(string url, string studentIp)
    {
        return EvaluateUrl(url, studentIp).IsAllowed;
    }

    public FilterDecision EvaluateUrl(string url, string studentIp)
    {
        var normalizedStudentIp = IpAddressNormalizer.Normalize(studentIp);
        var snapshot = GetOrBuildSnapshot();
        var student = ResolveStudentCached(normalizedStudentIp);
        var safeUrl = url ?? string.Empty;
        var normalizedUrl = safeUrl.ToLowerInvariant();
        var normalizedDomain = ExtractDomain(safeUrl).ToLowerInvariant();

        var whitelistMatch = FindWhitelistMatch(snapshot, student, normalizedUrl, normalizedDomain);
        if (whitelistMatch != null)
        {
            return FilterDecision.Allowed(
                normalizedDomain,
                "liberado por regra explicita",
                whitelistMatch.Pattern,
                whitelistMatch.Scope,
                "allowlist");
        }

        var blacklistMatch = FindBlacklistMatch(snapshot, student, normalizedUrl, normalizedDomain);
        if (blacklistMatch != null)
        {
            var reason = string.IsNullOrWhiteSpace(blacklistMatch.Category)
                ? "bloqueado por dominio"
                : "bloqueado por categoria";

            return FilterDecision.Blocked(
                normalizedDomain,
                reason,
                blacklistMatch.Pattern,
                blacklistMatch.Scope,
                "blocklist");
        }

        // Mantém o comportamento atual de fallback em whitelist mode.
        if (snapshot.WhitelistMode)
        {
            var hasWhitelistRules = snapshot.Global.HasWhitelist
                || (student != null &&
                    snapshot.ByStudentId.TryGetValue(student.Id, out var studentRules) &&
                    studentRules.HasWhitelist)
                || (!string.IsNullOrWhiteSpace(student?.Group) &&
                    snapshot.ByGroup.TryGetValue(student.Group, out var groupRules) &&
                    groupRules.HasWhitelist);

            if (hasWhitelistRules)
            {
                return FilterDecision.Blocked(
                    normalizedDomain,
                    "bloqueado por politica padrao",
                    null,
                    "policy",
                    "allowlist-default");
            }

            return FilterDecision.Allowed(
                normalizedDomain,
                "liberado por politica padrao",
                null,
                "policy",
                "allowlist-empty");
        }

        return FilterDecision.Allowed(
            normalizedDomain,
            "liberado por politica padrao",
            null,
            "policy",
            "blocklist-default");
    }

    private static RuleMatch? FindWhitelistMatch(RuleSnapshot snapshot, StudentInfo? student, string normalizedUrl, string normalizedDomain)
    {
        var match = FindMatch(snapshot.Global.Whitelist, normalizedUrl, normalizedDomain, "global");
        if (match != null)
        {
            return match;
        }

        if (student != null &&
            snapshot.ByStudentId.TryGetValue(student.Id, out var studentRules) &&
            (match = FindMatch(studentRules.Whitelist, normalizedUrl, normalizedDomain, "student")) != null)
        {
            return match;
        }

        if (!string.IsNullOrWhiteSpace(student?.Group) &&
            snapshot.ByGroup.TryGetValue(student.Group, out var groupRules) &&
            (match = FindMatch(groupRules.Whitelist, normalizedUrl, normalizedDomain, "group")) != null)
        {
            return match;
        }

        return null;
    }

    private static RuleMatch? FindBlacklistMatch(RuleSnapshot snapshot, StudentInfo? student, string normalizedUrl, string normalizedDomain)
    {
        var match = FindMatch(snapshot.Global.Blacklist, normalizedUrl, normalizedDomain, "global");
        if (match != null)
        {
            return match;
        }

        if (student != null &&
            snapshot.ByStudentId.TryGetValue(student.Id, out var studentRules) &&
            (match = FindMatch(studentRules.Blacklist, normalizedUrl, normalizedDomain, "student")) != null)
        {
            return match;
        }

        if (!string.IsNullOrWhiteSpace(student?.Group) &&
            snapshot.ByGroup.TryGetValue(student.Group, out var groupRules) &&
            (match = FindMatch(groupRules.Blacklist, normalizedUrl, normalizedDomain, "group")) != null)
        {
            return match;
        }

        return null;
    }

    private static RuleMatch? FindMatch(List<PreparedRule> rules, string normalizedUrl, string normalizedDomain, string scope)
    {
        foreach (var rule in rules)
        {
            if (rule.Matches(normalizedUrl, normalizedDomain))
            {
                return new RuleMatch(rule.Pattern, scope, rule.Category);
            }
        }

        return null;
    }

    private RuleSnapshot GetOrBuildSnapshot()
    {
        lock (_cacheLock)
        {
            if (_cachedSnapshot != null)
            {
                return _cachedSnapshot;
            }

            var settings = _db.GetSettings();
            var activeRules = _db.FilterRules.Find(r => r.IsActive).ToList();
            _cachedSnapshot = RuleSnapshot.Build(activeRules, settings.WhitelistMode);
            return _cachedSnapshot;
        }
    }

    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedSnapshot = null;
            _studentCache.Clear();
        }
    }

    private StudentInfo? ResolveStudentCached(string normalizedStudentIp)
    {
        if (string.IsNullOrWhiteSpace(normalizedStudentIp))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        lock (_cacheLock)
        {
            if (_studentCache.TryGetValue(normalizedStudentIp, out var cached) && cached.ExpiresAtUtc > now)
            {
                return cached.Student;
            }
        }

        var student = _db.Students.FindOne(s => s.IpAddress == normalizedStudentIp)
            ?? _db.Students.FindAll().FirstOrDefault(s => IpAddressNormalizer.EqualsNormalized(s.IpAddress, normalizedStudentIp));

        if (student != null &&
            !string.Equals(student.IpAddress, normalizedStudentIp, StringComparison.OrdinalIgnoreCase))
        {
            student.IpAddress = normalizedStudentIp;
            _db.Students.Update(student);
        }

        lock (_cacheLock)
        {
            _studentCache[normalizedStudentIp] = (student, now.AddSeconds(5));
        }

        return student;
    }

    private static PreparedRule PrepareRule(FilterRule rule)
    {
        var normalizedPattern = (rule.Pattern ?? string.Empty).Trim().ToLowerInvariant();
        var category = (rule.Category ?? string.Empty).Trim();

        if (normalizedPattern.StartsWith("/") && normalizedPattern.EndsWith("/") && normalizedPattern.Length > 1)
        {
            try
            {
                return new PreparedRule(rule.Type, RuleMatchKind.Regex, normalizedPattern, new Regex(
                    normalizedPattern[1..^1],
                    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(200)),
                    category);
            }
            catch
            {
                return new PreparedRule(rule.Type, RuleMatchKind.NeverMatch, normalizedPattern, null, category);
            }
        }

        if (normalizedPattern.Contains('*'))
        {
            var regexPattern = "^" + Regex.Escape(normalizedPattern).Replace("\\*", ".*") + "$";
            return new PreparedRule(rule.Type, RuleMatchKind.Wildcard, normalizedPattern, new Regex(
                regexPattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(200)),
                category);
        }

        return new PreparedRule(rule.Type, RuleMatchKind.Contains, normalizedPattern, null, category);
    }

    public string ExtractDomain(string url)
    {
        try
        {
            var uri = new Uri(url.StartsWith("http") ? url : "http://" + url);
            var host = uri.Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        }
        catch
        {
            return url;
        }
    }

    public List<FilterRule> GetAllRules() => _db.FilterRules.FindAll().ToList();

    public FilterRule AddRule(FilterRule rule)
    {
        _db.FilterRules.Insert(rule);
        InvalidateCache();
        return rule;
    }

    public void UpdateRule(FilterRule rule)
    {
        _db.FilterRules.Update(rule);
        InvalidateCache();
    }

    public bool DeleteRule(string id)
    {
        var deleted = _db.FilterRules.Delete(id);
        if (deleted)
        {
            InvalidateCache();
        }

        return deleted;
    }

    public void ToggleRule(string id)
    {
        var rule = _db.FilterRules.FindById(id);
        if (rule == null) return;
        rule.IsActive = !rule.IsActive;
        _db.FilterRules.Update(rule);
        InvalidateCache();
    }

    private sealed class RuleSnapshot
    {
        public ScopeRules Global { get; }
        public Dictionary<string, ScopeRules> ByStudentId { get; }
        public Dictionary<string, ScopeRules> ByGroup { get; }
        public bool WhitelistMode { get; }

        private RuleSnapshot(
            ScopeRules global,
            Dictionary<string, ScopeRules> byStudentId,
            Dictionary<string, ScopeRules> byGroup,
            bool whitelistMode)
        {
            Global = global;
            ByStudentId = byStudentId;
            ByGroup = byGroup;
            WhitelistMode = whitelistMode;
        }

        public static RuleSnapshot Build(List<FilterRule> activeRules, bool whitelistMode)
        {
            var global = new ScopeRules();
            var byStudentId = new Dictionary<string, ScopeRules>(StringComparer.OrdinalIgnoreCase);
            var byGroup = new Dictionary<string, ScopeRules>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in activeRules)
            {
                var prepared = PrepareRule(rule);

                if (!string.IsNullOrWhiteSpace(rule.ApplyToStudentId))
                {
                    var studentId = rule.ApplyToStudentId.Trim();
                    if (!byStudentId.TryGetValue(studentId, out var scope))
                    {
                        scope = new ScopeRules();
                        byStudentId[studentId] = scope;
                    }

                    scope.Add(prepared);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(rule.ApplyToGroup))
                {
                    var group = rule.ApplyToGroup.Trim();
                    if (!byGroup.TryGetValue(group, out var scope))
                    {
                        scope = new ScopeRules();
                        byGroup[group] = scope;
                    }

                    scope.Add(prepared);
                    continue;
                }

                global.Add(prepared);
            }

            return new RuleSnapshot(global, byStudentId, byGroup, whitelistMode);
        }
    }

    private sealed class ScopeRules
    {
        public List<PreparedRule> Whitelist { get; } = new();
        public List<PreparedRule> Blacklist { get; } = new();
        public bool HasWhitelist => Whitelist.Count > 0;

        public void Add(PreparedRule rule)
        {
            if (rule.Type == FilterType.Whitelist)
            {
                Whitelist.Add(rule);
            }
            else
            {
                Blacklist.Add(rule);
            }
        }
    }

    private enum RuleMatchKind
    {
        NeverMatch,
        Contains,
        Wildcard,
        Regex
    }

    private sealed class PreparedRule
    {
        public FilterType Type { get; }
        public string Category { get; }
        private RuleMatchKind Kind { get; }
        public string Pattern { get; }
        private Regex? Regex { get; }

        public PreparedRule(FilterType type, RuleMatchKind kind, string pattern, Regex? regex)
        {
            Type = type;
            Kind = kind;
            Pattern = pattern;
            Regex = regex;
            Category = string.Empty;
        }

        public PreparedRule(FilterType type, RuleMatchKind kind, string pattern, Regex? regex, string category)
            : this(type, kind, pattern, regex)
        {
            Category = category;
        }

        public bool Matches(string normalizedUrl, string normalizedDomain)
        {
            if (string.IsNullOrWhiteSpace(Pattern))
            {
                return false;
            }

            try
            {
                return Kind switch
                {
                    RuleMatchKind.Contains => DomainMatches(normalizedDomain, Pattern) ||
                                              Pattern.Contains('/') && normalizedUrl.Contains(Pattern, StringComparison.Ordinal),
                    RuleMatchKind.Wildcard => Regex != null && (Regex.IsMatch(normalizedDomain) || Regex.IsMatch(normalizedUrl)),
                    RuleMatchKind.Regex => Regex != null && Regex.IsMatch(normalizedUrl),
                    _ => false
                };
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        private static bool DomainMatches(string normalizedDomain, string pattern)
        {
            if (string.IsNullOrWhiteSpace(normalizedDomain) || string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            var normalizedPattern = pattern.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? pattern[4..]
                : pattern;

            return string.Equals(normalizedDomain, normalizedPattern, StringComparison.Ordinal) ||
                   normalizedDomain.EndsWith("." + normalizedPattern, StringComparison.Ordinal);
        }
    }
}

public sealed record FilterDecision(
    bool IsAllowed,
    string Domain,
    string Reason,
    string? MatchedRule,
    string Scope,
    string Policy)
{
    public static FilterDecision Allowed(string domain, string reason, string? matchedRule, string scope, string policy) =>
        new(true, domain, reason, matchedRule, scope, policy);

    public static FilterDecision Blocked(string domain, string reason, string? matchedRule, string scope, string policy) =>
        new(false, domain, reason, matchedRule, scope, policy);
}

internal sealed record RuleMatch(string Pattern, string Scope, string Category);
