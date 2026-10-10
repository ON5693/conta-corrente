using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Questao5.Application.Abstractions;
using Questao5.Infrastructure.Database;
using Questao5.Infrastructure.Services;
using Questao5.Infrastructure.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
        new ApiError("INVALID_REQUEST", "O corpo ou os parâmetros da requisição são inválidos. Verifique os tipos e campos enviados."));
});
builder.Services.AddMediatR(Assembly.GetExecutingAssembly());
builder.Services.AddSingleton(TimeProvider.System);
var connectionSettings = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
    builder.Configuration.GetValue<string>("DatabaseName") ?? "Data Source=database.sqlite");
if (connectionSettings.DataSource != ":memory:" && !Path.IsPathRooted(connectionSettings.DataSource))
    connectionSettings.DataSource = Path.Combine(builder.Environment.ContentRootPath, connectionSettings.DataSource);
builder.Services.AddSingleton(new DatabaseConfig { Name = connectionSettings.ToString() });
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<IDatabaseBootstrap, DatabaseBootstrap>();
builder.Services.AddScoped<IBankingStore, SqliteBankingStore>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "API Bancária", Version = "v1", Description = "Movimentação idempotente e consulta de saldo. Contas identificadas por UUID; valores em duas casas decimais; horários em UTC." });
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "Questao5.xml"));
    options.OperationFilter<BankingExamplesFilter>();
});

var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.MapControllers();
app.Services.GetRequiredService<IDatabaseBootstrap>().Setup();
app.Run();

public partial class Program;
