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

using System.Runtime.CompilerServices;
using System.Text.Json;
using Common.Domain.Constants;
using Common.Mediator;
using HotChocolate.Execution;
using Moq;
using TrackHub.Manager.Application.AlertEvents.Commands;
using TrackHub.Manager.Application.BackgroundJobs.Commands;
using TrackHub.Manager.Domain.Models;
using TrackHub.ServiceContracts.Harness;
using TrackHub.ServiceContracts.Tests.Harness;
using TrackHub.TripManagement.Application.Common;
using TrackHub.TripManagement.Domain.Constants;
using TrackHub.TripManagement.Domain.Records;
using ManagerAlertEventTypes = TrackHub.Manager.Domain.Constants.AlertEventTypes;
using ManagerAlertSeverities = TrackHub.Manager.Domain.Constants.AlertSeverities;
using TripAlertEmitter = TrackHub.TripManagement.Infrastructure.ManagerApi.AlertEmitter;

namespace TrackHub.ServiceContracts.Tests.RoundTripTests;

// The TripManagement service's REAL AlertEmitter pushes its mutation through Manager's REAL
// resolvers. Event-type and severity literals travel in GraphQL variables and are invisible to
// Layer A document validation — spec 11 §16 requires a round-trip case for EVERY new trip event
// type and severity literal, because a literal that exists here but not in Manager's
// AlertEventTypes catalog is exactly the drift this layer exists to catch (rules.md).
[TestFixture]
public class TripManagementToManagerRoundTripTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TripId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TripStopId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TransporterId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DriverId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset OccurredAt = new(2026, 7, 1, 12, 30, 45, TimeSpan.Zero);

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

    /// <summary>
    /// The ten trip alert literals spec 11 §12 declares, with the severity the emitting call site
    /// actually passes. Adding an emission without extending Manager's catalog fails here.
    /// </summary>
    private static IEnumerable<TestCaseData> TripAlerts()
    {
        yield return new TestCaseData(TripEventTypes.TripAssigned, TripAlertSeverities.Info);
        yield return new TestCaseData(TripEventTypes.TripStarted, TripAlertSeverities.Info);
        yield return new TestCaseData(TripEventTypes.TripStopArrived, TripAlertSeverities.Info);
        yield return new TestCaseData(TripEventTypes.TripStopDeparted, TripAlertSeverities.Info);
        yield return new TestCaseData(TripEventTypes.TripDelayed, TripAlertSeverities.Warning);
        yield return new TestCaseData(TripEventTypes.TripRouteDeviation, TripAlertSeverities.Warning);
        yield return new TestCaseData(TripEventTypes.TripPodSubmitted, TripAlertSeverities.Info);
        yield return new TestCaseData(TripEventTypes.TripCompleted, TripAlertSeverities.Info);
        yield return new TestCaseData(TripEventTypes.TripCancelled, TripAlertSeverities.Warning);
        yield return new TestCaseData(TripEventTypes.TripStartDue, TripAlertSeverities.Info);
    }

    [TestCaseSource(nameof(TripAlerts))]
    public async Task EmitAsync_DeliversTripEventTypeAndSeverityToManager(string eventType, string severity)
    {
        var received = SetupRecordAlertEvent();
        var deduplicationKey = $"trip-{eventType.ToLowerInvariant()}:{TripId:N}";

        var emitter = new TripAlertEmitter(_factory);
        await emitter.EmitAsync(eventType, severity, deduplicationKey, NewAlert(), CancellationToken.None);

        Assert.That(received.Value, Is.Not.Null, "the real RecordAlertEventCommand must reach the Manager handler");
        var alertEvent = received.Value!.Value.AlertEvent;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alertEvent.AccountId, Is.EqualTo(AccountId));
            Assert.That(alertEvent.EventType, Is.EqualTo(eventType));
            Assert.That(alertEvent.Severity, Is.EqualTo(severity));
            Assert.That(alertEvent.SourceModule, Is.EqualTo(TripSharing.SourceModule));
            Assert.That(alertEvent.ResourceType, Is.EqualTo(TripSharing.ResourceType));
            Assert.That(alertEvent.ResourceId, Is.EqualTo(TripId.ToString()));
            Assert.That(alertEvent.Status, Is.EqualTo("Open"));
            Assert.That(alertEvent.DeduplicationKey, Is.EqualTo(deduplicationKey));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ManagerAlertEventTypes.All, Does.Contain(eventType),
                $"'{eventType}' is emitted by TrackHub.TripManagement but is not in Manager's AlertEventTypes catalog — "
                + "alert rules could never be configured for it.");
            Assert.That(
                new[] { ManagerAlertSeverities.Info, ManagerAlertSeverities.Warning, ManagerAlertSeverities.High, ManagerAlertSeverities.Critical },
                Does.Contain(severity),
                $"'{severity}' is not a Manager AlertSeverities literal.");
        }
    }

    [TestCaseSource(nameof(TripAlerts))]
    public async Task EmitAsync_DeliversPayloadJsonForEachTripEventType(string eventType, string severity)
    {
        var received = SetupRecordAlertEvent();

        var emitter = new TripAlertEmitter(_factory);
        await emitter.EmitAsync(eventType, severity, $"trip-x:{TripId:N}", NewAlert(), CancellationToken.None);

        Assert.That(received.Value, Is.Not.Null);
        var payload = JsonSerializer.Deserialize<JsonElement>(received.Value!.Value.AlertEvent.PayloadJson!);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(payload.GetProperty("accountId").GetGuid(), Is.EqualTo(AccountId));
            Assert.That(payload.GetProperty("tripId").GetGuid(), Is.EqualTo(TripId));
            Assert.That(payload.GetProperty("tripStopId").GetGuid(), Is.EqualTo(TripStopId));
            Assert.That(payload.GetProperty("tripCode").GetString(), Is.EqualTo("TRIP-0001"));
            Assert.That(payload.GetProperty("transporterId").GetGuid(), Is.EqualTo(TransporterId));
            Assert.That(payload.GetProperty("driverId").GetGuid(), Is.EqualTo(DriverId));
            Assert.That(payload.GetProperty("delayMinutes").GetInt32(), Is.EqualTo(17));
            Assert.That(payload.GetProperty("occurredAt").GetDateTimeOffset(), Is.EqualTo(OccurredAt));
        }
    }

    // The reverse direction of the same drift check: Manager must not carry a Trip* alert type
    // that nothing emits, and must not be missing one that this module does emit.
    [Test]
    public void ManagerTripAlertCatalog_MatchesTheEmittedLiteralsExactly()
    {
        var emitted = TripAlerts().Select(c => (string)c.Arguments[0]!).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        var catalogued = ManagerAlertEventTypes.All
            .Where(v => v.StartsWith("Trip", StringComparison.Ordinal))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

        Assert.That(catalogued, Is.EqualTo(emitted),
            "Manager's AlertEventTypes trip entries and the literals TripManagement emits have drifted apart.");
    }

    [Test]
    public async Task RecordBackgroundJobRun_DeliversJobKeyAccountAndIdempotencyKey()
    {
        CreateBackgroundJobRunCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<CreateBackgroundJobRunCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<BackgroundJobRunVm>, CancellationToken>((cmd, _) => received = (CreateBackgroundJobRunCommand)cmd)
            .ReturnsAsync(new BackgroundJobRunVm(
                Guid.NewGuid(), "trip-eta-refresh", AccountId, null, "trip-eta:20260701123045000",
                "Succeeded", 1, OccurredAt, OccurredAt.AddSeconds(3), null, null));

        var recorder = new TrackHub.TripManagement.Infrastructure.ManagerApi.BackgroundJobRunRecorder(_factory);
        await recorder.RecordAsync(
            "trip-eta-refresh",
            AccountId,
            null,
            "trip-eta:20260701123045000",
            "Succeeded",
            OccurredAt,
            OccurredAt.AddSeconds(3),
            null,
            null,
            CancellationToken.None);

        Assert.That(received, Is.Not.Null, "the real CreateBackgroundJobRunCommand must reach the Manager handler");
        var run = received!.Value.BackgroundJobRun;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(run.JobKey, Is.EqualTo("trip-eta-refresh"));
            Assert.That(run.AccountId, Is.EqualTo(AccountId));
            Assert.That(run.IdempotencyKey, Is.EqualTo("trip-eta:20260701123045000"));
            Assert.That(run.Status, Is.EqualTo("Succeeded"));
            Assert.That(run.Attempts, Is.EqualTo(1));
            Assert.That(run.StartedAt, Is.EqualTo(OccurredAt));
            Assert.That(run.CompletedAt, Is.EqualTo(OccurredAt.AddSeconds(3)));
        }
    }

    private StrongBox<RecordAlertEventCommand?> SetupRecordAlertEvent()
    {
        var received = new StrongBox<RecordAlertEventCommand?>();
        _sender
            .Setup(s => s.Send(It.IsAny<RecordAlertEventCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<AlertEventVm>, CancellationToken>((cmd, _) => received.Value = (RecordAlertEventCommand)cmd)
            .ReturnsAsync(new AlertEventVm(
                Guid.NewGuid(), AccountId, TripEventTypes.TripAssigned, ManagerAlertSeverities.Info,
                TripSharing.SourceModule, TripSharing.ResourceType, TripId.ToString(), "Open",
                OccurredAt, OccurredAt, null, "dedup", OccurredAt));
        return received;
    }

    private static TripAlertDto NewAlert()
        => new(AccountId,
            TripId,
            TripStopId,
            "TRIP-0001",
            TransporterId,
            DriverId,
            "Bodega Norte",
            OccurredAt,
            OccurredAt.AddMinutes(20),
            OccurredAt.AddMinutes(3),
            17,
            4.6534,
            -74.0837);
}
