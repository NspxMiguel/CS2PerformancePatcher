using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cs2Patcher.Core;

/// <summary>
/// Reader/writer for Colossal's <c>Settings.coc</c>, which is plain text shaped as a
/// sequence of named sections, each followed by a JSON object:
/// <code>
/// Graphics Settings
/// { ... }
/// Interface Settings
/// { ... }
/// </code>
/// Sections we don't touch are round-tripped through the JSON parser unchanged, so
/// keybindings and audio survive a patch untouched.
/// </summary>
public sealed class CocDocument
{
    public sealed class Section(string name, JsonObject json)
    {
        public string Name { get; } = name;
        public JsonObject Json { get; set; } = json;
    }

    public const string GraphicsSection = "Graphics Settings";

    /// <summary>The JSON property the game uses to discriminate entries in <c>qualitySettings</c>.</summary>
    public const string TypeKey = "@type";

    public List<Section> Sections { get; } = [];

    public static CocDocument Parse(string text)
    {
        var doc = new CocDocument();
        var i = 0;

        while (i < text.Length)
        {
            // A section header is the next non-blank line that isn't the JSON body itself.
            var lineEnd = text.IndexOf('\n', i);
            if (lineEnd < 0) break;

            var header = text[i..lineEnd].Trim();
            i = lineEnd + 1;
            if (header.Length == 0) continue;

            var braceStart = text.IndexOf('{', i - 1);
            if (braceStart < 0) break;

            var braceEnd = FindMatchingBrace(text, braceStart);
            if (braceEnd < 0)
                throw new InvalidDataException($"Section '{header}' has an unbalanced JSON body.");

            var body = text[braceStart..(braceEnd + 1)];
            var node = JsonNode.Parse(body) as JsonObject
                       ?? throw new InvalidDataException($"Section '{header}' is not a JSON object.");

            doc.Sections.Add(new Section(header, node));
            i = braceEnd + 1;
        }

        if (doc.Sections.Count == 0)
            throw new InvalidDataException("No sections found — this does not look like a Settings.coc file.");

        return doc;
    }

    public static CocDocument Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Serializes back to the game's on-disk shape. The game re-indents on its next save.</summary>
    public string Serialize()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var sb = new StringBuilder();

        foreach (var section in Sections)
        {
            sb.Append(section.Name).Append('\n');
            sb.Append(section.Json.ToJsonString(options)).Append('\n');
        }

        return sb.ToString();
    }

    public void Save(string path) => File.WriteAllText(path, Serialize(), new UTF8Encoding(false));

    public Section? GetSection(string name) =>
        Sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds one entry of the graphics <c>qualitySettings</c> array by its <c>@type</c>,
    /// e.g. <c>"Game.Settings.ShadowsQualitySettings"</c>. Returns null when the game
    /// has not written that block yet (it only persists what it has touched).
    /// </summary>
    public JsonObject? GetQualitySetting(string typeName)
    {
        if (GetSection(GraphicsSection)?.Json["qualitySettings"] is not JsonArray array) return null;

        foreach (var entry in array)
        {
            if (entry is JsonObject obj && obj[TypeKey]?.GetValue<string>() == typeName)
                return obj;
        }
        return null;
    }

    /// <summary>
    /// Same as <see cref="GetQualitySetting"/>, but appends a new block when the game
    /// has not persisted one. Needed for types like ExtraQualitySettings, which are
    /// absent from a fresh Settings.coc.
    /// </summary>
    /// <summary>
    /// The Graphics section's own object, for the settings that sit beside the
    /// <c>qualitySettings</c> array rather than inside it — vSync, dlssQuality, resolution.
    /// </summary>
    public JsonObject GetGraphicsRoot() =>
        GetSection(GraphicsSection)?.Json
        ?? throw new InvalidDataException($"'{GraphicsSection}' section is missing.");

    public JsonObject GetOrAddQualitySetting(string typeName)
    {
        var existing = GetQualitySetting(typeName);
        if (existing is not null) return existing;

        var graphics = GetSection(GraphicsSection)
                       ?? throw new InvalidDataException($"'{GraphicsSection}' section is missing.");

        if (graphics.Json["qualitySettings"] is not JsonArray array)
        {
            array = [];
            graphics.Json["qualitySettings"] = array;
        }

        var created = new JsonObject { [TypeKey] = typeName };
        array.Add(created);
        return created;
    }

    private static int FindMatchingBrace(string text, int openIndex)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return i;
                    break;
            }
        }
        return -1;
    }
}
