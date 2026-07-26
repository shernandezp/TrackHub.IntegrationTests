using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Validation;
using Microsoft.Extensions.DependencyInjection;
using TrackHub.ServiceContracts.Tests.Harness;

namespace TrackHub.ServiceContracts.Tests.ContractTests;

// Layer A: validates the new account lifecycle/branding/context GraphQL surface against
// the Manager producer schema — including the inter-service `accountStatus` query shipped by Router
// and Reporting, and the portal-facing lifecycle/branding/context operations.
[TestFixture]
public class AccountStatusContractTests
{
    private static readonly DocumentValidator Validator = DocumentValidatorBuilder.New().AddDefaultRules().Build();
    private ISchemaDefinition _schema = null!;

    [OneTimeSetUp]
    public async Task BuildManagerSchema() => _schema = await ProducerSchema.BuildManagerSchemaAsync();

    // Portal-facing operations have no C# const to reference (they live in the React app), so the
    // representative documents are declared here to guard the Manager schema shape.
    private const string ChangeAccountStatusMutation = @"
        mutation($accountId: UUID!, $targetStatus: AccountStatus!, $reason: String) {
            changeAccountStatus(command: { accountId: $accountId, targetStatus: $targetStatus, reason: $reason }) {
                accountId
                status
                statusId
                active
            }
        }";

    private const string UpdateAccountBrandingMutation = @"
        mutation($accountId: UUID!, $displayName: String!, $primaryColor: String!) {
            updateAccountBranding(command: { branding: { accountId: $accountId, displayName: $displayName, primaryColor: $primaryColor } }) {
                accountId
                displayName
                logoDocumentId
                primaryColor
                reportHeader
                lastModified
            }
        }";

    private const string GetAccountBrandingQuery = @"
        query($accountId: UUID!) {
            accountBranding(query: { accountId: $accountId }) {
                accountId
                displayName
                logoDocumentId
                primaryColor
                reportHeader
                lastModified
            }
        }";

    private const string GetAccountContextQuery = @"
        query {
            accountContext {
                status
                statusId
                branding { accountId displayName logoDocumentId primaryColor reportHeader lastModified }
                features { featureKey enabled tier }
            }
        }";

    private const string GetAccountWithStatusQuery = @"
        query($id: UUID!) {
            account(query: { id: $id }) {
                accountId
                active
                status
                statusId
            }
        }";

    private static IEnumerable<TestCaseData> Calls()
    {
        yield return new TestCaseData("Router.AccountOperationalStatusReader.AccountStatus",
            TrackHub.Router.Infrastructure.ManagerApi.AccountOperationalStatusReader.AccountStatusQuery);
        yield return new TestCaseData("Reporting.AccountOperationalStatusReader.AccountStatus",
            TrackHub.Reporting.Infrastructure.GraphQLApi.AccountOperationalStatusReader.AccountStatusQuery);
        yield return new TestCaseData("Portal.changeAccountStatus", ChangeAccountStatusMutation);
        yield return new TestCaseData("Portal.updateAccountBranding", UpdateAccountBrandingMutation);
        yield return new TestCaseData("Portal.accountBranding", GetAccountBrandingQuery);
        yield return new TestCaseData("Portal.accountContext", GetAccountContextQuery);
        yield return new TestCaseData("Portal.account.status", GetAccountWithStatusQuery);

        // Reporting admin/lifecycle report readers.
        yield return new TestCaseData("Reporting.AdminReport.Accounts",
            TrackHub.Reporting.Infrastructure.GraphQLApi.AdminReportReader.AccountsQuery);
        yield return new TestCaseData("Reporting.AdminReport.AllAccountFeaturesMaster",
            TrackHub.Reporting.Infrastructure.GraphQLApi.AdminReportReader.AllAccountFeaturesMasterQuery);
        yield return new TestCaseData("Reporting.AdminReport.GroupsByAccount",
            TrackHub.Reporting.Infrastructure.GraphQLApi.AdminReportReader.GroupsByAccountQuery);
        yield return new TestCaseData("Reporting.AdminReport.UsersByGroup",
            TrackHub.Reporting.Infrastructure.GraphQLApi.AdminReportReader.UsersByGroupQuery);
        yield return new TestCaseData("Reporting.AdminReport.TransportersByGroup",
            TrackHub.Reporting.Infrastructure.GraphQLApi.AdminReportReader.TransportersByGroupQuery);
    }

    [TestCaseSource(nameof(Calls))]
    public void Document_IsValidAgainstManagerSchema(string call, string query)
    {
        var document = Utf8GraphQLParser.Parse(query);
        var result = Validator.Validate(_schema, document);

        Assert.That(result.HasErrors, Is.False,
            () => $"{call} no longer matches the Manager schema: "
                + string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}
