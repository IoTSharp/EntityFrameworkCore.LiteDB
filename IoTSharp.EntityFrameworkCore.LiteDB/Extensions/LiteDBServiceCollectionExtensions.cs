// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using IoTSharp.EntityFrameworkCore.LiteDB.Infrastructure.Internal;
using IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Utilities;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal;
using IoTSharp.EntityFrameworkCore.LiteDB.Diagnostics;
using IoTSharp.EntityFrameworkCore.LiteDB.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

// ReSharper disable once CheckNamespace
namespace IoTSharp.Extensions.DependencyInjection
{
    /// <summary>
    ///     LiteDB-specific extension methods for <see cref="IServiceCollection" />.
    /// </summary>
    public static class LiteDBServiceCollectionExtensions
    {
        /// <summary>
        ///     <para>
        ///         Adds the services required by the LiteDB database provider for Entity Framework
        ///         to an <see cref="IServiceCollection" />. You use this method when using dependency injection
        ///         in your application, such as with ASP.NET. For more information on setting up dependency
        ///         injection, see http://go.microsoft.com/fwlink/?LinkId=526890.
        ///     </para>
        ///     <para>
        ///         You only need to use this functionality when you want Entity Framework to resolve the services it uses
        ///         from an external dependency injection container. If you are not using an external
        ///         dependency injection container, Entity Framework will take care of creating the services it requires.
        ///     </para>
        /// </summary>
        /// <example>
        ///     <code>
        ///         public void ConfigureServices(IServiceCollection services)
        ///         {
        ///             services
        ///                 .AddEntityFrameworkLiteDBDatabase()
        ///                 .AddDbContext&lt;MyContext&gt;((serviceProvider, options) =>
        ///                     options.UseLiteDB("MyDatabase")
        ///                            .UseInternalServiceProvider(serviceProvider));
        ///         }
        ///     </code>
        /// </example>
        /// <param name="serviceCollection"> The <see cref="IServiceCollection" /> to add services to. </param>
        /// <returns>
        ///     The same service collection so that multiple calls can be chained.
        /// </returns>
        public static IServiceCollection AddEntityFrameworkLiteDBDatabase([NotNull] this IServiceCollection serviceCollection)
        {
            Check.NotNull(serviceCollection, nameof(serviceCollection));

            var builder = new EntityFrameworkServicesBuilder(serviceCollection)
                .TryAdd<LoggingDefinitions, LiteDBLoggingDefinitions>()
                .TryAdd<IDatabaseProvider, DatabaseProvider<LiteDBOptionsExtension>>()
                .TryAdd<IModelValidator, LiteDBModelValidator>()
                .TryAdd<IProviderConventionSetBuilder, LiteDBConventionSetBuilder>()
                .TryAdd<IValueGeneratorSelector, LiteDBValueGeneratorSelector>()
                .TryAdd<IDatabase, LiteDBDatabase>()
                .TryAdd<IDbContextTransactionManager, LiteDBTransactionManager>()
                .TryAdd<IDatabaseCreator, LiteDBDatabaseCreator>()
                .TryAdd<IQueryContextFactory, LiteDBQueryContextFactory>()
                .TryAdd<ITypeMappingSource, LiteDBTypeMappingSource>()
                .TryAdd<IShapedQueryCompilingExpressionVisitorFactory, LiteDBShapedQueryCompilingExpressionVisitorFactory>()
                .TryAdd<IQueryableMethodTranslatingExpressionVisitorFactory, LiteDBQueryableMethodTranslatingExpressionVisitorFactory>()
                .TryAdd<IQueryTranslationPreprocessorFactory, LiteDBQueryTranslationPreprocessorFactory>()
                .TryAddProviderSpecificServices(
                    b => b
                        .TryAddSingleton<LiteDBStoreCache, LiteDBStoreCache>()
                        .TryAddScoped<ILiteDBStoreProvider, LiteDBStoreProvider>());

            builder.TryAddCoreServices();

            return serviceCollection;
        }
    }
}
