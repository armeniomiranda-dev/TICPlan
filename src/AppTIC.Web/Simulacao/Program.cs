using System.Text.Json.Serialization;
using TerminalIntermodal.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<ITerminalStateStore, InMemoryTerminalStateStore>();
builder.Services.AddScoped<ICaisService, CaisService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var caisApi = app.MapGroup("/api/terminal");

caisApi.MapGet("/estado", (ITerminalStateStore store) => Results.Ok(store.ObterAtivos()));

caisApi.MapPost("/chegada", (NovaChegadaRequest req, ITerminalStateStore store, ICaisService solver) =>
{
    if (req.ChegadaMinutos >= req.PartidaMinutos)
        return Results.BadRequest(new { Mensagem = "Hora de partida tem de ser superior à de chegada." });

    store.RegistarChegada(req.Id, req.Zona, req.ChegadaMinutos, req.PartidaMinutos);
    var ativos = store.ObterAtivos();

    var dtos = ativos.Select(a => new AutocarroDto(
        a.AutocarroId,
        a.Zona,
        a.ChegadaMinutos,
        a.PartidaMinutos,
        CaisFixo: a.Estado == EstadoAutocarro.Atracado ? a.CaisAlocado : null
    )).ToList();

    var resultado = solver.AlocarCais(dtos);
    if (resultado is null)
        return Results.Conflict(new { Mensagem = "Falta de espaço na zona pretendida." });

    store.AtualizarAlocacoes(resultado);
    var atribuicao = resultado.First(r => r.AutocarroId == req.Id);

    return Results.Ok(new
    {
        req.Id,
        CaisAtribuido = atribuicao.Cais,
        Alocacoes = resultado.OrderBy(r => r.Cais)
    });
});

caisApi.MapPost("/atracar/{id}", (string id, ITerminalStateStore store) =>
    store.MarcarAtracado(id) ? Results.Ok() : Results.NotFound());

caisApi.MapPost("/partida/{id}", (string id, ITerminalStateStore store) =>
    store.MarcarPartida(id) ? Results.Ok() : Results.NotFound());

app.Run();

public record NovaChegadaRequest(string Id, ZonaCais Zona, int ChegadaMinutos, int PartidaMinutos);