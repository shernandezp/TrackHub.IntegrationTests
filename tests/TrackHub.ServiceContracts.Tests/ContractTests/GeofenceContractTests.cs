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
using TrackHub.Router.Infrastructure.GeofenceApi;
using TrackHub.ServiceContracts.Tests.Harness;

namespace TrackHub.ServiceContracts.Tests.ContractTests;

// Every query the Router and Reporting ship against the Geofence
// service, validated against its real in-process-built schema.
[TestFixture]
public class GeofenceContractTests
{
    private static readonly DocumentValidator Validator = DocumentValidatorBuilder.New().AddDefaultRules().Build();
    private ISchemaDefinition _schema = null!;

    [OneTimeSetUp]
    public async Task BuildGeofenceSchema() => _schema = await ProducerSchema.BuildGeofenceSchemaAsync();

    private static IEnumerable<TestCaseData> Calls()
    {
        yield return new TestCaseData("Router→Geofence GeofenceWriter.ProcessPositions", GeofenceWriter.ProcessPositionsMutation);
        yield return new TestCaseData("Reporting→Geofence GeofenceReader.GetTransportersInGeofence", GeofenceReader.TransportersInGeofenceQuery);
        yield return new TestCaseData("Reporting→Geofence GeofenceReader.GetGeofenceEvents", GeofenceReader.GeofenceEventsQuery);
    }

    [TestCaseSource(nameof(Calls))]
    public void ProductionQuery_IsValidAgainstGeofenceSchema(string call, string query)
    {
        var document = Utf8GraphQLParser.Parse(query);
        var result = Validator.Validate(_schema, document);

        Assert.That(result.HasErrors, Is.False,
            () => $"{call} no longer matches the Geofence schema: "
                + string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}
