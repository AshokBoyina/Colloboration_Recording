namespace NICE.Platform.Collaboration.Core.Responses;
public class IceServerConfigResponse
{
    public List<string> Urls { get; set; } = [];
    public string? Username { get; set; }
    public string? Credential { get; set; }
}
