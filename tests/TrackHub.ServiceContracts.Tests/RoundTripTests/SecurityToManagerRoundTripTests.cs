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
using TrackHub.Manager.Application.AuditEvents.Commands;
using TrackHub.Manager.Domain.Models;
using TrackHub.Security.Domain.Records;
using TrackHub.Security.Infrastructure.ManagerApi;
using TrackHub.ServiceContracts.Harness;
using TrackHub.ServiceContracts.Tests.Harness;

namespace TrackHub.ServiceContracts.Tests.RoundTripTests;

// Security's REAL ManagerAuditWriter forwards a security audit event against Manager's REAL
// CreateAuditEvent resolver. Verifies the security_client audit
// document maps field-for-field onto Manager's AuditEventDto through the real GraphQL pipeline.
[TestFixture]
public class SecurityToManagerRoundTripTests
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
    public async Task ForwardAuditEvent_RoundTripsSecurityAuditIntoManagerCreateAuditEvent()
    {
        CreateAuditEventCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<CreateAuditEventCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<AuditEventVm>, CancellationToken>((cmd, _) => received = (CreateAuditEventCommand)cmd)
            .ReturnsAsync(default(AuditEventVm) with { AuditEventId = FakeData.OperatorId });

        var writer = new ManagerAuditWriter(_factory);
        await writer.ForwardAuditEventAsync(new SecurityAuditEventDto(
            AccountId: FakeData.AccountId,
            ActorType: "User",
            ActorId: "actor-1",
            Action: "CreateServiceClientPermission",
            ResourceType: "ServiceClientPermission",
            ResourceId: "perm-1",
            OldValuesJson: null,
            NewValuesJson: "router_client:Audit:Write",
            CorrelationId: "corr-1"), CancellationToken.None);

        Assert.That(received, Is.Not.Null, "the real CreateAuditEventCommand must reach the Manager handler");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Value.AuditEvent.AccountId, Is.EqualTo(FakeData.AccountId));
            Assert.That(received!.Value.AuditEvent.ActorType, Is.EqualTo("User"));
            Assert.That(received!.Value.AuditEvent.ActorId, Is.EqualTo("actor-1"));
            Assert.That(received!.Value.AuditEvent.Action, Is.EqualTo("CreateServiceClientPermission"));
            Assert.That(received!.Value.AuditEvent.ResourceType, Is.EqualTo("ServiceClientPermission"));
            Assert.That(received!.Value.AuditEvent.ResourceId, Is.EqualTo("perm-1"));
            Assert.That(received!.Value.AuditEvent.NewValuesJson, Is.EqualTo("router_client:Audit:Write"));
            Assert.That(received!.Value.AuditEvent.CorrelationId, Is.EqualTo("corr-1"));
        }
    }
}
