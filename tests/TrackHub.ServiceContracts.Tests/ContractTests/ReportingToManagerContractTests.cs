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

// Every query Reporting ships against Manager, validated against
// Manager's real in-process-built schema. These feed the GPS report factories and the
// per-export audit trail.
[TestFixture]
public class ReportingToManagerContractTests
{
    private static readonly DocumentValidator Validator = DocumentValidatorBuilder.New().AddDefaultRules().Build();
    private ISchemaDefinition _schema = null!;

    [OneTimeSetUp]
    public async Task BuildManagerSchema() => _schema = await ProducerSchema.BuildManagerSchemaAsync();

    private static IEnumerable<TestCaseData> Calls()
    {
        yield return new TestCaseData("GpsManagerReader.GetOperators", GpsManagerReader.OperatorsByCurrentAccountQuery);
        yield return new TestCaseData("GpsManagerReader.GetSynchronizedDevices", GpsManagerReader.SynchronizedDevicesQuery);
        yield return new TestCaseData("GpsManagerReader.GetUnassignedDevices", GpsManagerReader.UnassignedSynchronizedDevicesQuery);
        yield return new TestCaseData("GpsManagerReader.GetAssignmentsByAccount", GpsManagerReader.TransporterDeviceAssignmentsByAccountQuery);
        yield return new TestCaseData("AccountFeatureReader.EnsureFeatureEnabled", AccountFeatureReader.ValidateFeatureEnabledQuery);
        yield return new TestCaseData("ReportAuditWriter.RecordReportExport", ReportAuditWriter.CreateAuditEventMutation);

        // Governed-catalog metadata lookup for the execution pipeline.
        yield return new TestCaseData("ReportCatalogReader.ReportByCode", ReportCatalogReader.ReportByCodeQuery);

        // Account branding lookup for PDF export headers — reuses Manager's existing
        // accountBranding query, so no Manager schema change.
        yield return new TestCaseData("ReportBrandingReader.AccountBranding", ReportBrandingReader.AccountBrandingQuery);

        // Document report readers.
        yield return new TestCaseData("DocumentReportReader.ExpiringDocuments", DocumentReportReader.ExpiringDocumentsQuery);
        yield return new TestCaseData("DocumentReportReader.DocumentTypes", DocumentReportReader.DocumentTypesQuery);
        yield return new TestCaseData("DocumentReportReader.TransporterDocumentCompliance", DocumentReportReader.TransporterDocumentComplianceQuery);
        yield return new TestCaseData("DocumentReportReader.SharesByAccount", DocumentReportReader.SharesByAccountQuery);
        yield return new TestCaseData("DocumentReportReader.SearchDocuments", DocumentReportReader.SearchDocumentsQuery);

        // Workforce report readers (spec 09 §13).
        yield return new TestCaseData("WorkforceReportReader.DriversByAccount", WorkforceReportReader.DriversByAccountQuery);
        yield return new TestCaseData("WorkforceReportReader.DriverQualifications", WorkforceReportReader.DriverQualificationsQuery);
        yield return new TestCaseData("WorkforceReportReader.DriverAssignmentHistory", WorkforceReportReader.DriverAssignmentHistoryQuery);
    }

    [TestCaseSource(nameof(Calls))]
    public void ProductionQuery_IsValidAgainstManagerSchema(string call, string query)
    {
        var document = Utf8GraphQLParser.Parse(query);
        var result = Validator.Validate(_schema, document);

        Assert.That(result.HasErrors, Is.False,
            () => $"Reporting→Manager {call} no longer matches the Manager schema: "
                + string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}
