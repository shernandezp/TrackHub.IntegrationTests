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
using TrackHub.Geofencing.Domain.Records;
using TrackHub.Geofencing.Infrastructure.ManagerApi;
using TrackHub.Manager.Application.AlertEvents.Commands;
using TrackHub.Manager.Application.BackgroundJobs.Commands;
using TrackHub.Manager.Domain.Models;
using TrackHub.ServiceContracts.Harness;
using TrackHub.ServiceContracts.Tests.Harness;

namespace TrackHub.ServiceContracts.Tests.RoundTripTests;

// The Geofencing service's REAL AlertEmitter/BackgroundJobRunRecorder push their mutations
// through Manager's REAL resolvers. The event-type/severity/status literals travel in GraphQL
// variables and are invisible to Layer A document validation — this round-trip asserts they
// arrive intact in the real Manager commands (rules.md, inter-service client rules).
[TestFixture]
public class GeofencingToManagerRoundTripTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TransporterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GeofenceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid GeofenceEventId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset EnteredAt = new(2026, 7, 1, 12, 30, 45, TimeSpan.Zero);

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
    public async Task EmitGeofenceEntered_DeliversAlertEventWithLiteralsAndDedupKey()
    {
        var received = SetupRecordAlertEvent();

        var emitter = new AlertEmitter(_factory);
        await emitter.EmitGeofenceEnteredAsync(NewAlert(dwellSeconds: null, exitedAt: null), CancellationToken.None);

        Assert.That(received.Value, Is.Not.Null, "the real RecordAlertEventCommand must reach the Manager handler");
        var alertEvent = received.Value!.Value.AlertEvent;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alertEvent.AccountId, Is.EqualTo(AccountId));
            Assert.That(alertEvent.EventType, Is.EqualTo("GeofenceEntered"));
            Assert.That(alertEvent.Severity, Is.EqualTo("Info"));
            Assert.That(alertEvent.SourceModule, Is.EqualTo("TrackHub.Geofencing"));
            Assert.That(alertEvent.ResourceType, Is.EqualTo("Geofence"));
            Assert.That(alertEvent.ResourceId, Is.EqualTo(GeofenceId.ToString()));
            Assert.That(alertEvent.Status, Is.EqualTo("Open"));
            Assert.That(alertEvent.DeduplicationKey, Is.EqualTo($"geofence-enter:{GeofenceEventId:N}"));
        }

        var payload = JsonSerializer.Deserialize<JsonElement>(alertEvent.PayloadJson!);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(payload.GetProperty("transporterId").GetGuid(), Is.EqualTo(TransporterId));
            Assert.That(payload.GetProperty("geofenceName").GetString(), Is.EqualTo("Main Depot"));
            Assert.That(payload.GetProperty("geofenceEventId").GetGuid(), Is.EqualTo(GeofenceEventId));
        }
    }

    [Test]
    public async Task EmitGeofenceExited_DeliversDwellSecondsAndExitDedupKey()
    {
        var received = SetupRecordAlertEvent();

        var emitter = new AlertEmitter(_factory);
        await emitter.EmitGeofenceExitedAsync(
            NewAlert(dwellSeconds: 1830, exitedAt: EnteredAt.AddSeconds(1830)), CancellationToken.None);

        Assert.That(received.Value, Is.Not.Null);
        var alertEvent = received.Value!.Value.AlertEvent;
        var payload = JsonSerializer.Deserialize<JsonElement>(alertEvent.PayloadJson!);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alertEvent.EventType, Is.EqualTo("GeofenceExited"));
            Assert.That(alertEvent.Severity, Is.EqualTo("Info"));
            Assert.That(alertEvent.DeduplicationKey, Is.EqualTo($"geofence-exit:{GeofenceEventId:N}"));
            Assert.That(payload.GetProperty("dwellSeconds").GetInt64(), Is.EqualTo(1830));
        }
    }

    [Test]
    public async Task EmitGeofenceDwellExceeded_DeliversWarningSeverityAndDwellDedupKey()
    {
        var received = SetupRecordAlertEvent();

        var emitter = new AlertEmitter(_factory);
        await emitter.EmitGeofenceDwellExceededAsync(
            NewAlert(dwellSeconds: 5400, exitedAt: null), CancellationToken.None);

        Assert.That(received.Value, Is.Not.Null);
        var alertEvent = received.Value!.Value.AlertEvent;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alertEvent.EventType, Is.EqualTo("GeofenceDwellExceeded"));
            Assert.That(alertEvent.Severity, Is.EqualTo("Warning"));
            Assert.That(alertEvent.DeduplicationKey, Is.EqualTo($"geofence-dwell:{GeofenceEventId:N}"));
        }
    }

    [Test]
    public async Task RecordBackgroundJobRun_DeliversJobKeyStatusAndIdempotencyKey()
    {
        CreateBackgroundJobRunCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<CreateBackgroundJobRunCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<BackgroundJobRunVm>, CancellationToken>((cmd, _) => received = (CreateBackgroundJobRunCommand)cmd)
            .ReturnsAsync(new BackgroundJobRunVm(
                Guid.NewGuid(), "geofence-dwell-evaluation", null, "3", "geofence-dwell:20260701123045000",
                "Succeeded", 1, EnteredAt, EnteredAt.AddSeconds(2), null, null));

        var recorder = new BackgroundJobRunRecorder(_factory);
        await recorder.RecordAsync(
            "geofence-dwell-evaluation",
            "3",
            "geofence-dwell:20260701123045000",
            "Succeeded",
            EnteredAt,
            EnteredAt.AddSeconds(2),
            CancellationToken.None);

        Assert.That(received, Is.Not.Null, "the real CreateBackgroundJobRunCommand must reach the Manager handler");
        var run = received!.Value.BackgroundJobRun;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(run.JobKey, Is.EqualTo("geofence-dwell-evaluation"));
            Assert.That(run.AccountId, Is.Null);
            Assert.That(run.ResourceKey, Is.EqualTo("3"));
            Assert.That(run.IdempotencyKey, Is.EqualTo("geofence-dwell:20260701123045000"));
            Assert.That(run.Status, Is.EqualTo("Succeeded"));
            Assert.That(run.Attempts, Is.EqualTo(1));
            Assert.That(run.StartedAt, Is.EqualTo(EnteredAt));
            Assert.That(run.CompletedAt, Is.EqualTo(EnteredAt.AddSeconds(2)));
        }
    }

    private StrongBox<RecordAlertEventCommand?> SetupRecordAlertEvent()
    {
        var received = new StrongBox<RecordAlertEventCommand?>();
        _sender
            .Setup(s => s.Send(It.IsAny<RecordAlertEventCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<AlertEventVm>, CancellationToken>((cmd, _) => received.Value = (RecordAlertEventCommand)cmd)
            .ReturnsAsync(new AlertEventVm(
                Guid.NewGuid(), AccountId, "GeofenceEntered", "Info", "TrackHub.Geofencing", "Geofence",
                GeofenceId.ToString(), "Open", EnteredAt, EnteredAt, null, "dedup", EnteredAt));
        return received;
    }

    private static GeofenceAlertDto NewAlert(long? dwellSeconds, DateTimeOffset? exitedAt)
        => new(GeofenceEventId,
            AccountId,
            TransporterId,
            GeofenceId,
            "Main Depot",
            2,
            EnteredAt,
            exitedAt,
            dwellSeconds,
            4.6534,
            -74.0837);
}
