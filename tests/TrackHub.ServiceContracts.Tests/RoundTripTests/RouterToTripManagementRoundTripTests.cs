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
using TrackHub.Router.Infrastructure.TripApi;
using TrackHub.ServiceContracts.Harness;
using TrackHub.ServiceContracts.Tests.Harness;
using TrackHub.TripManagement.Application.TripEvents.Commands.ProcessTripPositions;
using RouterModels = TrackHub.Router.Domain.Models;
using TripResultVm = TrackHub.TripManagement.Domain.Models.TripProcessingResultVm;

namespace TrackHub.ServiceContracts.Tests.RoundTripTests;

// The Router's REAL TripPositionWriter pushes a position batch through TripManagement's REAL
// resolvers — the hot-path feed behind stop arrival/departure detection and corridor deviation.
// Mandatory per spec 11 §16: the batch payload travels entirely in GraphQL variables, which
// Layer A document validation cannot see.
[TestFixture]
public class RouterToTripManagementRoundTripTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TransporterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 1, 12, 30, 45, TimeSpan.Zero);

    private Mock<ISender> _sender = null!;
    private InProcessGraphQLClientFactory _factory = null!;

    [OneTimeSetUp]
    public async Task BuildTripManagementExecutor()
    {
        _sender = new Mock<ISender>();
        var executor = await ProducerSchema.BuildTripManagementExecutorAsync(_sender.Object);
        _factory = new InProcessGraphQLClientFactory(
            new Dictionary<string, IRequestExecutor> { [Clients.TripManagement] = executor });
    }

    [SetUp]
    public void ResetSender() => _sender.Reset();

    [Test]
    public async Task ProcessTripPositions_DeliversBatchAndReturnsProcessingCounts()
    {
        ProcessTripPositionsCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<ProcessTripPositionsCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<TripResultVm>, CancellationToken>((cmd, _) => received = (ProcessTripPositionsCommand)cmd)
            .ReturnsAsync(new TripResultVm(ProcessedCount: 2, StopsArrived: 1, StopsDeparted: 1, DeviationsRaised: 1));

        var writer = new TripPositionWriter(_factory);
        var result = await writer.ProcessTripPositionsAsync(
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
            Assert.That(result.StopsArrived, Is.EqualTo(1));
            Assert.That(result.StopsDeparted, Is.EqualTo(1));
            Assert.That(result.DeviationsRaised, Is.EqualTo(1));
        }

        Assert.That(received, Is.Not.Null, "the real ProcessTripPositionsCommand must reach the TripManagement handler");
        var rows = received!.Value.Positions.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Value.AccountId, Is.EqualTo(AccountId));
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].TransporterId, Is.EqualTo(TransporterId));
            Assert.That(rows[0].Latitude, Is.EqualTo(4.6534));
            Assert.That(rows[0].Longitude, Is.EqualTo(-74.0837));
            Assert.That(rows[0].DeviceDateTime, Is.EqualTo(Timestamp));
            Assert.That(rows[1].Latitude, Is.EqualTo(4.6600));
            Assert.That(rows[1].DeviceDateTime, Is.EqualTo(Timestamp.AddMinutes(1)));
        }
    }

    // An empty cycle is a normal Router outcome (nothing moved). It must still coerce and return
    // a result rather than failing the whole stored batch.
    [Test]
    public async Task ProcessTripPositions_EmptyBatchStillRoundTrips()
    {
        ProcessTripPositionsCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<ProcessTripPositionsCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<TripResultVm>, CancellationToken>((cmd, _) => received = (ProcessTripPositionsCommand)cmd)
            .ReturnsAsync(new TripResultVm(0, 0, 0, 0));

        var writer = new TripPositionWriter(_factory);
        var result = await writer.ProcessTripPositionsAsync([], AccountId, CancellationToken.None);

        Assert.That(received, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Value.Positions, Is.Empty);
            Assert.That(result.ProcessedCount, Is.Zero);
        }
    }
}
