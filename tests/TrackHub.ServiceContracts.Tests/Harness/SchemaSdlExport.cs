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

namespace TrackHub.ServiceContracts.Tests.Harness;

/// <summary>
/// Exports each producer's SDL to <c>TrackHub/schemas/&lt;service&gt;.graphql</c> for the React
/// portal's graphql-codegen pipeline. Runs with the normal contract-test suite so the
/// checked-in SDLs stay in lockstep with the producer schemas: a schema change shows up as a
/// diff in the TrackHub repo, where the frontend codegen validates portal operations against it.
/// File names match the frontend's GRAPHQL_ENDPOINTS keys (TrackHub/src/api/core/endpoints.ts).
/// </summary>
[TestFixture]
public class SchemaSdlExport
{
    private static readonly (string FileName, Func<Task<ISchemaDefinition>> Build)[] Producers =
    [
        ("manager.graphql", ProducerSchema.BuildManagerSchemaAsync),
        ("security.graphql", ProducerSchema.BuildSecuritySchemaAsync),
        ("geofencing.graphql", ProducerSchema.BuildGeofenceSchemaAsync),
        ("router.graphql", ProducerSchema.BuildRouterSchemaAsync),
        ("telemetry.graphql", ProducerSchema.BuildTelemetrySchemaAsync),
        // File name must equal the frontend's GRAPHQL_ENDPOINTS key (`tripManagement`), spec 11 §8.
        ("tripManagement.graphql", ProducerSchema.BuildTripManagementSchemaAsync),
    ];

    [Test]
    public async Task Export_producer_schemas_for_frontend_codegen()
    {
        var schemasDir = System.IO.Path.Combine(FindWorkspaceRoot(), "TrackHub", "schemas");
        Directory.CreateDirectory(schemasDir);

        foreach (var (fileName, build) in Producers)
        {
            var schema = await build();
            var sdl = schema.ToString().ReplaceLineEndings("\n") + "\n";
            await File.WriteAllTextAsync(System.IO.Path.Combine(schemasDir, fileName), sdl);
        }

        Assert.That(
            Producers.Select(p => System.IO.Path.Combine(schemasDir, p.FileName)).All(File.Exists),
            Is.True);
    }

    /// <summary>Walks up from the test binary to the multi-repo workspace root.</summary>
    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(System.IO.Path.Combine(dir.FullName, "system-context")) &&
                Directory.Exists(System.IO.Path.Combine(dir.FullName, "TrackHub")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Workspace root not found (no ancestor containing system-context/ and TrackHub/).");
    }
}
