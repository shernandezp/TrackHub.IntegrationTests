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

using Common.Mediator;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TrackHub.ServiceContracts.Harness;
using TrackHub.Router.Web.GraphQL;
using GeofenceMutation = TrackHub.Geofencing.Web.GraphQL.Mutation.Mutation;
using GeofenceQuery = TrackHub.Geofencing.Web.GraphQL.Query.Query;
using ManagerMutation = TrackHub.Manager.Web.GraphQL.Mutation.Mutation;
using ManagerQuery = TrackHub.Manager.Web.GraphQL.Query.Query;
using RouterMutation = TrackHub.Router.Web.GraphQL.Mutation.Mutation;
using RouterQuery = TrackHub.Router.Web.GraphQL.Query.Query;
using SecurityMutation = TrackHub.Security.Web.GraphQL.Mutation.Mutation;
using SecurityQuery = TrackHub.Security.Web.GraphQL.Query.Query;
using TripMutation = TrackHub.TripManagement.Web.GraphQL.Mutation.Mutation;
using TripQuery = TrackHub.TripManagement.Web.GraphQL.Query.Query;
using TelemetryMutation = TrackHub.Telemetry.Web.GraphQL.Mutation.Mutation;
using TelemetryQuery = TrackHub.Telemetry.Web.GraphQL.Query.Query;

namespace TrackHub.ServiceContracts.Tests.Harness;

/// <summary>
/// Producer wrappers over <see cref="ProducerSchemaBuilder"/>: the real Query/Mutation types
/// each service ships. Router is the one deviating producer — its two extra error filters are
/// applied exactly as its Program.cs does.
/// </summary>
internal static class ProducerSchema
{
    public static Task<ISchemaDefinition> BuildManagerSchemaAsync()
        => ProducerSchemaBuilder.BuildSchemaAsync<ManagerQuery, ManagerMutation>(Mock.Of<ISender>());

    public static Task<ISchemaDefinition> BuildTelemetrySchemaAsync()
        => ProducerSchemaBuilder.BuildSchemaAsync<TelemetryQuery, TelemetryMutation>(Mock.Of<ISender>());

    public static Task<ISchemaDefinition> BuildSecuritySchemaAsync()
        => ProducerSchemaBuilder.BuildSchemaAsync<SecurityQuery, SecurityMutation>(Mock.Of<ISender>());

    public static Task<ISchemaDefinition> BuildRouterSchemaAsync()
        => ProducerSchemaBuilder.BuildSchemaAsync<RouterQuery, RouterMutation>(Mock.Of<ISender>(), ConfigureRouter);

    public static Task<ISchemaDefinition> BuildGeofenceSchemaAsync()
        => ProducerSchemaBuilder.BuildSchemaAsync<GeofenceQuery, GeofenceMutation>(Mock.Of<ISender>());

    public static Task<ISchemaDefinition> BuildTripManagementSchemaAsync()
        => ProducerSchemaBuilder.BuildSchemaAsync<TripQuery, TripMutation>(Mock.Of<ISender>());

    public static Task<IRequestExecutor> BuildManagerExecutorAsync(ISender sender)
        => ProducerSchemaBuilder.BuildExecutorAsync<ManagerQuery, ManagerMutation>(sender);

    public static Task<IRequestExecutor> BuildTelemetryExecutorAsync(ISender sender)
        => ProducerSchemaBuilder.BuildExecutorAsync<TelemetryQuery, TelemetryMutation>(sender);

    public static Task<IRequestExecutor> BuildSecurityExecutorAsync(ISender sender)
        => ProducerSchemaBuilder.BuildExecutorAsync<SecurityQuery, SecurityMutation>(sender);

    public static Task<IRequestExecutor> BuildRouterExecutorAsync(ISender sender)
        => ProducerSchemaBuilder.BuildExecutorAsync<RouterQuery, RouterMutation>(sender, ConfigureRouter);

    public static Task<IRequestExecutor> BuildGeofenceExecutorAsync(ISender sender)
        => ProducerSchemaBuilder.BuildExecutorAsync<GeofenceQuery, GeofenceMutation>(sender);

    public static Task<IRequestExecutor> BuildTripManagementExecutorAsync(ISender sender)
        => ProducerSchemaBuilder.BuildExecutorAsync<TripQuery, TripMutation>(sender);

    // Mirrors Router's Program.cs deviation from the shared chain.
    private static void ConfigureRouter(HotChocolate.Execution.Configuration.IRequestExecutorBuilder builder)
        => builder
            .AddErrorFilter<GeocodingErrorFilter>()
            .AddErrorFilter<OperatorSyncErrorFilter>();
}
