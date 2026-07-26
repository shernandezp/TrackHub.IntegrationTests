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
using TrackHub.Manager.Infrastructure.RouterApi;
using TrackHub.ServiceContracts.Harness;
using TrackHub.ServiceContracts.Tests.Harness;
using TrackHub.Router.Application.DevicePositions.Commands.Sync;

namespace TrackHub.ServiceContracts.Tests.RoundTripTests;

// Manager's REAL sync dispatcher fires the manual device-sync trigger
// against the Router's REAL resolvers — the entry point of the cross-service sync pipeline.
[TestFixture]
public class ManagerToRouterRoundTripTests
{
    private Mock<ISender> _sender = null!;
    private InProcessGraphQLClientFactory _factory = null!;

    [OneTimeSetUp]
    public async Task BuildRouterExecutor()
    {
        _sender = new Mock<ISender>();
        var executor = await ProducerSchema.BuildRouterExecutorAsync(_sender.Object);
        _factory = new InProcessGraphQLClientFactory(
            new Dictionary<string, IRequestExecutor> { [Clients.Router] = executor });
    }

    [SetUp]
    public void ResetSender() => _sender.Reset();

    [Test]
    public async Task DispatchManualSync_DeliversTriggerCommandAndReturnsAcceptance()
    {
        TriggerOperatorSyncCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<TriggerOperatorSyncCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<bool>, CancellationToken>((cmd, _) => received = (TriggerOperatorSyncCommand)cmd)
            .ReturnsAsync(true);

        var correlationId = Guid.NewGuid().ToString();
        var dispatcher = new RouterSyncDispatcher(_factory);
        var accepted = await dispatcher.DispatchManualSyncAsync(
            FakeData.AccountId,
            FakeData.OperatorId,
            correlationId,
            resetDeviceCatalog: true,
            autoAssignNewDevices: false,
            CancellationToken.None);

        Assert.That(accepted, Is.True);
        Assert.That(received, Is.Not.Null, "the real TriggerOperatorSyncCommand must reach the Router handler");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Value.AccountId, Is.EqualTo(FakeData.AccountId));
            Assert.That(received!.Value.OperatorId, Is.EqualTo(FakeData.OperatorId));
            Assert.That(received!.Value.TriggerType, Is.EqualTo("MANUAL"));
            Assert.That(received!.Value.ResetDeviceCatalog, Is.True);
            Assert.That(received!.Value.AutoAssignNewDevices, Is.False);
            Assert.That(received!.Value.CorrelationId, Is.EqualTo(correlationId),
                "the correlation id born in Manager's handler must reach the Router command unchanged");
        }
    }
}
