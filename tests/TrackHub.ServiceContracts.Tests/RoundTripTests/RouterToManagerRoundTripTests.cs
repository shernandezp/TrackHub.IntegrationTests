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

using Common.Domain.Constants;
using Common.Mediator;
using HotChocolate.Execution;
using ManagerApi;
using Moq;
using TrackHub.Manager.Application.AlertEvents.Commands;
using TrackHub.Manager.Application.GpsIntegration.Commands;
using TrackHub.Manager.Application.Operators.Queries.Get;
using TrackHub.Router.Infrastructure.ManagerApi;
using TrackHub.ServiceContracts.Harness;
using TrackHub.ServiceContracts.Tests.Harness;
using TrackHub.Router.Domain.Models;
using ManagerModels = TrackHub.Manager.Domain.Models;

namespace TrackHub.ServiceContracts.Tests.RoundTripTests;

// The Router's REAL readers/writers execute against Manager's REAL
// resolvers over an in-process executor; only the mediator behind the resolvers is faked.
// Covers the operator read (the credential-rich flow the sync pipeline depends on) and the
// device-sync mutation (the largest input payload the Router sends to Manager).
[TestFixture]
public class RouterToManagerRoundTripTests
{
    private Mock<ISender> _sender = null!;
    private InProcessGraphQLClientFactory _factory = null!;

    [OneTimeSetUp]
    public async Task BuildManagerExecutor()
    {
        _sender = new Mock<ISender>();
        var executor = await ProducerSchema.BuildManagerExecutorAsync(_sender.Object);
        _factory = new InProcessGraphQLClientFactory(
            new Dictionary<string, IRequestExecutor> { [Clients.Manager] = executor });
    }

    [SetUp]
    public void ResetSender() => _sender.Reset();

    [Test]
    public async Task GetOperator_RoundTripsCredentialAndHealthFieldsIntoRouterOperatorVm()
    {
        _sender
            .Setup(s => s.Send(It.IsAny<GetOperatorQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FakeData.ManagerOperator());

        var reader = new OperatorReader(_factory);
        var op = await reader.GetOperatorAsync(FakeData.OperatorId, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(op.OperatorId, Is.EqualTo(FakeData.OperatorId));
            Assert.That(op.ProtocolTypeId, Is.EqualTo(3));
            Assert.That(op.AccountId, Is.EqualTo(FakeData.AccountId));
            Assert.That(op.Enabled, Is.True);
            Assert.That(op.SyncIntervalMinutes, Is.EqualTo(30));
            Assert.That(op.HealthStatus, Is.EqualTo("HEALTHY"), "the Manager enum must deserialize into the Router's string field");
            Assert.That(op.LastHealthCheckAt, Is.EqualTo(FakeData.Timestamp));
            Assert.That(op.LastManualSyncAt, Is.EqualTo(FakeData.Timestamp.AddMinutes(-5)));
            Assert.That(op.LastDeviceSyncAt, Is.EqualTo(FakeData.Timestamp.AddMinutes(-10)));
            Assert.That(op.LastPositionSyncAt, Is.EqualTo(FakeData.Timestamp.AddMinutes(-1)));
            Assert.That(op.Credential, Is.Not.Null);
        }

        var credential = op.Credential!.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(credential.CredentialId, Is.EqualTo(FakeData.CredentialId));
            Assert.That(credential.Uri, Is.EqualTo("https://provider.example.com/api"));
            Assert.That(credential.Username, Is.EqualTo("router-user"));
            Assert.That(credential.Password, Is.EqualTo("cipher-pass"));
            Assert.That(credential.Salt, Is.EqualTo("salt-value"));
            Assert.That(credential.Key, Is.EqualTo("key-1"));
            Assert.That(credential.Key2, Is.EqualTo("key-2"));
            Assert.That(credential.Token, Is.EqualTo("access-token"));
            Assert.That(credential.RefreshToken, Is.EqualTo("refresh-token"));
            Assert.That(credential.TokenExpiration, Is.Not.Null,
                "Manager's DateTimeOffset must deserialize into the Router's DateTimeOffset field");
            Assert.That(credential.TokenExpiration!.Value.ToUniversalTime(),
                Is.EqualTo(FakeData.TokenExpiration.ToUniversalTime()));
        }

        _sender.Verify(s => s.Send(
            It.Is<GetOperatorQuery>(q => q.Id == FakeData.OperatorId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SynchronizeOperatorDevices_DeliversDevicePayloadAndReturnsCounts()
    {
        SynchronizeOperatorDevicesCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<SynchronizeOperatorDevicesCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ManagerModels.OperatorSyncRunVm>, CancellationToken>((cmd, _) =>
                received = (SynchronizeOperatorDevicesCommand)cmd)
            .ReturnsAsync(FakeData.ManagerSyncRun());

        var writer = new DeviceSyncWriter(_factory);
        var counts = await writer.SynchronizeAsync(
            FakeData.AccountId,
            FakeData.OperatorId,
            [
                new SynchronizedDeviceDto(
                    AccountId: FakeData.AccountId,
                    OperatorId: FakeData.OperatorId,
                    Serial: "SER-001",
                    Name: "Tracker 1",
                    Identifier: 4711,
                    ProviderDisplayName: "Tracker One",
                    DeviceTypeId: 2,
                    Description: "test device",
                    ProviderMetadataHash: "hash-1",
                    ProviderStatus: "active"),
            ],
            correlationId: "corr-sync-1",
            triggerType: "MANUAL",
            autoAssignNewDevices: true,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(counts.DevicesSeen, Is.EqualTo(12));
            Assert.That(counts.DevicesAdded, Is.EqualTo(3));
            Assert.That(counts.DevicesUpdated, Is.EqualTo(4));
            Assert.That(counts.DevicesRemoved, Is.EqualTo(2));
            Assert.That(counts.DevicesIgnored, Is.EqualTo(3));
        }

        Assert.That(received, Is.Not.Null, "the real SynchronizeOperatorDevicesCommand must reach the producer handler");
        var command = received!.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(command.AccountId, Is.EqualTo(FakeData.AccountId));
            Assert.That(command.OperatorId, Is.EqualTo(FakeData.OperatorId));
            Assert.That(command.CorrelationId, Is.EqualTo("corr-sync-1"));
            Assert.That(command.TriggerType, Is.EqualTo("MANUAL"));
            Assert.That(command.AutoAssignNewDevices, Is.True);
            Assert.That(command.Devices, Has.Count.EqualTo(1));
        }

        var device = command.Devices.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(device.Serial, Is.EqualTo("SER-001"));
            Assert.That(device.Name, Is.EqualTo("Tracker 1"));
            Assert.That(device.Identifier, Is.EqualTo(4711));
            Assert.That(device.DeviceTypeId, Is.EqualTo((short)2));
            Assert.That(device.ProviderDisplayName, Is.EqualTo("Tracker One"));
            Assert.That(device.ProviderMetadataHash, Is.EqualTo("hash-1"));
            Assert.That(device.ProviderStatus, Is.EqualTo("active"));
        }
    }

    // Layer B: the Router's alert emitter delivers every DTO field (severity/status
    // travel as string literals in variables) into the real RecordAlertEventCommand — the command
    // whose post-commit evaluation creates Pending deliveries (evaluation itself is covered by
    // Manager's AlertRuleEvaluator unit tests and the smoke flow).
    [Test]
    public async Task RecordAlertEvent_DeliversAlertFieldsIntoManagerCommand()
    {
        RecordAlertEventCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<RecordAlertEventCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ManagerModels.AlertEventVm>, CancellationToken>((cmd, _) =>
                received = (RecordAlertEventCommand)cmd)
            .ReturnsAsync(new ManagerModels.AlertEventVm(
                Guid.NewGuid(), FakeData.AccountId, "GpsOperatorPositionSyncFailed", "Warning", "Router",
                "Operator", FakeData.OperatorId.ToString(), "Open", FakeData.Timestamp, FakeData.Timestamp,
                null, "sync-failed:key", FakeData.Timestamp));

        var writer = new TrackHub.Router.Infrastructure.ManagerApi.AlertEventWriter(_factory);
        await writer.RecordAsync(
            new AlertEventDto(
                AccountId: FakeData.AccountId,
                EventType: "GpsOperatorPositionSyncFailed",
                Severity: "Warning",
                SourceModule: "Router",
                ResourceType: "Operator",
                ResourceId: FakeData.OperatorId.ToString(),
                Status: "Open",
                PayloadJson: "{\"error\":\"timeout\"}",
                DeduplicationKey: "sync-failed:key"),
            CancellationToken.None);

        Assert.That(received, Is.Not.Null, "the real RecordAlertEventCommand must reach the producer handler");
        var alertEvent = received!.Value.AlertEvent;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alertEvent.AccountId, Is.EqualTo(FakeData.AccountId));
            Assert.That(alertEvent.EventType, Is.EqualTo("GpsOperatorPositionSyncFailed"));
            Assert.That(alertEvent.Severity, Is.EqualTo("Warning"));
            Assert.That(alertEvent.SourceModule, Is.EqualTo("Router"));
            Assert.That(alertEvent.ResourceType, Is.EqualTo("Operator"));
            Assert.That(alertEvent.ResourceId, Is.EqualTo(FakeData.OperatorId.ToString()));
            Assert.That(alertEvent.Status, Is.EqualTo("Open"));
            Assert.That(alertEvent.PayloadJson, Is.EqualTo("{\"error\":\"timeout\"}"));
            Assert.That(alertEvent.DeduplicationKey, Is.EqualTo("sync-failed:key"));
        }
    }
}
