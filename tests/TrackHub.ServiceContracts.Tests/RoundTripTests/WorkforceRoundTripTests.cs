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
using Common.Application.Interfaces;
using Common.Domain.Constants;
using Common.Mediator;
using HotChocolate.Execution;
using Moq;
using TrackHub.Manager.Application.Drivers.Queries;
using TrackHub.Manager.Domain.Models;
using TrackHub.Reporting.Domain.Interfaces;
using TrackHub.Reporting.Infrastructure.GraphQLApi;
using TrackHub.ServiceContracts.Harness;
using TrackHub.ServiceContracts.Tests.Harness;

namespace TrackHub.ServiceContracts.Tests.RoundTripTests;

// Spec 09 §16 Layer B. Layer A only proves the workforce documents PARSE against Manager's schema;
// everything travelling in GraphQL variables — the "Transporter" resource-type literal, the optional
// driver/transporter filters, the DateTimeOffset window, the paging bounds — is invisible to document
// validation. These tests push Reporting's REAL query documents through Manager's REAL resolvers and
// assert the values land intact in the REAL query record structs.
[TestFixture]
public class WorkforceRoundTripTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DriverId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TransporterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);

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

    private static IUser FakeUser()
    {
        var user = new Mock<IUser>();
        user.SetupGet(x => x.AccountId).Returns(AccountId);
        return user.Object;
    }

    // The feature gate is exercised by the Reporting unit tests; here it must simply not block.
    private static IAccountFeatureReader FakeFeatureReader()
    {
        var reader = new Mock<IAccountFeatureReader>();
        reader.Setup(x => x.EnsureFeatureEnabledAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return reader.Object;
    }

    private WorkforceReportReader NewReader() => new(_factory, FakeUser(), FakeFeatureReader());

    private StrongBox<GetDriverQualificationsQuery?> SetupQualifications()
    {
        var received = new StrongBox<GetDriverQualificationsQuery?>(null);
        _sender
            .Setup(s => s.Send(It.IsAny<GetDriverQualificationsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<IReadOnlyCollection<DriverQualificationVm>>, CancellationToken>((q, _) => received.Value = (GetDriverQualificationsQuery)q)
            .ReturnsAsync((IReadOnlyCollection<DriverQualificationVm>)[]);
        return received;
    }

    private StrongBox<GetDriverAssignmentHistoryQuery?> SetupAssignmentHistory()
    {
        var received = new StrongBox<GetDriverAssignmentHistoryQuery?>(null);
        _sender
            .Setup(s => s.Send(It.IsAny<GetDriverAssignmentHistoryQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<IReadOnlyCollection<DriverTransporterAssignmentVm>>, CancellationToken>((q, _) => received.Value = (GetDriverAssignmentHistoryQuery)q)
            .ReturnsAsync((IReadOnlyCollection<DriverTransporterAssignmentVm>)[]);
        return received;
    }

    [Test]
    public async Task QualificationExpirations_RoundTripsTheExpiryWindowAndPaging()
    {
        var received = SetupQualifications();

        var reader = NewReader();
        await reader.GetDriverQualificationsAsync(null, 15, CancellationToken.None);

        Assert.That(received.Value, Is.Not.Null, "Reporting's document must reach Manager's real query handler");
        var query = received.Value!.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.AccountId, Is.EqualTo(AccountId));
            // The expiry window is what makes this report the expirations report — a dropped or
            // mis-coerced Int here would silently widen it to the whole catalogue.
            Assert.That(query.ExpiringWithinDays, Is.EqualTo(15));
            Assert.That(query.Take, Is.EqualTo(500), "Reporting pages at the producer's clamp ceiling");
            Assert.That(query.Skip, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task AssignmentHistory_RoundTripsTheDateWindowAndOptionalFilters()
    {
        var received = SetupAssignmentHistory();

        var reader = NewReader();
        await reader.GetDriverAssignmentHistoryAsync(DriverId, TransporterId, From, To, CancellationToken.None);

        Assert.That(received.Value, Is.Not.Null);
        var query = received.Value!.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.AccountId, Is.EqualTo(AccountId));
            Assert.That(query.DriverId, Is.EqualTo(DriverId));
            Assert.That(query.TransporterId, Is.EqualTo(TransporterId));
            // DateTimeOffset coercion across the GraphQL boundary is the risky part: an offset lost
            // here shifts every row in the report by the viewer's timezone.
            Assert.That(query.From, Is.EqualTo(From));
            Assert.That(query.To, Is.EqualTo(To));
        }
    }

    [Test]
    public async Task AssignmentHistory_OmittedFiltersArriveAsNullNotEmptyGuid()
    {
        var received = SetupAssignmentHistory();

        var reader = NewReader();
        await reader.GetDriverAssignmentHistoryAsync(null, null, null, null, CancellationToken.None);

        Assert.That(received.Value, Is.Not.Null);
        var query = received.Value!.Value;
        using (Assert.EnterMultipleScope())
        {
            // Guid.Empty instead of null would filter the report down to nothing rather than to
            // "unfiltered" — the two are indistinguishable in the document, only in the variables.
            Assert.That(query.DriverId, Is.Null);
            Assert.That(query.TransporterId, Is.Null);
            Assert.That(query.From, Is.Null);
            Assert.That(query.To, Is.Null);
        }
    }

    // AC4: ValidateDriverAssignment is satisfied by an active assignment row OR the default
    // transporter. The "Transporter" resource-type literal travels as a variable, so Layer A cannot
    // see it; spec 10 and DocumentAccessPolicy both depend on it matching exactly.
    [TestCase("Transporter", true, TestName = "ValidateDriverAssignment_AssignmentRowPathSatisfies")]
    [TestCase("Transporter", false, TestName = "ValidateDriverAssignment_DefaultTransporterPathSatisfies")]
    public async Task ValidateDriverAssignment_RoundTripsResourceTypeLiteral(string resourceType, bool satisfied)
    {
        ValidateDriverAssignmentQuery? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<ValidateDriverAssignmentQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<bool>, CancellationToken>((q, _) => received = (ValidateDriverAssignmentQuery)q)
            .ReturnsAsync(satisfied);

        const string document = @"
            query($driverId: UUID!, $resourceType: String!, $resourceId: String!) {
                validateDriverAssignment(query: { driverId: $driverId, resourceType: $resourceType, resourceId: $resourceId })
            }";

        var client = _factory.CreateClient(Clients.Manager);
        var response = await client.SendQueryAsync<ValidateResponse>(new GraphQL.GraphQLRequest
        {
            Query = document,
            Variables = new { driverId = DriverId, resourceType, resourceId = TransporterId.ToString() },
        }, CancellationToken.None);

        Assert.That(received, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Value.DriverId, Is.EqualTo(DriverId));
            Assert.That(received.Value.ResourceType, Is.EqualTo("Transporter"));
            Assert.That(received.Value.ResourceId, Is.EqualTo(TransporterId.ToString()));
            Assert.That(response.Data.ValidateDriverAssignment, Is.EqualTo(satisfied));
        }
    }

    private sealed class ValidateResponse
    {
        public bool ValidateDriverAssignment { get; set; }
    }
}
