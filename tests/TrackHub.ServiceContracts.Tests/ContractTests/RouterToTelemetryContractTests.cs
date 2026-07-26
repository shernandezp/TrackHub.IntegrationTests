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
using TrackHub.Router.Infrastructure.TelemetryApi;
using TrackHub.ServiceContracts.Tests.Harness;

namespace TrackHub.ServiceContracts.Tests.ContractTests;

// Every query the Router ships against Telemetry is validated as a
// document against Telemetry's real, in-process-built schema. This guards the seam created by
// the Telemetry extraction — the newest, most change-prone coupling.
[TestFixture]
public class RouterToTelemetryContractTests
{
    private static readonly DocumentValidator Validator = DocumentValidatorBuilder.New().AddDefaultRules().Build();
    private ISchemaDefinition _schema = null!;

    [OneTimeSetUp]
    public async Task BuildTelemetrySchema() => _schema = await ProducerSchema.BuildTelemetrySchemaAsync();

    private static IEnumerable<TestCaseData> Calls()
    {
        yield return new TestCaseData("TransporterPositionReader.GetByOperator", TransporterPositionReader.TransporterPositionByOperatorQuery);
        yield return new TestCaseData("TransporterPositionReader.GetByOperators", TransporterPositionReader.TransporterPositionsByOperatorsQuery);
        yield return new TestCaseData("PositionHistoryReader.GetRange", PositionHistoryReader.PositionHistoryRangeQuery);
        yield return new TestCaseData("PositionWriter.AddOrUpdate", PositionWriter.BulkTransporterPositionMutation);
        yield return new TestCaseData("PositionHistorySystemWriter.AppendRange", PositionHistorySystemWriter.AppendPositionHistoryBatchMutation);
        yield return new TestCaseData("ResolvedAddressWriter.Persist", ResolvedAddressWriter.PersistResolvedAddressMutation);
        yield return new TestCaseData("OperatorHealthCheckWriter.Record", OperatorHealthCheckWriter.RecordOperatorHealthMutation);
        yield return new TestCaseData("OperatorSyncRunWriter.Record", OperatorSyncRunWriter.RecordOperatorSyncRunMutation);
    }

    [TestCaseSource(nameof(Calls))]
    public void ProductionQuery_IsValidAgainstTelemetrySchema(string call, string query)
    {
        var document = Utf8GraphQLParser.Parse(query);
        var result = Validator.Validate(_schema, document);

        Assert.That(result.HasErrors, Is.False,
            () => $"Router→Telemetry {call} no longer matches the Telemetry schema: "
                + string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}
