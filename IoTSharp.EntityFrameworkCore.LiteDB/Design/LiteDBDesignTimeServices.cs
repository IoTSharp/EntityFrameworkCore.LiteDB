// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Service registration pattern adapted from EF Core v10.0.14. See UPSTREAM.md.
using IoTSharp.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

[assembly: DesignTimeProviderServices("IoTSharp.EntityFrameworkCore.LiteDB.Design.LiteDBDesignTimeServices")]

namespace IoTSharp.EntityFrameworkCore.LiteDB.Design;

/// <summary>Registers LiteDB's EF design-time services, including compiled runtime model generation.</summary>
public sealed class LiteDBDesignTimeServices : IDesignTimeServices
{
    /// <inheritdoc />
    public void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddEntityFrameworkLiteDBDatabase();
#pragma warning disable EF1001 // Provider design-time integration requires the EF Core 10 internal annotation generator.
        new EntityFrameworkDesignServicesBuilder(serviceCollection)
            .TryAdd<ICSharpRuntimeAnnotationCodeGenerator, CSharpRuntimeAnnotationCodeGenerator>()
            .TryAddCoreServices();
#pragma warning restore EF1001
    }
}
