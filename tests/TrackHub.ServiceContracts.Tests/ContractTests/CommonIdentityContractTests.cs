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

namespace TrackHub.ServiceContracts.Tests.ContractTests;

// The Common.Infrastructure IdentityService is the client EVERY service's authorization
// pipeline uses against Security. Its documents — above all `authorizeUser`, which runs on
// every authorized user request platform-wide — are validated against Security's real schema.
[TestFixture]
public class CommonIdentityContractTests
{
    private static readonly DocumentValidator Validator = DocumentValidatorBuilder.New().AddDefaultRules().Build();
    private ISchemaDefinition _schema = null!;

    [OneTimeSetUp]
    public async Task BuildSecuritySchema() => _schema = await ProducerSchema.BuildSecuritySchemaAsync();

    private static IEnumerable<TestCaseData> Calls()
    {
        yield return new TestCaseData("IdentityService.GetUserName", Common.Infrastructure.IdentityService.UserNameQuery);
        yield return new TestCaseData("IdentityService.Authorize", Common.Infrastructure.IdentityService.AuthorizeQuery);
        yield return new TestCaseData("IdentityService.IsInRole", Common.Infrastructure.IdentityService.IsInRoleQuery);
        yield return new TestCaseData("IdentityService.AuthorizeUser", Common.Infrastructure.IdentityService.AuthorizeUserQuery);
        yield return new TestCaseData("IdentityService.IsValidService", Common.Infrastructure.IdentityService.IsValidServiceQuery);
        yield return new TestCaseData("IdentityService.IsValidServiceForResource", Common.Infrastructure.IdentityService.IsValidServiceForResourceQuery);
        yield return new TestCaseData("IdentityService.IsValidServiceForResourceFull", Common.Infrastructure.IdentityService.IsValidServiceForResourceFullQuery);
    }

    [TestCaseSource(nameof(Calls))]
    public void IdentityDocument_IsValidAgainstSecuritySchema(string call, string query)
    {
        var document = Utf8GraphQLParser.Parse(query);
        var result = Validator.Validate(_schema, document);

        Assert.That(result.HasErrors, Is.False,
            () => $"Common→Security {call} no longer matches the Security schema: "
                + string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}
