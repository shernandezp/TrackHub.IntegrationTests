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
using Moq;
using TrackHub.Router.Infrastructure.TelemetryApi;
using TrackHub.ServiceContracts.Harness;
using TrackHub.ServiceContracts.Tests.Harness;
using TrackHub.Telemetry.Application.GpsIntegration.Commands;
using TrackHub.Telemetry.Application.GpsIntegration.Queries;
using TrackHub.Telemetry.Application.TransporterPosition.Commands.Create;
using TrackHub.Telemetry.Application.TransporterPosition.Queries.GetByOperator;
using TrackHub.Telemetry.Domain.Enums;
using TrackHub.Router.Domain.Models;
using TelemetryModels = TrackHub.Telemetry.Domain.Models;
using TelemetryRecords = TrackHub.Telemetry.Domain.Records;

namespace TrackHub.ServiceContracts.Tests.RoundTripTests;

// The Router's REAL readers/writers execute against Telemetry's REAL
// resolvers over an in-process executor; only the mediator behind the resolvers is faked.
// This catches drift Layer A cannot: enum/UUID/DateTime serialization, casing, and the
// field-to-property mapping on both sides of the positions/history/health/sync-run flows.
[TestFixture]
public class RouterToTelemetryRoundTripTests
{
    private Mock<ISender> _sender = null!;
    private InProcessGraphQLClientFactory _factory = null!;

    [OneTimeSetUp]
    public async Task BuildTelemetryExecutor()
    {
        _sender = new Mock<ISender>();
        var executor = await ProducerSchema.BuildTelemetryExecutorAsync(_sender.Object);
        _factory = new InProcessGraphQLClientFactory(
            new Dictionary<string, IRequestExecutor> { [Clients.Telemetry] = executor });
    }

    [SetUp]
    public void ResetSender() => _sender.Reset();

    [Test]
    public async Task GetTransporterPositions_RoundTripsThroughRealSchemaIntoRouterPositionVm()
    {
        _sender
            .Setup(s => s.Send(It.IsAny<GetTransporterPositionsByOperatorQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([FakeData.TelemetryPosition()]);

        var reader = new TransporterPositionReader(_factory);
        var positions = (await reader.GetTransporterPositionAsync(FakeData.OperatorId, CancellationToken.None)).ToList();

        Assert.That(positions, Has.Count.EqualTo(1));
        var position = positions[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(position.TransporterId, Is.EqualTo(FakeData.TransporterId));
            Assert.That(position.DeviceName, Is.EqualTo("Device-01"));
            Assert.That(position.TransporterType, Is.EqualTo("CAR"), "GraphQL enum name must deserialize into the Router's string field");
            Assert.That(position.Latitude, Is.EqualTo(4.6534));
            Assert.That(position.Longitude, Is.EqualTo(-74.0837));
            Assert.That(position.Altitude, Is.EqualTo(2601.5));
            Assert.That(position.DeviceDateTime, Is.EqualTo(FakeData.Timestamp));
            Assert.That(position.Speed, Is.EqualTo(42.5));
            Assert.That(position.Course, Is.EqualTo(187.3));
            Assert.That(position.EventId, Is.EqualTo(7));
            Assert.That(position.Address, Is.EqualTo("Cll 100 # 8-20"));
            Assert.That(position.City, Is.EqualTo("Bogota"));
            Assert.That(position.Country, Is.EqualTo("CO"));
            Assert.That(position.Attributes, Is.Not.Null);
            Assert.That(position.Attributes!.Value.Satellites, Is.EqualTo(12));
            Assert.That(position.Attributes!.Value.Ignition, Is.True);
            Assert.That(position.Attributes!.Value.Mileage, Is.EqualTo(12345.6));
            Assert.That(position.Attributes!.Value.Hourmeter, Is.EqualTo(220.5));
            Assert.That(position.Attributes!.Value.Temperature, Is.EqualTo(21.5));
            // Open attribute bag round-trips as a JSON string through Telemetry's `extra: String`
            // output and the Router's read query subselection (router-audit A-03).
            Assert.That(position.Attributes!.Value.Extra, Is.EqualTo(FakeData.ExtraJson));
        }

        _sender.Verify(s => s.Send(
            It.Is<GetTransporterPositionsByOperatorQuery>(q => q.OperatorId == FakeData.OperatorId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetPositionHistoryRange_RoundTripsRowsAndVariableCoercion()
    {
        _sender
            .Setup(s => s.Send(It.IsAny<GetPositionHistoryRangeQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([FakeData.TelemetryHistoryRow()]);

        var reader = new PositionHistoryReader(_factory);
        var from = FakeData.Timestamp.AddHours(-2);
        var to = FakeData.Timestamp;
        var positions = (await reader.GetPositionHistoryRangeAsync(
            FakeData.AccountId, FakeData.TransporterId, from, to, CancellationToken.None)).ToList();

        Assert.That(positions, Has.Count.EqualTo(1));
        var position = positions[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(position.TransporterId, Is.EqualTo(FakeData.TransporterId));
            Assert.That(position.DeviceDateTime, Is.EqualTo(FakeData.Timestamp), "sourceTimestamp must map onto DeviceDateTime");
            Assert.That(position.Latitude, Is.EqualTo(4.6534));
            Assert.That(position.Speed, Is.EqualTo(42.5));
            Assert.That(position.Course, Is.EqualTo(187.3));
            Assert.That(position.EventId, Is.EqualTo(7));
            Assert.That(position.Address, Is.EqualTo("Cll 100 # 8-20"));
            Assert.That(position.State, Is.EqualTo("Bogota D.C."));
        }

        // The query's UUID/DateTime/Int variables must coerce into the producer's request type.
        _sender.Verify(s => s.Send(
            It.Is<GetPositionHistoryRangeQuery>(q =>
                q.AccountId == FakeData.AccountId
                && q.TransporterId == FakeData.TransporterId
                && q.From == from
                && q.To == to
                && q.MaxPoints == 10000),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RecordOperatorHealth_CoercesRouterStringsIntoTelemetryEnums()
    {
        TelemetryRecords.OperatorHealthCheckDto? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<RecordOperatorHealthCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<TelemetryModels.OperatorHealthCheckVm>, CancellationToken>((cmd, _) =>
                received = ((RecordOperatorHealthCommand)cmd).Check)
            .ReturnsAsync(FakeData.TelemetryHealthCheck());

        var writer = new OperatorHealthCheckWriter(_factory);
        await writer.RecordAsync(new OperatorHealthCheckDto(
            AccountId: FakeData.AccountId,
            OperatorId: FakeData.OperatorId,
            CheckType: "PING",
            Status: "HEALTHY",
            LatencyMs: 120,
            StartedAt: FakeData.Timestamp,
            CompletedAt: FakeData.Timestamp.AddSeconds(1),
            ErrorCode: null,
            ErrorMessage: null,
            RetryCount: 2,
            CorrelationId: "corr-1"), CancellationToken.None);

        Assert.That(received, Is.Not.Null, "the real RecordOperatorHealthCommand must reach the producer handler");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Value.AccountId, Is.EqualTo(FakeData.AccountId));
            Assert.That(received!.Value.OperatorId, Is.EqualTo(FakeData.OperatorId));
            Assert.That(received!.Value.CheckType, Is.EqualTo(OperatorHealthCheckType.Ping), "the Router's string must coerce into the Telemetry enum");
            Assert.That(received!.Value.Status, Is.EqualTo(OperatorHealthStatus.Healthy));
            Assert.That(received!.Value.LatencyMs, Is.EqualTo(120));
            Assert.That(received!.Value.StartedAt, Is.EqualTo(FakeData.Timestamp));
            Assert.That(received!.Value.RetryCount, Is.EqualTo(2));
            Assert.That(received!.Value.CorrelationId, Is.EqualTo("corr-1"));
        }
    }

    // Every checkType literal the Router actually SENDS must be a valid Telemetry enum value.
    // Enum values travel in GraphQL variables, so the Layer A document validation cannot catch
    // an invalid literal — this round trip does (regression: "SYNC" was rejected at coercion,
    // silently killing the sync-recorded health observation).
    [TestCase("PING", OperatorHealthCheckType.Ping)]
    [TestCase("DEVICE_SYNC", OperatorHealthCheckType.DeviceSync)]
    [TestCase("POSITION_SYNC", OperatorHealthCheckType.PositionSync)]
    [TestCase("TOKEN_REFRESH", OperatorHealthCheckType.TokenRefresh)]
    public async Task RecordOperatorHealth_EveryRouterCheckTypeLiteral_CoercesIntoTheTelemetryEnum(
        string checkType, OperatorHealthCheckType expected)
    {
        TelemetryRecords.OperatorHealthCheckDto? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<RecordOperatorHealthCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<TelemetryModels.OperatorHealthCheckVm>, CancellationToken>((cmd, _) =>
                received = ((RecordOperatorHealthCommand)cmd).Check)
            .ReturnsAsync(FakeData.TelemetryHealthCheck());

        var writer = new OperatorHealthCheckWriter(_factory);
        await writer.RecordAsync(new OperatorHealthCheckDto(
            AccountId: FakeData.AccountId,
            OperatorId: FakeData.OperatorId,
            CheckType: checkType,
            Status: "OFFLINE",
            LatencyMs: 5,
            StartedAt: FakeData.Timestamp,
            CompletedAt: FakeData.Timestamp.AddSeconds(1),
            ErrorCode: "ProviderUnreachable",
            ErrorMessage: "probe failed",
            RetryCount: 0,
            CorrelationId: "corr-enum"), CancellationToken.None);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.CheckType, Is.EqualTo(expected));
        Assert.That(received!.Value.Status, Is.EqualTo(OperatorHealthStatus.Offline));
    }

    [Test]
    public async Task RecordOperatorSyncRun_CoercesRouterStringsIntoTelemetryEnums()
    {
        TelemetryRecords.OperatorSyncRunDto? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<RecordOperatorSyncRunCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<TelemetryModels.OperatorSyncRunVm>, CancellationToken>((cmd, _) =>
                received = ((RecordOperatorSyncRunCommand)cmd).Run)
            .ReturnsAsync(FakeData.TelemetrySyncRun());

        var writer = new OperatorSyncRunWriter(_factory);
        await writer.RecordAsync(new OperatorSyncRunDto(
            AccountId: FakeData.AccountId,
            OperatorId: FakeData.OperatorId,
            TriggerType: "AUTOMATIC",
            Result: "SUCCEEDED",
            StartedAt: FakeData.Timestamp,
            CompletedAt: FakeData.Timestamp.AddSeconds(30),
            DevicesSeen: 12,
            DevicesAdded: 3,
            DevicesUpdated: 4,
            DevicesRemoved: 2,
            DevicesIgnored: 3,
            PositionsRead: 100,
            PositionsAccepted: 95,
            PositionsRejected: 5,
            ErrorCode: null,
            ErrorMessage: null,
            CorrelationId: "corr-2"), CancellationToken.None);

        Assert.That(received, Is.Not.Null, "the real RecordOperatorSyncRunCommand must reach the producer handler");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Value.TriggerType, Is.EqualTo(SyncTriggerType.Automatic));
            Assert.That(received!.Value.Result, Is.EqualTo(OperatorSyncResult.Succeeded));
            Assert.That(received!.Value.DevicesSeen, Is.EqualTo(12));
            Assert.That(received!.Value.PositionsAccepted, Is.EqualTo(95));
            Assert.That(received!.Value.StartedAt, Is.EqualTo(FakeData.Timestamp));
            Assert.That(received!.Value.CompletedAt, Is.EqualTo(FakeData.Timestamp.AddSeconds(30)));
            Assert.That(received!.Value.CorrelationId, Is.EqualTo("corr-2"));
        }
    }

    // Every (triggerType, result) literal pair the Router actually sends must coerce into the
    // Telemetry enums — enum values travel in variables, invisible to Layer A validation.
    [TestCase("MANUAL", SyncTriggerType.Manual, "SUCCEEDED", OperatorSyncResult.Succeeded)]
    [TestCase("MANUAL", SyncTriggerType.Manual, "FAILED", OperatorSyncResult.Failed)]
    [TestCase("AUTOMATIC", SyncTriggerType.Automatic, "FAILED", OperatorSyncResult.Failed)]
    public async Task RecordOperatorSyncRun_EveryRouterLiteral_CoercesIntoTheTelemetryEnums(
        string triggerType, SyncTriggerType expectedTrigger, string result, OperatorSyncResult expectedResult)
    {
        TelemetryRecords.OperatorSyncRunDto? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<RecordOperatorSyncRunCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<TelemetryModels.OperatorSyncRunVm>, CancellationToken>((cmd, _) =>
                received = ((RecordOperatorSyncRunCommand)cmd).Run)
            .ReturnsAsync(FakeData.TelemetrySyncRun());

        var writer = new OperatorSyncRunWriter(_factory);
        await writer.RecordAsync(new OperatorSyncRunDto(
            AccountId: FakeData.AccountId,
            OperatorId: FakeData.OperatorId,
            TriggerType: triggerType,
            Result: result,
            StartedAt: FakeData.Timestamp,
            CompletedAt: FakeData.Timestamp.AddSeconds(5),
            DevicesSeen: 0,
            DevicesAdded: 0,
            DevicesUpdated: 0,
            DevicesRemoved: 0,
            DevicesIgnored: 0,
            PositionsRead: 0,
            PositionsAccepted: 0,
            PositionsRejected: 0,
            ErrorCode: result == "FAILED" ? "ProviderUnreachable" : null,
            ErrorMessage: result == "FAILED" ? "boom" : null,
            CorrelationId: "corr-enum"), CancellationToken.None);

        Assert.That(received, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Value.TriggerType, Is.EqualTo(expectedTrigger));
            Assert.That(received!.Value.Result, Is.EqualTo(expectedResult));
        }
    }

    [Test]
    public async Task BulkTransporterPosition_DeliversPositionsToTheProducerCommand()
    {
        BulkTransporterPositionCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<BulkTransporterPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest, CancellationToken>((cmd, _) => received = (BulkTransporterPositionCommand)cmd)
            .Returns(Task.CompletedTask);

        var writer = new PositionWriter(_factory);
        var ok = await writer.AddOrUpdatePositionAsync(
            [
                new PositionVm(
                    TransporterId: FakeData.TransporterId,
                    DeviceName: "Device-01",
                    TransporterType: "CAR",
                    Latitude: 4.6534,
                    Longitude: -74.0837,
                    Altitude: 2601.5,
                    DeviceDateTime: FakeData.Timestamp,
                    ServerDateTime: null,
                    Speed: 42.5,
                    Course: 187.3,
                    EventId: 7,
                    Address: "Cll 100 # 8-20",
                    City: "Bogota",
                    State: "Bogota D.C.",
                    Country: "CO",
                    Attributes: new AttributesVm(
                        Ignition: true,
                        Satellites: 12,
                        Mileage: 12345.6,
                        Hourmeter: 220.5,
                        Temperature: 21.5,
                        Extra: FakeData.ExtraJson)),
            ], CancellationToken.None);

        Assert.That(ok, Is.True);
        Assert.That(received, Is.Not.Null, "the real BulkTransporterPositionCommand must reach the producer handler");
        var rows = received!.Value.Positions.ToList();
        Assert.That(rows, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows[0].TransporterId, Is.EqualTo(FakeData.TransporterId));
            Assert.That(rows[0].DeviceDateTime, Is.EqualTo(FakeData.Timestamp));
            Assert.That(rows[0].Latitude, Is.EqualTo(4.6534));
            Assert.That(rows[0].Speed, Is.EqualTo(42.5));
            Assert.That(rows[0].Attributes, Is.Not.Null);
            Assert.That(rows[0].Attributes!.Value.Satellites, Is.EqualTo(12));
            // The open attribute bag is accepted by Telemetry's AttributesDtoInput.extra and
            // coerces into the producer command (router-audit A-03).
            Assert.That(rows[0].Attributes!.Value.Extra, Is.EqualTo(FakeData.ExtraJson));
        }
    }
}
