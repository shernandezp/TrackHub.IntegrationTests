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

using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Validation;
using Microsoft.Extensions.DependencyInjection;
using TrackHub.ServiceContracts.Tests.Harness;
using TrackHub.TripManagement.Infrastructure.ManagerApi;

namespace TrackHub.ServiceContracts.Tests.ContractTests;

// Every document the TripManagement service ships against Manager under the trip_client identity
// (spec 11 §16): alert emission, background job-run recording, the three cross-account
// validations, the public-link grant lifecycle including resolution, and POD document state.
// Validated against Manager's real, in-process-built schema.
[TestFixture]
public class TripManagementToManagerContractTests
{
    private static readonly DocumentValidator Validator = DocumentValidatorBuilder.New().AddDefaultRules().Build();
    private ISchemaDefinition _schema = null!;

    [OneTimeSetUp]
    public async Task BuildManagerSchema() => _schema = await ProducerSchema.BuildManagerSchemaAsync();

    private static IEnumerable<TestCaseData> Calls()
    {
        yield return new TestCaseData("AlertEmitter.Record", AlertEmitter.RecordAlertEventMutation);
        yield return new TestCaseData("BackgroundJobRunRecorder.Record", BackgroundJobRunRecorder.CreateBackgroundJobRunMutation);
        yield return new TestCaseData("ManagerValidationClient.ValidateDriverAssignment", ManagerValidationClient.ValidateDriverAssignmentQuery);
        yield return new TestCaseData("ManagerValidationClient.ValidateGroupVisibility", ManagerValidationClient.ValidateGroupVisibilityQuery);
        yield return new TestCaseData("ManagerValidationClient.ValidateFeatureEnabled", ManagerValidationClient.ValidateFeatureEnabledQuery);
        yield return new TestCaseData("PublicLinkGrantClient.Create", PublicLinkGrantClient.CreatePublicLinkGrantMutation);
        yield return new TestCaseData("PublicLinkGrantClient.Revoke", PublicLinkGrantClient.RevokePublicLinkGrantMutation);
        yield return new TestCaseData("PublicLinkGrantClient.Resolve", PublicLinkGrantClient.ResolvePublicLinkGrantMutation);
        yield return new TestCaseData("DocumentClient.GetDocumentState", DocumentClient.DocumentQuery);
    }

    [TestCaseSource(nameof(Calls))]
    public void ProductionQuery_IsValidAgainstManagerSchema(string call, string query)
    {
        var document = Utf8GraphQLParser.Parse(query);
        var result = Validator.Validate(_schema, document);

        Assert.That(result.HasErrors, Is.False,
            () => $"TripManagement→Manager {call} no longer matches the Manager schema: "
                + string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}
