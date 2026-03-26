using ProxyEdu.Shared.Models;
using System.Text.RegularExpressions;

namespace ProxyEdu.Server.Services;

public class FilterService
{
    private readonly DatabaseService _db;
    private readonly object _cacheLock = new();
    private RuleSnapshot? _cachedSnapshot;

    public FilterService(DatabaseService db)
    {
        _db = db;
    }

    public bool IsUrlAllowed(string url, string studentIp)
    {
        var settings = _db.GetSettings();
        var normalizedStudentIp = IpAddressNormalizer.Normalize(studentIp);
        var student = _db.Students.FindOne(s => s.IpAddress == normalizedStudentIp)
            ?? _db.Students.FindAll().FirstOrDefault(s => IpAddressNormalizer.EqualsNormalized(s.IpAddress, normalizedStudentIp));

        if (student != null &&
            !string.Equals(student.IpAddress, normalizedStudentIp, StringComparison.OrdinalIgnoreCase))
        {
            student.IpAddress = normalizedStudentIp;
            _db.Students.Update(student);
        }

        var snapshot = GetOrBuildSnapshot();
        var safeUrl = url ?? string.Empty;
        var normalizedUrl = safeUrl.ToLowerInvariant();
        var normalizedDomain = ExtractDomain(safeUrl).ToLowerInvariant();

        if (HasWhitelistMatch(snapshot, student, normalizedUrl, normalizedDomain))
        {
            return true;
        }

        if (HasBlacklistMatch(snapshot, student, normalizedUrl, normalizedDomain))
        {
            return false;
        }

        // Mantém o comportamento atual de fallback em whitelist mode.
        if (settings.WhitelistMode)
        {
            var hasWhitelistRules = snapshot.Global.HasWhitelist
                || (student != null &&
                    snapshot.ByStudentId.TryGetValue(student.Id, out var studentRules) &&
                    studentRules.HasWhitelist)
                || (!string.IsNullOrWhiteSpace(student?.Group) &&
                    snapshot.ByGroup.TryGetValue(student.Group, out var groupRules) &&
                    groupRules.HasWhitelist);

            return !hasWhitelistRules;
        }

        return true;
    }

    private static bool HasWhitelistMatch(RuleSnapshot snapshot, StudentInfo? student, string normalizedUrl, string normalizedDomain)
    {
        if (HasMatch(snapshot.Global.Whitelist, normalizedUrl, normalizedDomain))
        {
            return true;
        }

        if (student != null &&
            snapshot.ByStudentId.TryGetValue(student.Id, out var studentRules) &&
            HasMatch(studentRules.Whitelist, normalizedUrl, normalizedDomain))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(student?.Group) &&
            snapshot.ByGroup.TryGetValue(student.Group, out var groupRules) &&
            HasMatch(groupRules.Whitelist, normalizedUrl, normalizedDomain))
        {
            return true;
        }

        return false;
    }

    private static bool HasBlacklistMatch(RuleSnapshot snapshot, StudentInfo? student, string normalizedUrl, string normalizedDomain)
    {
        if (HasMatch(snapshot.Global.Blacklist, normalizedUrl, normalizedDomain))
        {
            return true;
        }

        if (student != null &&
            snapshot.ByStudentId.TryGetValue(student.Id, out var studentRules) &&
            HasMatch(studentRules.Blacklist, normalizedUrl, normalizedDomain))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(student?.Group) &&
            snapshot.ByGroup.TryGetValue(student.Group, out var groupRules) &&
            HasMatch(groupRules.Blacklist, normalizedUrl, normalizedDomain))
        {
            return true;
        }

        return false;
    }

    private static bool HasMatch(List<PreparedRule> rules, string normalizedUrl, string normalizedDomain)
    {
        foreach (var rule in rules)
        {
            if (rule.Matches(normalizedUrl, normalizedDomain))
            {
                return true;
            }
        }

        return false;
    }

    private RuleSnapshot GetOrBuildSnapshot()
    {
        lock (_cacheLock)
        {
            if (_cachedSnapshot != null)
            {
                return _cachedSnapshot;
            }

            var activeRules = _db.FilterRules.Find(r => r.IsActive).ToList();
            _cachedSnapshot = RuleSnapshot.Build(activeRules);
            return _cachedSnapshot;
        }
    }

    private void InvalidateSnapshot()
    {
        lock (_cacheLock)
        {
            _cachedSnapshot = null;
        }
    }

    private static PreparedRule PrepareRule(FilterRule rule)
    {
        var normalizedPattern = (rule.Pattern ?? string.Empty).Trim().ToLowerInvariant();

        if (normalizedPattern.StartsWith("/") && normalizedPattern.EndsWith("/") && normalizedPattern.Length > 1)
        {
            try
            {
                return new PreparedRule(rule.Type, RuleMatchKind.Regex, normalizedPattern, new Regex(
                    normalizedPattern[1..^1],
                    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(200)));
            }
            catch
            {
                return new PreparedRule(rule.Type, RuleMatchKind.NeverMatch, normalizedPattern, null);
            }
        }

        if (normalizedPattern.Contains('*'))
        {
            var regexPattern = "^" + Regex.Escape(normalizedPattern).Replace("\\*", ".*") + "$";
            return new PreparedRule(rule.Type, RuleMatchKind.Wildcard, normalizedPattern, new Regex(
                regexPattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(200)));
        }

        return new PreparedRule(rule.Type, RuleMatchKind.Contains, normalizedPattern, null);
    }

    public string ExtractDomain(string url)
    {
        try
        {
            var uri = new Uri(url.StartsWith("http") ? url : "http://" + url);
            return uri.Host.TrimStart("www.".ToCharArray());
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
        InvalidateSnapshot();
        return rule;
    }

    public void UpdateRule(FilterRule rule)
    {
        _db.FilterRules.Update(rule);
        InvalidateSnapshot();
    }

    public bool DeleteRule(string id)
    {
        var deleted = _db.FilterRules.Delete(id);
        if (deleted)
        {
            InvalidateSnapshot();
        }

        return deleted;
    }

    public void ToggleRule(string id)
    {
        var rule = _db.FilterRules.FindById(id);
        if (rule == null) return;
        rule.IsActive = !rule.IsActive;
        _db.FilterRules.Update(rule);
        InvalidateSnapshot();
    }

    private sealed class RuleSnapshot
    {
        public ScopeRules Global { get; }
        public Dictionary<string, ScopeRules> ByStudentId { get; }
        public Dictionary<string, ScopeRules> ByGroup { get; }

        private RuleSnapshot(
            ScopeRules global,
            Dictionary<string, ScopeRules> byStudentId,
            Dictionary<string, ScopeRules> byGroup)
        {
            Global = global;
            ByStudentId = byStudentId;
            ByGroup = byGroup;
        }

        public static RuleSnapshot Build(List<FilterRule> activeRules)
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

            return new RuleSnapshot(global, byStudentId, byGroup);
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
        private RuleMatchKind Kind { get; }
        private string Pattern { get; }
        private Regex? Regex { get; }

        public PreparedRule(FilterType type, RuleMatchKind kind, string pattern, Regex? regex)
        {
            Type = type;
            Kind = kind;
            Pattern = pattern;
            Regex = regex;
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
                    RuleMatchKind.Contains => normalizedDomain.Contains(Pattern, StringComparison.Ordinal) ||
                                              normalizedUrl.Contains(Pattern, StringComparison.Ordinal),
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
    }
}
