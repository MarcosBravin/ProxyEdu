using Microsoft.AspNetCore.Mvc;
using ProxyEdu.Server.Services;
using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FiltersController : ControllerBase
{
    private readonly FilterService _filter;
    private readonly DatabaseService _db;

    public FiltersController(FilterService filter, DatabaseService db)
    {
        _filter = filter;
        _db = db;
    }

    [HttpGet]
    public IActionResult GetAll() => Ok(_filter.GetAllRules());

    [HttpPost]
    public IActionResult Create([FromBody] FilterRule rule)
    {
        rule.Id = Guid.NewGuid().ToString();
        rule.CreatedAt = DateTime.UtcNow;
        return Ok(_filter.AddRule(rule));
    }

    [HttpPut("{id}")]
    public IActionResult Update(string id, [FromBody] FilterRule rule)
    {
        rule.Id = id;
        _filter.UpdateRule(rule);
        return Ok(rule);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        _filter.DeleteRule(id);
        return Ok(new { success = true });
    }

    [HttpPost("{id}/toggle")]
    public IActionResult Toggle(string id)
    {
        _filter.ToggleRule(id);
        return Ok(new { success = true });
    }

    [HttpPost("import")]
    public IActionResult Import([FromBody] ImportRulesRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Conteudo para importacao nao informado.");

        var patterns = ParsePatterns(request.Content);
        if (patterns.Count == 0)
            return BadRequest("Nenhum padrao valido encontrado para importacao.");

        var scopeGroup = string.IsNullOrWhiteSpace(request.ApplyToGroup) ? null : request.ApplyToGroup.Trim();
        var scopeStudent = string.IsNullOrWhiteSpace(request.ApplyToStudentId) ? null : request.ApplyToStudentId.Trim();
        var imported = 0;
        var skipped = 0;

        foreach (var pattern in patterns.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var duplicate = _db.FilterRules.FindOne(r =>
                r.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase) &&
                r.Type == request.Type &&
                r.ApplyToGroup == scopeGroup &&
                r.ApplyToStudentId == scopeStudent);

            if (duplicate != null)
            {
                skipped++;
                continue;
            }

            var rule = new FilterRule
            {
                Id = Guid.NewGuid().ToString(),
                Pattern = pattern,
                Type = request.Type,
                Description = request.DescriptionPrefix ?? "Importado em lote",
                IsActive = true,
                ApplyToGroup = scopeGroup,
                ApplyToStudentId = scopeStudent,
                CreatedAt = DateTime.UtcNow
            };

            _filter.AddRule(rule);
            imported++;
        }

        return Ok(new { imported, skipped, total = patterns.Count });
    }

    // Quick add common presets
    [HttpPost("preset/{name}")]
    public IActionResult AddPreset(string name)
    {
        var presets = new Dictionary<string, List<FilterRule>>
        {
            ["social"] = new()
            {
                new() { Pattern = "facebook.com", Type = FilterType.Blacklist, Description = "Facebook" },
                new() { Pattern = "instagram.com", Type = FilterType.Blacklist, Description = "Instagram" },
                new() { Pattern = "tiktok.com", Type = FilterType.Blacklist, Description = "TikTok" },
                new() { Pattern = "twitter.com", Type = FilterType.Blacklist, Description = "Twitter/X" },
                new() { Pattern = "snapchat.com", Type = FilterType.Blacklist, Description = "Snapchat" },
            },
            ["games"] = new()
            {
                new() { Pattern = "*.roblox.com", Type = FilterType.Blacklist, Description = "Roblox" },
                new() { Pattern = "*.friv.com", Type = FilterType.Blacklist, Description = "Friv" },
                new() { Pattern = "*.miniclip.com", Type = FilterType.Blacklist, Description = "Miniclip" },
                new() { Pattern = "poki.com", Type = FilterType.Blacklist, Description = "Poki" },
            },
            ["streaming"] = new()
            {
                new() { Pattern = "youtube.com", Type = FilterType.Blacklist, Description = "YouTube" },
                new() { Pattern = "netflix.com", Type = FilterType.Blacklist, Description = "Netflix" },
                new() { Pattern = "twitch.tv", Type = FilterType.Blacklist, Description = "Twitch" },
                new() { Pattern = "spotify.com", Type = FilterType.Blacklist, Description = "Spotify" },
            }
        };

        if (!presets.ContainsKey(name)) return BadRequest("Preset não encontrado");

        foreach (var rule in presets[name])
        {
            rule.Id = Guid.NewGuid().ToString();
            rule.IsActive = true;
            _filter.AddRule(rule);
        }

        return Ok(new { added = presets[name].Count });
    }

    private static List<string> ParsePatterns(string content)
    {
        var lines = content
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Where(l => !l.StartsWith("#") && !l.StartsWith("//"))
            .ToList();

        var items = new List<string>();
        foreach (var line in lines)
        {
            var lineWithoutComment = line.Split('#')[0].Trim();
            if (string.IsNullOrWhiteSpace(lineWithoutComment)) continue;

            var parts = lineWithoutComment
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length == 0)
            {
                items.Add(lineWithoutComment);
                continue;
            }

            foreach (var part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                    items.Add(part.Trim());
            }
        }

        return items;
    }
}

public class ImportRulesRequest
{
    public string Content { get; set; } = "";
    public FilterType Type { get; set; } = FilterType.Blacklist;
    public string? DescriptionPrefix { get; set; }
    public string? ApplyToGroup { get; set; }
    public string? ApplyToStudentId { get; set; }
}
