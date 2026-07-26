using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Validation;
using Microsoft.Extensions.DependencyInjection;
using TrackHub.ServiceContracts.Tests.Harness;

namespace TrackHub.ServiceContracts.Tests.ContractTests;

// Layer A: validates the new/changed Document Management GraphQL surface against the
// Manager producer schema. These are portal-facing operations (no C# const in a client), so the
// representative documents are declared here to guard the Manager schema shape.
[TestFixture]
public class DocumentContractTests
{
    private static readonly DocumentValidator Validator = DocumentValidatorBuilder.New().AddDefaultRules().Build();
    private ISchemaDefinition _schema = null!;

    [OneTimeSetUp]
    public async Task BuildManagerSchema() => _schema = await ProducerSchema.BuildManagerSchemaAsync();

    private const string DocumentFields = @"
        documentId accountId ownerEntityType ownerEntityId uploadedByPrincipalType uploadedByPrincipalId
        fileName category title description contentType sizeBytes sha256Hash classification status expiresAt
        visibilityScope scanStatus currentVersion downloadUrl lastModified";

    private const string DocumentsForOwnerQuery = @"
        query($accountId: UUID!, $ownerEntityType: String!, $ownerEntityId: String!) {
            documentsForOwner(query: { accountId: $accountId, ownerEntityType: $ownerEntityType, ownerEntityId: $ownerEntityId, skip: 0, take: 50 }) {
                documentId fileName category status scanStatus currentVersion downloadUrl classification
            }
        }";

    private const string GetDocumentQuery = @"
        query($documentId: UUID!) {
            document(query: { documentId: $documentId }) {
                documentId fileName category status scanStatus downloadUrl
            }
        }";

    private const string GetDocumentVersionsQuery = @"
        query($documentId: UUID!) {
            documentVersions(query: { documentId: $documentId, skip: 0, take: 50 }) {
                documentVersionId documentId versionNumber contentType fileName sizeBytes sha256Hash scanStatus reason createdAt
            }
        }";

    private const string ActiveDocumentByCategoryQuery = @"
        query($ownerEntityType: String!, $ownerEntityId: String!, $category: String!) {
            activeDocumentByCategory(query: { ownerEntityType: $ownerEntityType, ownerEntityId: $ownerEntityId, category: $category }) {
                documentId category status
            }
        }";

    private const string DocumentSignaturesQuery = @"
        query($documentId: UUID!) {
            documentSignatures(query: { documentId: $documentId }) {
                documentSignatureId documentId signerName legalTextAccepted signedAt createdAt
            }
        }";

    private const string SearchDocumentsQuery = @"
        query {
            searchDocuments(query: { filter: { category: ""SOAT"", status: ""Active"", expiringWithinDays: 30 }, skip: 0, take: 50 }) {
                documentId fileName category status classification
            }
        }";

    private const string ExpiringDocumentsQuery = @"
        query {
            expiringDocuments(query: { withinDays: 30, skip: 0, take: 50 }) {
                documentId category expiresAt status
            }
        }";

    private const string DocumentSharesQuery = @"
        query($documentId: UUID!) {
            documentShares(query: { documentId: $documentId }) {
                publicLinkGrantId accountId resourceType resourceId scopes purpose expiresAt accessCount
            }
        }";

    private const string DocumentTypesQuery = @"
        query($accountId: UUID!) {
            documentTypes(query: { accountId: $accountId, includeDisabled: false }) {
                documentTypeId accountId category displayName required expiring defaultValidityDays enabled createdAt
            }
        }";

    private const string CreateDocumentMetadataMutation = @"
        mutation($document: DocumentDtoInput!) {
            createDocumentMetadata(command: { document: $document }) { documentId fileName category status }
        }";

    private const string ReplaceDocumentVersionMutation = @"
        mutation($documentId: UUID!, $newVersion: DocumentVersionDtoInput!) {
            replaceDocumentVersion(command: { documentId: $documentId, newVersion: $newVersion }) { documentId currentVersion scanStatus }
        }";

    private const string VoidDocumentMutation = @"
        mutation($documentId: UUID!, $reason: String!) {
            voidDocument(command: { documentId: $documentId, reason: $reason })
        }";

    private const string ExpireDocumentMutation = @"
        mutation($documentId: UUID!, $expiresAt: DateTime!) {
            expireDocument(command: { documentId: $documentId, expiresAt: $expiresAt })
        }";

    private const string DeleteDocumentReferenceMutation = @"
        mutation($documentId: UUID!) {
            deleteDocumentReference(command: { documentId: $documentId })
        }";

    private const string SignDocumentMutation = @"
        mutation($signature: DocumentSignatureDtoInput!) {
            signDocument(command: { signature: $signature }) { documentSignatureId documentId signerName signedAt }
        }";

    private const string ConfigureDocumentTypeMutation = @"
        mutation($documentType: DocumentTypeDtoInput!) {
            configureDocumentType(command: { documentType: $documentType }) { documentTypeId category required expiring enabled }
        }";

    private const string DisableDocumentTypeMutation = @"
        mutation($documentTypeId: UUID!) {
            disableDocumentType(command: { documentTypeId: $documentTypeId })
        }";

    private static IEnumerable<TestCaseData> Calls()
    {
        yield return new TestCaseData("Portal.documentsForOwner", DocumentsForOwnerQuery);
        yield return new TestCaseData("Portal.document", GetDocumentQuery);
        yield return new TestCaseData("Portal.documentVersions", GetDocumentVersionsQuery);
        yield return new TestCaseData("Portal.activeDocumentByCategory", ActiveDocumentByCategoryQuery);
        yield return new TestCaseData("Portal.documentSignatures", DocumentSignaturesQuery);
        yield return new TestCaseData("Portal.searchDocuments", SearchDocumentsQuery);
        yield return new TestCaseData("Portal.expiringDocuments", ExpiringDocumentsQuery);
        yield return new TestCaseData("Portal.documentShares", DocumentSharesQuery);
        yield return new TestCaseData("Portal.documentTypes", DocumentTypesQuery);
        yield return new TestCaseData("Portal.createDocumentMetadata", CreateDocumentMetadataMutation);
        yield return new TestCaseData("Portal.replaceDocumentVersion", ReplaceDocumentVersionMutation);
        yield return new TestCaseData("Portal.voidDocument", VoidDocumentMutation);
        yield return new TestCaseData("Portal.expireDocument", ExpireDocumentMutation);
        yield return new TestCaseData("Portal.deleteDocumentReference", DeleteDocumentReferenceMutation);
        yield return new TestCaseData("Portal.signDocument", SignDocumentMutation);
        yield return new TestCaseData("Portal.configureDocumentType", ConfigureDocumentTypeMutation);
        yield return new TestCaseData("Portal.disableDocumentType", DisableDocumentTypeMutation);
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
