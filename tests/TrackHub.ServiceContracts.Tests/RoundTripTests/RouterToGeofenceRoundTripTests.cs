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
using TrackHub.Geofencing.Application.GeofenceEvents.Commands.ProcessPositions;
using TrackHub.Router.Infrastructure.GeofenceApi;
using TrackHub.ServiceContracts.Harness;
using TrackHub.ServiceContracts.Tests.Harness;
using GeofenceResultVm = TrackHub.Geofencing.Domain.Models.GeofenceProcessingResultVm;
using RouterModels = TrackHub.Router.Domain.Models;

namespace TrackHub.ServiceContracts.Tests.RoundTripTests;

// The Router's REAL GeofenceWriter pushes a position batch through
// the Geofence service's REAL resolvers — the hot-path feed of geofence event detection.
[TestFixture]
public class RouterToGeofenceRoundTripTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TransporterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 1, 12, 30, 45, TimeSpan.Zero);

    private Mock<ISender> _sender = null!;
    private InProcessGraphQLClientFactory _factory = null!;

    [OneTimeSetUp]
    public async Task BuildGeofenceExecutor()
    {
        _sender = new Mock<ISender>();
        var executor = await ProducerSchema.BuildGeofenceExecutorAsync(_sender.Object);
        _factory = new InProcessGraphQLClientFactory(
            new Dictionary<string, IRequestExecutor> { [Clients.Geofence] = executor });
    }

    [SetUp]
    public void ResetSender() => _sender.Reset();

    [Test]
    public async Task ProcessPositions_DeliversBatchAndReturnsProcessingCounts()
    {
        ProcessPositionsCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<ProcessPositionsCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<GeofenceResultVm>, CancellationToken>((cmd, _) => received = (ProcessPositionsCommand)cmd)
            .ReturnsAsync(new GeofenceResultVm(ProcessedCount: 2, EventsCreated: 1, EventsUpdated: 1));

        var writer = new GeofenceWriter(_factory);
        var result = await writer.ProcessPositionsAsync(
            [
                default(RouterModels.PositionVm) with
                {
                    TransporterId = TransporterId,
                    Latitude = 4.6534,
                    Longitude = -74.0837,
                    DeviceDateTime = Timestamp,
                },
                default(RouterModels.PositionVm) with
                {
                    TransporterId = TransporterId,
                    Latitude = 4.6600,
                    Longitude = -74.0900,
                    DeviceDateTime = Timestamp.AddMinutes(1),
                },
            ],
            AccountId,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ProcessedCount, Is.EqualTo(2));
            Assert.That(result.EventsCreated, Is.EqualTo(1));
            Assert.That(result.EventsUpdated, Is.EqualTo(1));
        }

        Assert.That(received, Is.Not.Null, "the real ProcessPositionsCommand must reach the Geofence handler");
        var rows = received!.Value.Positions.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Value.AccountId, Is.EqualTo(AccountId));
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].TransporterId, Is.EqualTo(TransporterId));
            Assert.That(rows[0].Latitude, Is.EqualTo(4.6534));
            Assert.That(rows[0].Longitude, Is.EqualTo(-74.0837));
            Assert.That(rows[0].DeviceDateTime, Is.EqualTo(Timestamp));
            Assert.That(rows[1].DeviceDateTime, Is.EqualTo(Timestamp.AddMinutes(1)));
        }
    }
}
