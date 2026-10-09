namespace HassExport.Model;

public class HassSettings
{
    public string Url { get; set; } = "http://192.168.33.3:8033";
    public string OutputFolder { get; set; } = "D:\\workspace\\PERSONNEL\\OREE_DOUS_HASS\\Elements";
    public string ProfileFolder { get; set; } = ".playwright-profile";
    public bool Headless { get; set; }
    public List<string> Keys { get; set; } = [];
}
