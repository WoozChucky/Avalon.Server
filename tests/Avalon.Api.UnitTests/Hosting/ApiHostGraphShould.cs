using System.Reflection;
using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Worlds;
using Avalon.Database.Character;
using Avalon.Database.World;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// Each API service runs alone in its own process (#794, design section 2.2). Its real host, with
/// <c>Application:Services</c> naming only it, builds under the container validation every Avalon host builds with, so
/// no registration is missing and no scoped service is captured; maps that service's controllers and no other's; and
/// can build every one of them, with the services their actions take, from a request's services. The container never
/// sees a controller, so a dependency only another service registers, or a need the service does not declare, would
/// otherwise first show as a 500 on a live endpoint. The world databases it reads open the request's world (#523): a
/// single database there would silently serve one world everywhere.
/// </summary>
public sealed class ApiHostGraphShould
{
    public static TheoryData<string> Services => new(ApiServices.All.Select(service => service.Name));

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Build_the_service_alone_and_serve_its_own_routes_and_no_others(string name)
    {
        IApiService service = ApiServices.All.Single(candidate => candidate.Name == name);
        await using WebApplication process = ApiProcess.Build(service);
        using IServiceScope request = process.Services.CreateScope();

        ControllerActionDescriptor[] actions = ApiProcess.Endpoints(process)
            .Select(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>())
            .OfType<ControllerActionDescriptor>()
            .ToArray();
        Assert.NotEmpty(actions);
        foreach (TypeInfo controller in actions.Select(action => action.ControllerTypeInfo).Distinct())
        {
            Assert.Same(service.ControllerAssembly, controller.Assembly);
            ActivatorUtilities.CreateInstance(request.ServiceProvider, controller);
        }

        foreach (ParameterDescriptor parameter in actions.SelectMany(action => action.Parameters)
                     .Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Services))
        {
            request.ServiceProvider.GetRequiredService(parameter.ParameterType);
        }

        if (service.Needs.WorldDatabases.HasFlag(WorldDatabaseParts.World))
        {
            Assert.IsType<CurrentWorldDbContextFactory<WorldDbContext>>(
                request.ServiceProvider.GetRequiredService<IDbContextFactory<WorldDbContext>>());
        }

        if (service.Needs.WorldDatabases.HasFlag(WorldDatabaseParts.Characters))
        {
            Assert.IsType<CurrentWorldDbContextFactory<CharacterDbContext>>(
                request.ServiceProvider.GetRequiredService<IDbContextFactory<CharacterDbContext>>());
        }
    }
}
