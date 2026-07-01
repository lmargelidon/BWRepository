using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProcessAst.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IFacadeDispatcher, InMemoryFacadeDispatcher>();
builder.Services.AddControllers();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    application = "ProcessAst.Host",
    version = "V11"
}));

app.MapPost("/api/process/execute", (ProcessExecutionContext context) =>
    Results.Ok(new
    {
        accepted = true,
        context.MessageName
    }));

app.MapControllers();

app.Run();
