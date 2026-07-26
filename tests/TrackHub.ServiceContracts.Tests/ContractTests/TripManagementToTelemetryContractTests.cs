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
using TrackHub.TripManagement.Infrastructure.TelemetryApi;

namespace TrackHub.ServiceContracts.Tests.ContractTests;

// TripManagement's route-replay read against Telemetry (spec 11 §7.5), issued under the
// trip_client identity, validated against Telemetry's real, in-process-built schema.
[TestFixture]
public class TripManagementToTelemetryContractTests
{
    private static readonly DocumentValidator Validator = DocumentValidatorBuilder.New().AddDefaultRules().Build();
    private ISchemaDefinition _schema = null!;

    [OneTimeSetUp]
    public async Task BuildTelemetrySchema() => _schema = await ProducerSchema.BuildTelemetrySchemaAsync();

    private static IEnumerable<TestCaseData> Calls()
    {
        yield return new TestCaseData("PositionHistoryClient.GetRange", PositionHistoryClient.PositionHistoryRangeQuery);
    }

    [TestCaseSource(nameof(Calls))]
    public void ProductionQuery_IsValidAgainstTelemetrySchema(string call, string query)
    {
        var document = Utf8GraphQLParser.Parse(query);
        var result = Validator.Validate(_schema, document);

        Assert.That(result.HasErrors, Is.False,
            () => $"TripManagement→Telemetry {call} no longer matches the Telemetry schema: "
                + string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}
