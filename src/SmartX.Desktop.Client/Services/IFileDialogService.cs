namespace SmartX.Desktop.Client.Services;

// the file picker sits behind this so the view models stay testable and free of windows
public interface IFileDialogService
{
    IReadOnlyList<string> PickFiles(string title, string filter, bool allowMultiple = true);

    string? PickSaveLocation(string title, string suggestedFileName, string filter);

    const string AttachmentFilter =
        "Sensor attachments|*.json;*.yaml;*.yml;*.toml;*.ini;*.cfg;*.conf;*.xml;*.csv;*.log;*.txt;*.ndjson;*.jpg;*.jpeg;*.png;*.webp;*.gif;*.zip;*.pcap|" +
        "Configuration files|*.json;*.yaml;*.yml;*.toml;*.ini;*.cfg;*.conf;*.xml|" +
        "Hardware logs|*.log;*.txt;*.ndjson|" +
        "Deployment photographs|*.jpg;*.jpeg;*.png;*.webp;*.gif|" +
        "Packet captures and archives|*.pcap;*.zip";
}

public sealed class NullFileDialogService : IFileDialogService
{
    public IReadOnlyList<string> PickFiles(string title, string filter, bool allowMultiple = true)
        => Array.Empty<string>();

    public string? PickSaveLocation(string title, string suggestedFileName, string filter) => null;
}
