using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using SmartX.Core.Domain;
using SmartX.Desktop.Client.Mvvm;
using SmartX.Desktop.Client.Services;

namespace SmartX.Desktop.Client.ViewModels;

public sealed partial class RegistrationFormViewModel : ObservableObject, INotifyDataErrorInfo
{
    [GeneratedRegex(@"^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$")]
    private static partial Regex MacPattern();

    private readonly Dictionary<string, List<string>> _errors = new();

    private string _macAddress = string.Empty;
    private string _alias = string.Empty;
    private SensorCategory _category = SensorCategory.Environmental;
    private string _facility = "Facility A";
    private string _zone = "Zone 1";
    private string _subZone = "Sub-Zone B";
    private string _nodeId = string.Empty;
    private string _firmwareVersion = "esp32-idf-5.2.0";
    private string _unit = "%VWC";
    private double _minExpected = 10d;
    private double _maxExpected = 70d;

    public RegistrationFormViewModel()
    {
        Categories = new ObservableCollection<SensorCategory>(Enum.GetValues<SensorCategory>());
        ValidateAll();
    }

    public ObservableCollection<SensorCategory> Categories { get; }

    public string MacAddress
    {
        get => _macAddress;
        set
        {
            if (!Set(ref _macAddress, value)) return;
            Validate(nameof(MacAddress));
            Raise(nameof(IsMacValid));
            Raise(nameof(MacHint));
        }
    }

    public bool IsMacValid => MacPattern().IsMatch(MacAddress.Trim());

    public string MacHint => MacAddress.Trim().Length == 0
        ? "six hexadecimal octets, for example A4:CF:12:9B:4E:07"
        : IsMacValid
            ? "valid MAC  —  48 bit hardware address"
            : "not a 48 bit address yet";

    public string Alias
    {
        get => _alias;
        set { if (Set(ref _alias, value)) Validate(nameof(Alias)); }
    }

    public SensorCategory Category
    {
        get => _category;
        set
        {
            if (!Set(ref _category, value)) return;

            (Unit, MinExpected, MaxExpected) = value switch
            {
                SensorCategory.Environmental => ("%VWC", 10d, 70d),
                SensorCategory.PowerConsumption => ("W", 0d, 2_400d),
                SensorCategory.Actuator => ("state", 0d, 1d),
                _ => (Unit, MinExpected, MaxExpected)
            };

            Raise(nameof(CategoryHint));
            ValidateAll();
        }
    }

    public string CategoryHint => Category switch
    {
        SensorCategory.Environmental => "Publishes a 32-bit float — soil moisture, temperature, humidity, CO₂.",
        SensorCategory.PowerConsumption => "Publishes a 32-bit integer — instantaneous wattage from a smart meter or PDU.",
        SensorCategory.Actuator => "Publishes a boolean — valve, relay, pump or contactor position.",
        _ => string.Empty
    };

    public string Facility
    {
        get => _facility;
        set { if (Set(ref _facility, value)) Validate(nameof(Facility)); }
    }

    public string Zone
    {
        get => _zone;
        set { if (Set(ref _zone, value)) Validate(nameof(Zone)); }
    }

    public string SubZone { get => _subZone; set => Set(ref _subZone, value); }

    public string NodeId
    {
        get => _nodeId;
        set { if (Set(ref _nodeId, value)) Validate(nameof(NodeId)); }
    }

    public string FirmwareVersion { get => _firmwareVersion; set => Set(ref _firmwareVersion, value); }

    public string Unit { get => _unit; set => Set(ref _unit, value); }

    public double MinExpected
    {
        get => _minExpected;
        set { if (Set(ref _minExpected, value)) Validate(nameof(MaxExpected)); }
    }

    public double MaxExpected
    {
        get => _maxExpected;
        set { if (Set(ref _maxExpected, value)) Validate(nameof(MaxExpected)); }
    }

    public bool IsValid => _errors.Count == 0;

    public SensorRegistrationRequest ToRequest() => new()
    {
        MacAddress = MacAddress.Trim().Replace('-', ':').ToUpperInvariant(),
        Alias = string.IsNullOrWhiteSpace(Alias) ? NodeId.Trim() : Alias.Trim(),
        Category = Category,
        Facility = Facility.Trim(),
        Zone = Zone.Trim(),
        SubZone = SubZone.Trim(),
        NodeId = NodeId.Trim(),
        FirmwareVersion = FirmwareVersion.Trim(),
        Unit = Unit.Trim(),
        MinExpected = MinExpected,
        MaxExpected = MaxExpected
    };

    public void Reset()
    {
        MacAddress = string.Empty;
        Alias = string.Empty;
        NodeId = string.Empty;
        ValidateAll();
    }

    private void ValidateAll()
    {
        Validate(nameof(MacAddress));
        Validate(nameof(Alias));
        Validate(nameof(Facility));
        Validate(nameof(Zone));
        Validate(nameof(NodeId));
        Validate(nameof(MaxExpected));
    }

    private void Validate(string propertyName)
    {
        var found = new List<string>();

        switch (propertyName)
        {
            case nameof(MacAddress):
                if (string.IsNullOrWhiteSpace(MacAddress))
                    found.Add("A MAC address is required.");
                else if (!MacPattern().IsMatch(MacAddress.Trim()))
                    found.Add("Use six hexadecimal octets, e.g. A4:CF:12:9B:4E:07.");
                break;

            case nameof(Alias):
                if (Alias.Length > 64) found.Add("Alias may not exceed 64 characters.");
                break;

            case nameof(Facility):
                if (string.IsNullOrWhiteSpace(Facility)) found.Add("Facility is required.");
                break;

            case nameof(Zone):
                if (string.IsNullOrWhiteSpace(Zone)) found.Add("Zone is required.");
                break;

            case nameof(NodeId):
                if (string.IsNullOrWhiteSpace(NodeId)) found.Add("Node identifier is required.");
                break;

            case nameof(MaxExpected):
                if (Category != SensorCategory.Actuator && MinExpected >= MaxExpected)
                    found.Add("The maximum must be greater than the minimum.");
                break;
        }

        if (found.Count > 0) _errors[propertyName] = found;
        else _errors.Remove(propertyName);

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        Raise(nameof(IsValid));
    }

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => _errors.Count > 0;

    public System.Collections.IEnumerable GetErrors(string? propertyName)
        => propertyName is not null && _errors.TryGetValue(propertyName, out var list)
            ? list
            : Array.Empty<string>();
}
