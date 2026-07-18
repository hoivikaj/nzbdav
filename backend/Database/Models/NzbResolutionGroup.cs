namespace NzbWebDAV.Database.Models;

public class NzbResolutionGroup
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string ProfileToken { get; set; } = "";
    public string SearchId { get; set; } = "";
    public string CandidatesJson { get; set; } = "";
    public string TokensJson { get; set; } = "";
    public long CreatedAtUnix { get; set; }
}
