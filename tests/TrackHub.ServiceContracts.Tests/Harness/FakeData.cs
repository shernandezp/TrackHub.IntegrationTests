// Copyright (c) 2026 Sergio Hernandez. All rights reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License").
//  You may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//

using Common.Domain.Enums;
using TrackHub.Telemetry.Domain.Enums;
using ManagerModels = TrackHub.Manager.Domain.Models;
using TelemetryModels = TrackHub.Telemetry.Domain.Models;

namespace TrackHub.ServiceContracts.Tests.Harness;

/// <summary>
/// Canned domain objects the mocked producer handlers return. Values are deliberately
/// distinctive (non-default) so a field-mapping or type drift shows up as a wrong value,
/// not a silently-matching default.
/// </summary>
internal static class FakeData
{
    public static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OperatorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid TransporterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid CredentialId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid HealthCheckId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid SyncRunId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    public static readonly DateTimeOffset Timestamp = new(2026, 7, 1, 12, 30, 45, TimeSpan.Zero);
    public static readonly DateTimeOffset TokenExpiration = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    public static TelemetryModels.TransporterPositionVm TelemetryPosition() => new(
        TransporterPositionId: Guid.Parse("77777777-7777-7777-7777-777777777777"),
        TransporterId: TransporterId,
        DeviceName: "Device-01",
        TransporterType: TransporterType.Car,
        GeometryId: null,
        Latitude: 4.6534,
        Longitude: -74.0837,
        Altitude: 2601.5,
        DeviceDateTime: Timestamp,
        Speed: 42.5,
        Course: 187.3,
        EventId: 7,
        Address: "Cll 100 # 8-20",
        City: "Bogota",
        State: "Bogota D.C.",
        Country: "CO",
        Attributes: new TelemetryModels.AttributesVm(
            Ignition: true,
            Satellites: 12,
            Mileage: 12345.6,
            Hourmeter: 220.5,
            Temperature: 21.5,
            Extra: ExtraJson));

    // Open attribute-bag payload: provider signals beyond the 5 promoted
    // fields travel as a JSON object string in `attributes.extra`.
    public const string ExtraJson = "{\"fuelLevelPct\":\"80\",\"rpm\":\"1500\"}";

    public static TelemetryModels.TransporterPositionHistoryVm TelemetryHistoryRow() => new(
        TransporterPositionHistoryId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
        AccountId: AccountId,
        OperatorId: OperatorId,
        DeviceId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
        TransporterId: TransporterId,
        SourceTimestamp: Timestamp,
        ReceivedAt: Timestamp.AddSeconds(4),
        Latitude: 4.6534,
        Longitude: -74.0837,
        Altitude: 2601.5,
        Speed: 42.5,
        Course: 187.3,
        EventId: 7,
        Address: "Cll 100 # 8-20",
        City: "Bogota",
        State: "Bogota D.C.",
        Country: "CO",
        Attributes: null,
        IdempotencyKey: "idem-1");

    public static TelemetryModels.OperatorHealthCheckVm TelemetryHealthCheck() => default(TelemetryModels.OperatorHealthCheckVm) with
    {
        OperatorHealthCheckId = HealthCheckId,
        AccountId = AccountId,
        OperatorId = OperatorId,
        CheckType = OperatorHealthCheckType.Ping,
        Status = OperatorHealthStatus.Healthy,
        StartedAt = Timestamp,
    };

    public static TelemetryModels.OperatorSyncRunVm TelemetrySyncRun() => default(TelemetryModels.OperatorSyncRunVm) with
    {
        OperatorSyncRunId = SyncRunId,
        AccountId = AccountId,
        OperatorId = OperatorId,
        TriggerType = SyncTriggerType.Automatic,
        Result = OperatorSyncResult.Succeeded,
        StartedAt = Timestamp,
    };

    public static ManagerModels.OperatorVm ManagerOperator() => default(ManagerModels.OperatorVm) with
    {
        OperatorId = OperatorId,
        Name = "Operator-01",
        ProtocolTypeId = 3,
        AccountId = AccountId,
        Enabled = true,
        SyncIntervalMinutes = 30,
        HealthStatus = TrackHub.Manager.Domain.Enums.OperatorHealthStatus.Healthy,
        LastHealthCheckAt = Timestamp,
        LastManualSyncAt = Timestamp.AddMinutes(-5),
        LastDeviceSyncAt = Timestamp.AddMinutes(-10),
        LastPositionSyncAt = Timestamp.AddMinutes(-1),
        Credential = new ManagerModels.CredentialTokenVm(
            CredentialId: CredentialId,
            Uri: "https://provider.example.com/api",
            Username: "router-user",
            Password: "cipher-pass",
            Salt: "salt-value",
            Key: "key-1",
            Key2: "key-2",
            Token: "access-token",
            TokenExpiration: TokenExpiration,
            RefreshToken: "refresh-token",
            RefreshTokenExpiration: TokenExpiration.AddDays(30)),
    };

    public static ManagerModels.OperatorSyncRunVm ManagerSyncRun() => default(ManagerModels.OperatorSyncRunVm) with
    {
        OperatorSyncRunId = SyncRunId,
        AccountId = AccountId,
        OperatorId = OperatorId,
        StartedAt = Timestamp,
        DevicesSeen = 12,
        DevicesAdded = 3,
        DevicesUpdated = 4,
        DevicesRemoved = 2,
        DevicesIgnored = 3,
    };
}
