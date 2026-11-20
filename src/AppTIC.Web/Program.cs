using AppTIC.Web.Data;
using AppTIC.Web.Infrastructure;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.Terminal;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;

var builder = WebApplication.CreateBuilder(args);

var syncfusionKey = builder.Configuration["Syncfusion:LicenseKey"];
if (!string.IsNullOrWhiteSpace(syncfusionKey))
    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionKey);

// Não codificar acentos como entidades HTML (á -> &#xE1;).
builder.Services.AddWebEncoders(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

// No IIS o application pool pode não ter perfil carregado: sem pasta de chaves, as sessões perdiam-se a cada reciclagem.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    builder.Services.AddDataProtection().SetApplicationName("AppTIC").PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .UseSnakeCaseNamingConvention());

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(10);
    options.SlidingExpiration = true;
});

builder.Services.Configure<GtfsOptions>(builder.Configuration.GetSection(GtfsOptions.Section));
var maxUpload = builder.Configuration.GetSection(GtfsOptions.Section).Get<GtfsOptions>()?.MaxUploadBytes ?? new GtfsOptions().MaxUploadBytes;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUpload + 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUpload + 1024 * 1024);

builder.Services.AddScoped<IActivityLog, ActivityLog>();
builder.Services.AddScoped<GtfsFeedService>();
builder.Services.AddScoped<GtfsDbLoader>();
builder.Services.AddScoped<GtfsImportService>();
builder.Services.AddScoped<ImportacaoService>();
builder.Services.AddScoped<ExcelImportService>();
builder.Services.AddScoped<NumeracaoService>();
builder.Services.AddScoped<ExportacaoPlaneamento>();
builder.Services.AddScoped<OcupacaoTerminal>();
builder.Services.AddScoped<SimulacaoService>();
builder.Services.AddSingleton<ICaisService, CaisService>();
builder.Services.AddSingleton<ITerminalStateStore, InMemoryTerminalStateStore>();

// FlixBus em tempo real (chegadas/partidas no TIC), atualizado em segundo plano.
builder.Services.Configure<FlixbusOptions>(builder.Configuration.GetSection(FlixbusOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<FlixbusRealtimeStore>();
builder.Services.AddSingleton<RastoStore>(); // últimos 10 min de posições (FlixBus, STCP, CP) para o rasto no mapa
builder.Services.AddHttpClient<FlixbusClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FlixbusOptions>>().Value;
    http.BaseAddress = new Uri(opt.BaseUrl.EndsWith('/') ? opt.BaseUrl : opt.BaseUrl + "/");
    http.Timeout = TimeSpan.FromSeconds(20);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("AppTIC/1.0");
});
builder.Services.AddHostedService<FlixbusRealtimeWorker>();

// STCP em tempo real (FIWARE da Porto Digital + GTFS dos dados abertos), filtrado às paragens do TIC.
builder.Services.Configure<StcpOptions>(builder.Configuration.GetSection(StcpOptions.Section));
builder.Services.AddSingleton<StcpRealtimeStore>();
builder.Services.AddHttpClient<StcpClient>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(120); // o GTFS tem ~10 MB
    http.DefaultRequestHeaders.UserAgent.ParseAdd("AppTIC/1.0");
});
builder.Services.AddHostedService<StcpRealtimeWorker>();

// CP (comboios.live) e Metro do Porto (GTFS + estado das linhas) na estação de Campanhã.
builder.Services.Configure<CpOptions>(builder.Configuration.GetSection(CpOptions.Section));
builder.Services.AddSingleton<CpRealtimeStore>();
builder.Services.AddHttpClient<CpClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CpOptions>>().Value;
    http.BaseAddress = new Uri(opt.BaseUrl.EndsWith('/') ? opt.BaseUrl : opt.BaseUrl + "/");
    http.Timeout = TimeSpan.FromSeconds(20);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("AppTIC/1.0");
});
builder.Services.AddHostedService<CpRealtimeWorker>();

builder.Services.Configure<MetroOptions>(builder.Configuration.GetSection(MetroOptions.Section));
builder.Services.AddSingleton<MetroRealtimeStore>();
builder.Services.AddHttpClient<MetroClient>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(60);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("AppTIC/1.0");
});
builder.Services.AddHostedService<MetroRealtimeWorker>();
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
// PascalCase como nos controladores MVC: o simulacao.js lê Token, Mensagem, Alocacoes, etc.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
});

// Todas as páginas exigem sessão iniciada, exceto as marcadas com [AllowAnonymous].
builder.Services.AddControllersWithViews(o =>
    {
        o.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
        o.ModelBinderProviders.Insert(0, new FlexibleDecimalModelBinderProvider());
    })
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = null);

var app = builder.Build();

var culture = new CultureInfo("pt-PT");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culture),
    SupportedCultures = [culture],
    SupportedUICultures = [culture]
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// API do terminal (simulação): só para o Admin.
var terminalApi = app.MapGroup("/api/terminal").RequireAuthorization(p => p.RequireRole(Roles.Admin));
terminalApi.AddEndpointFilter(async (context, next) =>
{
    if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
    {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { Mensagem = "Token antifalsificação inválido ou em falta." });
        }
    }
    return await next(context);
});

terminalApi.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
    Results.Ok(new { Token = antiforgery.GetAndStoreTokens(context).RequestToken }));

terminalApi.MapGet("/estado", (ITerminalStateStore store) => Results.Ok(store.ObterAtivos()));

terminalApi.MapPost("/alocar-preview", (IReadOnlyList<AutocarroDto> pedidos, ICaisService solver) =>
{
    if (pedidos.Count == 0)
        return Results.Ok(Array.Empty<AlocacaoResposta>());

    if (pedidos.Any(p => string.IsNullOrWhiteSpace(p.AutocarroId) || !Enum.IsDefined(p.Zona) || p.ChegadaMinutos >= p.PartidaMinutos))
        return Results.BadRequest(new { Mensagem = "Existem pedidos inválidos no preview de alocação." });

    var resultado = solver.AlocarCais(pedidos);
    return resultado is null
        ? Results.Conflict(new { Mensagem = "Inviável alocar cais para os pedidos enviados." })
        : Results.Ok(resultado.OrderBy(a => a.Cais));
});

terminalApi.MapPost("/planear-dia", (PlaneamentoPedido pedido, ICaisService solver) =>
{
    if (CaisService.Validar(pedido) is { } erro)
        return Results.BadRequest(new { Mensagem = erro });

    var resultado = solver.PlanearDia(pedido);
    return resultado is null
        ? Results.Conflict(new { Mensagem = "Os cais fixos (autocarros já atracados) sobrepõem-se; não é possível planear." })
        : Results.Ok(resultado);
});

terminalApi.MapPost("/chegada",(NovaChegadaRequest pedido, ITerminalStateStore store) =>
{
    if (string.IsNullOrWhiteSpace(pedido.Id) || !Enum.IsDefined(pedido.Zona) ||
        pedido.ChegadaMinutos < 0 || pedido.ChegadaMinutos >= pedido.PartidaMinutos)
        return Results.BadRequest(new { Mensagem = "Indique um identificador, uma zona válida e um intervalo de minutos válido." });

    var resultado = store.RegistarChegada(pedido);
    if (!resultado.Sucesso)
        return Results.Conflict(new { Mensagem = resultado.Erro });

    var atribuicao = resultado.Alocacoes!.First(a => a.AutocarroId == pedido.Id);
    return Results.Ok(new
    {
        Mensagem = "Autocarro integrado com sucesso.",
        CaisAtribuido = atribuicao.Cais,
        AlocacoesAtivas = resultado.Alocacoes.OrderBy(a => a.Cais)
    });
});

terminalApi.MapPost("/atracar/{id}", (string id, ITerminalStateStore store) =>
    store.MarcarAtracado(id)
        ? Results.Ok(new { Mensagem = $"Autocarro {id} marcado como atracado (cais travado)." })
        : Results.NotFound(new { Mensagem = "Autocarro não encontrado, já atracado ou sem cais atribuído." }));

terminalApi.MapPost("/partida/{id}", (string id, ITerminalStateStore store) =>
    store.MarcarPartida(id)
        ? Results.Ok(new { Mensagem = $"Autocarro {id} concluiu viagem e libertou o cais." })
        : Results.NotFound(new { Mensagem = "Autocarro não encontrado ou já concluído." }));

// FlixBus em tempo real: último estado em cache (atualizado pelo FlixbusRealtimeWorker).
app.MapGet("/api/flixbus/tic", (FlixbusRealtimeStore store) => Results.Ok(store.Atual))
    .RequireAuthorization();

// STCP em tempo real: autocarros das viagens que param no TIC (cache do StcpRealtimeWorker).
app.MapGet("/api/stcp/tic", (StcpRealtimeStore store) => Results.Ok(store.Atual))
    .RequireAuthorization();
app.MapGet("/api/cp/tic", (CpRealtimeStore store) => Results.Ok(store.Atual))
    .RequireAuthorization();
app.MapGet("/api/metro/tic", (MetroRealtimeStore store) => Results.Ok(store.Atual))
    .RequireAuthorization();
// Rasto dos últimos 10 min por veículo: { "stcp:1832": [[lon, lat, unixSeg], ...], ... }
app.MapGet("/api/tempo-real/rastos", (RastoStore rastos) => Results.Ok(rastos.Obter().ToDictionary(
        r => r.Key, r => r.Value.Select(p => new double[] { p.Longitude, p.Latitude, p.Hora.ToUnixTimeSeconds() }).ToList())))
    .RequireAuthorization();

// Traçado de uma viagem FlixBus (polylines codificadas), pedido pelo mapa ao clicar num autocarro.
app.MapGet("/api/flixbus/rota/{rideUuid}", async (string rideUuid, FlixbusClient client, CancellationToken ct) =>
    Guid.TryParse(rideUuid, out _)
        ? Results.Ok(await client.ObterRotaAsync(rideUuid, ct))
        : Results.BadRequest(new { Mensagem = "Identificador de viagem inválido." }))
    .RequireAuthorization();

await DbSeeder.SeedAsync(app.Services, app.Configuration);

app.Run();
