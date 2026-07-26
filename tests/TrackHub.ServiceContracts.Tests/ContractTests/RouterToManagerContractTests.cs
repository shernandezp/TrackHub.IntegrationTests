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
using TrackHub.Router.Infrastructure.ManagerApi;
using TrackHub.ServiceContracts.Tests.Harness;

namespace TrackHub.ServiceContracts.Tests.ContractTests;

// Every query the Router ships against Manager is validated as a
// document against Manager's real, in-process-built schema. A producer rename/removal of a
// field, argument, or type that any of these calls depends on fails the matching case.
[TestFixture]
public class RouterToManagerContractTests
{
    private static readonly DocumentValidator Validator = DocumentValidatorBuilder.New().AddDefaultRules().Build();
    private ISchemaDefinition _schema = null!;

    [OneTimeSetUp]
    public async Task BuildManagerSchema() => _schema = await ProducerSchema.BuildManagerSchemaAsync();

    private static IEnumerable<TestCaseData> Calls()
    {
        yield return new TestCaseData("AccountReader.GetAccountsToSync", AccountReader.AccountsToSyncQuery);
        yield return new TestCaseData("AccountReader.IsFeatureEnabled", AccountReader.ValidateFeatureEnabledQuery);
        yield return new TestCaseData("AccountReader.GetAllAccountFeatures", AccountReader.AllAccountFeaturesQuery);
        yield return new TestCaseData("AlertEventWriter.Record", AlertEventWriter.RecordAlertEventMutation);
        yield return new TestCaseData("CredentialWriter.UpdateToken", ManagerApi.CredentialWriter.UpdateTokenMutation);
        yield return new TestCaseData("DeviceSyncWriter.Reset", DeviceSyncWriter.WipeDevicesMutation);
        yield return new TestCaseData("DeviceSyncWriter.Synchronize", DeviceSyncWriter.SynchronizeOperatorDevicesMutation);
        yield return new TestCaseData("DeviceTransporterReader.GetVisibleByOperator", DeviceTransporterReader.VisibleDeviceTransportersByOperatorQuery);
        yield return new TestCaseData("DeviceTransporterReader.GetAssignedByOperator", DeviceTransporterReader.AssignedDeviceTransportersByOperatorQuery);
        yield return new TestCaseData("DeviceTransporterReader.GetById", DeviceTransporterReader.DeviceTransporterByIdQuery);
        yield return new TestCaseData("GeocodingProviderReader.GetActive", GeocodingProviderReader.ActiveGeocodingProviderQuery);
        yield return new TestCaseData("GroupVisibilityReader.Validate", GroupVisibilityReader.ValidateGroupVisibilityQuery);
        yield return new TestCaseData("OperatorReader.GetOperatorsByUser", ManagerApi.OperatorReader.OperatorsByUserQuery);
        yield return new TestCaseData("OperatorReader.GetOperatorByTransporter", ManagerApi.OperatorReader.OperatorByTransporterQuery);
        yield return new TestCaseData("OperatorReader.GetOperator", ManagerApi.OperatorReader.OperatorQuery);
        yield return new TestCaseData("OperatorReader.GetOperatorsByAccounts", ManagerApi.OperatorReader.OperatorsMasterQuery);
        yield return new TestCaseData("TransporterTypeReader.GetTransporterType", TransporterTypeReader.TransporterTypeQuery);
    }

    [TestCaseSource(nameof(Calls))]
    public void ProductionQuery_IsValidAgainstManagerSchema(string call, string query)
    {
        var document = Utf8GraphQLParser.Parse(query);
        var result = Validator.Validate(_schema, document);

        Assert.That(result.HasErrors, Is.False,
            () => $"Router→Manager {call} no longer matches the Manager schema: "
                + string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}
