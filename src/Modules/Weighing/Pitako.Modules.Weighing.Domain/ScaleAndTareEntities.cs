using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Weighing.Domain;

public enum ScaleManufacturer
{
    Toledo = 1,
    Filizola = 2,
    Urano = 3,
    Generic = 9
}

public enum ScaleConnectionType
{
    SerialPort = 1,
    UsbHid = 2,
    TcpIp = 3,
    ManualOnly = 4
}

public enum WeighingCaptureMode
{
    AutomaticScale = 1,
    ManualFallback = 2
}

/// <summary>
/// Configuração de balança comercial vinculada a um estabelecimento (Seção 14).
/// </summary>
public sealed class ScaleDeviceConfig : TenantScopedAggregateRoot
{
    private ScaleDeviceConfig()
    {
        Name = string.Empty;
        ProtocolProfile = string.Empty;
        EndpointOrPort = string.Empty;
    }

    private ScaleDeviceConfig(
        Guid id,
        Guid tenantId,
        string name,
        ScaleManufacturer manufacturer,
        ScaleConnectionType connectionType,
        string protocolProfile,
        string endpointOrPort,
        int baudRate,
        bool allowManualFallback,
        bool isDefault) : base(id, tenantId)
    {
        Name = name;
        Manufacturer = manufacturer;
        ConnectionType = connectionType;
        ProtocolProfile = protocolProfile;
        EndpointOrPort = endpointOrPort;
        BaudRate = baudRate;
        AllowManualFallback = allowManualFallback;
        IsDefault = isDefault;
        IsActive = true;
    }

    public string Name { get; private set; }
    public ScaleManufacturer Manufacturer { get; private set; }
    public ScaleConnectionType ConnectionType { get; private set; }
    public string ProtocolProfile { get; private set; }
    public string EndpointOrPort { get; private set; }
    public int BaudRate { get; private set; }
    public bool AllowManualFallback { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<ScaleDeviceConfig> Create(
        Guid tenantId,
        string name,
        ScaleManufacturer manufacturer,
        ScaleConnectionType connectionType,
        string endpointOrPort,
        string protocolProfile = "TOLEDO_PRT",
        int baudRate = 9600,
        bool allowManualFallback = true,
        bool isDefault = false)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<ScaleDeviceConfig>(Error.Validation("Scale.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<ScaleDeviceConfig>(Error.Validation("Scale.NameRequired", "Nome da balança é obrigatório."));
        if (connectionType != ScaleConnectionType.ManualOnly && string.IsNullOrWhiteSpace(endpointOrPort))
            return Result.Failure<ScaleDeviceConfig>(Error.Validation("Scale.EndpointRequired", "Porta serial, dispositivo ou IP da balança é obrigatório."));
        if (baudRate <= 0)
            return Result.Failure<ScaleDeviceConfig>(Error.Validation("Scale.InvalidBaudRate", "BaudRate inválido."));

        return Result.Success(new ScaleDeviceConfig(
            Guid.NewGuid(),
            tenantId,
            name.Trim(),
            manufacturer,
            connectionType,
            string.IsNullOrWhiteSpace(protocolProfile) ? "TOLEDO_PRT" : protocolProfile.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(endpointOrPort) ? "MANUAL" : endpointOrPort.Trim(),
            baudRate,
            allowManualFallback,
            isDefault));
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}

/// <summary>
/// Recipiente/tara pré-cadastrado pelo estabelecimento para desconto automático do peso bruto (Seção 14).
/// </summary>
public sealed class TarePreset : TenantScopedAggregateRoot
{
    private TarePreset()
    {
        Name = string.Empty;
    }

    private TarePreset(
        Guid id,
        Guid tenantId,
        string name,
        decimal tareWeightGrams) : base(id, tenantId)
    {
        Name = name;
        TareWeightGrams = decimal.Round(tareWeightGrams, 3, MidpointRounding.ToEven);
        IsActive = true;
    }

    public string Name { get; private set; }
    public decimal TareWeightGrams { get; private set; }
    public bool IsActive { get; private set; }

    public static Result<TarePreset> Create(Guid tenantId, string name, decimal tareWeightGrams)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TarePreset>(Error.Validation("Tare.TenantRequired", "TenantId é obrigatório."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<TarePreset>(Error.Validation("Tare.NameRequired", "Nome da tara é obrigatório."));
        if (tareWeightGrams < 0m)
            return Result.Failure<TarePreset>(Error.Validation("Tare.NegativeWeight", "Peso da tara não pode ser negativo."));

        return Result.Success(new TarePreset(Guid.NewGuid(), tenantId, name.Trim(), tareWeightGrams));
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
