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
using TrackHub.Reporting.Infrastructure.GraphQLApi;
using TrackHub.ServiceContracts.Tests.Harness;

namespace TrackHub.ServiceContracts.Tests.ContractTests;

// The four report feeds Reporting drains from TripManagement for the spec 11 §13 reports
// (trip summary/detail, on-time, dwell, toll cost, POD export), validated against
// TripManagement's real, in-process-built schema.
[TestFixture]
public class ReportingToTripManagementContractTests
{
    private static readonly DocumentValidator Validator = DocumentValidatorBuilder.New().AddDefaultRules().Build();
    private ISchemaDefinition _schema = null!;

    [OneTimeSetUp]
    public async Task BuildTripManagementSchema() => _schema = await ProducerSchema.BuildTripManagementSchemaAsync();

    private static IEnumerable<TestCaseData> Calls()
    {
        yield return new TestCaseData("TripReportReader.GetTrips", TripReportReader.TripReportDataQuery);
        yield return new TestCaseData("TripReportReader.GetTripStops", TripReportReader.TripStopReportDataQuery);
        yield return new TestCaseData("TripReportReader.GetTripTolls", TripReportReader.TripTollReportDataQuery);
        yield return new TestCaseData("TripReportReader.GetTripProofsOfDelivery", TripReportReader.TripPodReportDataQuery);
    }

    [TestCaseSource(nameof(Calls))]
    public void ProductionQuery_IsValidAgainstTripManagementSchema(string call, string query)
    {
        var document = Utf8GraphQLParser.Parse(query);
        var result = Validator.Validate(_schema, document);

        Assert.That(result.HasErrors, Is.False,
            () => $"Reporting→TripManagement {call} no longer matches the TripManagement schema: "
                + string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}
