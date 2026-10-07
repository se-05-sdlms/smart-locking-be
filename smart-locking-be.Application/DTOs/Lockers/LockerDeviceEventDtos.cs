using System.Text.Json.Serialization;

namespace smart_locking_be.Application.DTOs.Lockers;

public sealed record CommandAckPayload(
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] bool Ok);

public sealed record DoorEventPayload(
    [property: JsonRequired] int HardwareChannel,
    [property: JsonRequired] string State,
    DateTimeOffset? At);

public sealed record StatusPayload([property: JsonRequired] bool Online);
