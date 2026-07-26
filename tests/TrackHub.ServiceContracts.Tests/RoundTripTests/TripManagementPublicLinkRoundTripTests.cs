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
using TrackHub.Manager.Application.PublicLinks.Commands;
using TrackHub.Manager.Domain.Enums;
using TrackHub.Manager.Domain.Models;
using TrackHub.ServiceContracts.Harness;
using TrackHub.ServiceContracts.Tests.Harness;
using TrackHub.TripManagement.Domain.Constants;
using TrackHub.TripManagement.Domain.Models;
using TrackHub.TripManagement.Infrastructure.ManagerApi;

namespace TrackHub.ServiceContracts.Tests.RoundTripTests;

// TripManagement's REAL PublicLinkGrantClient against Manager's REAL resolvers. The
// PublicLinkResolution literals (FOUND / NOT_FOUND / EXPIRED — the 200 / 404 / 410 public
// tracking shapes) travel as GraphQL enum values in the RESPONSE and are invisible to Layer A
// document validation, so each one needs a round-trip proving the consumer maps it correctly
// (spec 11 §16, §17.24; rules.md).
[TestFixture]
public class TripManagementPublicLinkRoundTripTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TripId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GrantId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset ExpiresAt = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

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

    private static IEnumerable<TestCaseData> Resolutions()
    {
        // Manager enum value → the shape the public tracking endpoint renders → consumer mapping.
        yield return new TestCaseData(PublicLinkResolution.Found, PublicTripResolution.Found, true);
        yield return new TestCaseData(PublicLinkResolution.NotFound, PublicTripResolution.NotFound, false);
        yield return new TestCaseData(PublicLinkResolution.Expired, PublicTripResolution.Expired, false);
    }

    [TestCaseSource(nameof(Resolutions))]
    public async Task ResolvePublicLinkGrant_MapsEveryResolutionLiteral(
        PublicLinkResolution produced, PublicTripResolution expected, bool disclosesGrant)
    {
        ResolvePublicLinkGrantCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<ResolvePublicLinkGrantCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<PublicLinkResolutionVm>, CancellationToken>((cmd, _) => received = (ResolvePublicLinkGrantCommand)cmd)
            .ReturnsAsync(disclosesGrant
                ? new PublicLinkResolutionVm(produced, GrantId, TripId.ToString())
                : new PublicLinkResolutionVm(produced, null, null));

        var client = new PublicLinkGrantClient(_factory);
        var result = await client.ResolveAsync(
            GrantId, AccountId, TripSharing.ResourceType, TripId.ToString(), TripSharing.TrackScope,
            "a-plaintext-token", CancellationToken.None);

        Assert.That(received, Is.Not.Null, "the real ResolvePublicLinkGrantCommand must reach the Manager handler");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Value.PublicLinkGrantId, Is.EqualTo(GrantId));
            Assert.That(received!.Value.AccountId, Is.EqualTo(AccountId));
            Assert.That(received!.Value.ResourceType, Is.EqualTo(TripSharing.ResourceType));
            Assert.That(received!.Value.ResourceId, Is.EqualTo(TripId.ToString()));
            Assert.That(received!.Value.Scope, Is.EqualTo(TripSharing.TrackScope));
            Assert.That(received!.Value.Token, Is.EqualTo("a-plaintext-token"));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Resolution, Is.EqualTo(expected),
                $"Manager's {produced} literal no longer maps to {expected} in PublicLinkGrantClient.");
            Assert.That(result.PublicLinkGrantId, disclosesGrant ? Is.EqualTo(GrantId) : Is.Null);
            Assert.That(result.ResourceId, disclosesGrant ? Is.EqualTo(TripId.ToString()) : Is.Null);
        }
    }

    // There are exactly three resolution literals. A fourth added to Manager without a consumer
    // mapping would silently fall into the NOT_FOUND default — a new outcome rendered as 404.
    [Test]
    public void PublicLinkResolution_HasExactlyTheThreeMappedLiterals()
    {
        var literals = Enum.GetNames<PublicLinkResolution>().OrderBy(v => v, StringComparer.Ordinal).ToArray();

        Assert.That(literals, Is.EqualTo(new[] { "Expired", "Found", "NotFound" }),
            "PublicLinkResolution gained or lost a literal; PublicLinkGrantClient.MapResolution and the "
            + "404/410/200 public tracking contract must be revisited.");
    }

    [Test]
    public async Task CreatePublicLinkGrant_DeliversTripScopeAndReturnsOneTimeToken()
    {
        CreatePublicLinkGrantCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<CreatePublicLinkGrantCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<PublicLinkGrantVm>, CancellationToken>((cmd, _) => received = (CreatePublicLinkGrantCommand)cmd)
            .ReturnsAsync(new PublicLinkGrantVm(
                GrantId, AccountId, TripSharing.ResourceType, TripId.ToString(), TripSharing.TrackScope,
                "customer tracking", ExpiresAt, null, null, "user:1", 0, null, ExpiresAt, "one-time-plaintext"));

        var client = new PublicLinkGrantClient(_factory);
        var grant = await client.CreateAsync(
            AccountId, TripSharing.ResourceType, TripId.ToString(), TripSharing.TrackScope,
            "customer tracking", ExpiresAt, "user:1", CancellationToken.None);

        Assert.That(received, Is.Not.Null, "the real CreatePublicLinkGrantCommand must reach the Manager handler");
        var sent = received!.Value.PublicLinkGrant;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent.AccountId, Is.EqualTo(AccountId));
            Assert.That(sent.ResourceType, Is.EqualTo(TripSharing.ResourceType));
            Assert.That(sent.ResourceId, Is.EqualTo(TripId.ToString()));
            Assert.That(sent.Scopes, Is.EqualTo(TripSharing.TrackScope));
            Assert.That(sent.SubjectTokenIdHash, Is.Null,
                "TripManagement must never hash a token itself — Manager generates it (spec 11 §18.10).");
            Assert.That(sent.ExpiresAt, Is.EqualTo(ExpiresAt));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(grant.PublicLinkGrantId, Is.EqualTo(GrantId));
            Assert.That(grant.Token, Is.EqualTo("one-time-plaintext"));
            Assert.That(grant.ExpiresAt, Is.EqualTo(ExpiresAt));
        }
    }

    [Test]
    public async Task RevokePublicLinkGrant_DeliversGrantIdAndRevoker()
    {
        RevokePublicLinkGrantCommand? received = null;
        _sender
            .Setup(s => s.Send(It.IsAny<RevokePublicLinkGrantCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest, CancellationToken>((cmd, _) => received = (RevokePublicLinkGrantCommand)cmd)
            .Returns(Task.CompletedTask);

        var client = new PublicLinkGrantClient(_factory);
        await client.RevokeAsync(GrantId, "user:1", CancellationToken.None);

        Assert.That(received, Is.Not.Null, "the real RevokePublicLinkGrantCommand must reach the Manager handler");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received!.Value.PublicLinkGrantId, Is.EqualTo(GrantId));
            Assert.That(received!.Value.RevokedBy, Is.EqualTo("user:1"));
        }
    }
}
