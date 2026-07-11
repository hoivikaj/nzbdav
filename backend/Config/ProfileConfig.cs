using System.Text.Json.Serialization;

namespace NzbWebDAV.Config;

public class ProfileConfig
{
    public List<Profile> Profiles { get; set; } = [];

    public ProfileConfig Normalized()
    {
        foreach (var p in Profiles) p.MigrateLegacy();
        return this;
    }

    public class Profile
    {
        public required string Token { get; set; }
        public required string Name { get; set; }
        public List<string> IndexerNames { get; set; } = [];
        public List<string>? EnabledAdapters { get; set; }

        public FallbackMode MovieFallback { get; set; } = FallbackMode.Off;
        public FallbackMode TvFallback { get; set; } = FallbackMode.Off;
        public int MovieFallbackMinResults { get; set; } = 3;
        public int TvFallbackMinResults { get; set; } = 3;

        public int? QueryFallbackMinResults { get; set; }

        internal void MigrateLegacy()
        {
            if (QueryFallbackMinResults is { } legacy && legacy > 0
                && MovieFallback == FallbackMode.Off
                && TvFallback == FallbackMode.Off)
            {
                MovieFallback = FallbackMode.Title;
                TvFallback = FallbackMode.Broad;
                MovieFallbackMinResults = legacy;
                TvFallbackMinResults = legacy;
            }
            QueryFallbackMinResults = null;
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FallbackMode
    {
        Off = 0,
        Title = 1,
        Broad = 2,
    }
}
