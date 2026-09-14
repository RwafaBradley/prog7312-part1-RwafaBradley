using SmartX.Core.Domain;
using SmartX.Core.Telemetry;
using SmartX.Core.Topology;

namespace SmartX.Core.Validation;

// checks happen at the door, so one misbehaving node cannot drag the averages everything else gets scored against
public static class PacketValidator
{
    public static IReadOnlyList<string> ValidateRegistration(SensorRegistration registration)
    {
        var errors = new List<string>();

        if (!MacAddressRules.IsValid(registration.MacAddress))
        {
            errors.Add("MAC address must be six hexadecimal octets, e.g. A4:CF:12:9B:4E:07.");
        }

        if (string.IsNullOrWhiteSpace(registration.Location.Facility))
        {
            errors.Add("Deployment facility is required.");
        }

        if (string.IsNullOrWhiteSpace(registration.Location.Zone))
        {
            errors.Add("Deployment zone is required.");
        }

        if (string.IsNullOrWhiteSpace(registration.Location.NodeId))
        {
            errors.Add("Node identifier is required.");
        }

        if (!Enum.IsDefined(registration.Category))
        {
            errors.Add("Sensor category must be Environmental, PowerConsumption or Actuator.");
        }

        if (registration.MinExpected >= registration.MaxExpected &&
            registration.Category != SensorCategory.Actuator)
        {
            errors.Add("The expected minimum must be lower than the expected maximum.");
        }

        if (registration.Alias.Length > 64)
        {
            errors.Add("Alias may not exceed 64 characters.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidatePacket(
        SensorRegistration registration,
        TelemetryEnvelope envelope,
        long lastSequence)
    {
        var errors = new List<string>();

        if (!MacAddressRules.IsValid(envelope.MacAddress))
        {
            errors.Add("Packet carries a malformed MAC address.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Metric))
        {
            errors.Add("Packet carries no metric name.");
        }

        if (envelope.Kind != registration.ValueKind)
        {
            errors.Add(
                $"Payload type {envelope.Kind} does not match the registered {registration.Category} channel, which publishes {registration.ValueKind}.");
        }

        if (double.IsNaN(envelope.Numeric) || double.IsInfinity(envelope.Numeric))
        {
            errors.Add("Payload is not a finite number.");
        }
        else if (registration.Category != SensorCategory.Actuator)
        {
            var span = registration.MaxExpected - registration.MinExpected;
            // a little slack on purpose, a reading just outside the band is the interesting one and is kept and flagged rather than binned
            var slack = Math.Abs(span) * 0.5d;

            if (envelope.Numeric < registration.MinExpected - slack ||
                envelope.Numeric > registration.MaxExpected + slack)
            {
                errors.Add(
                    $"Reading {envelope.Numeric:F2} is implausible for a channel rated {registration.MinExpected:F1}–{registration.MaxExpected:F1}.");
            }
        }
        else if (envelope.Numeric is not (0d or 1d))
        {
            errors.Add("An actuator state must be 0 or 1.");
        }

        if (envelope.Sequence < 0)
        {
            errors.Add("Sequence numbers are monotonic and may not be negative.");
        }
        // a repeated counter means the packet is a replay, the same way a till refuses to scan one receipt twice
        else if (lastSequence >= 0 && envelope.Sequence <= lastSequence)
        {
            errors.Add($"Replayed or out-of-order sequence {envelope.Sequence}; last accepted was {lastSequence}.");
        }

        if (envelope.TimestampUtc > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            errors.Add("Packet timestamp is more than five minutes in the future; check the node's clock.");
        }

        return errors;
    }
}
