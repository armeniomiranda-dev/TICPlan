# AppTIC

Aplicação ASP.NET Core MVC (.NET 10) para registar o planeamento de circulações no TIC, segundo o modelo da folha
`template_planeamento_simplificado_vazio.xlsx`, e guardar feeds GTFS com registo de atividade por login.

## Stack

- .NET 10, ASP.NET Core MVC, ASP.NET Core Identity (perfis `Admin` e `Utilizador`)
- PostgreSQL 18 via EF Core + Npgsql (tabelas e colunas em snake_case)
- Bootstrap 5.3 + Bootstrap Icons (layout), Syncfusion EJ2 (grelhas, dropdowns, datas, horas, checkboxes), FullCalendar 6 (calendários)

## Modelo de dados (folhas do Excel)

| Folha | Entidade | Notas |
|---|---|---|
| info_operador | `Operador` | NIF único por login, alvará e validade, âmbito (nacional/internacional/nac-int) |
| paragens | `Paragem` | código único por login, latitude/longitude (aceita `.` ou `,`) |
| circulações | `Circulacao` | operador, linha, autorização, declaração de capacidade, ID viagem (único por login), origem/destino, chegada/partida no TIC, tempo de embarque |
| calendário | `CirculacaoDia` | um registo por dia em que a circulação se realiza (ausência = não) |
| percursos | `PercursoParagem` | sequência ordenada de paragens por circulação |

Além disso: `GtfsFeed` (ficheiro .zip, SHA-256, estado de validação, contagens, datas do feed_info) e `RegistoAtividade`
(log de sessões, GTFS, alterações de dados e administração).

## Perfis e segmentação por operador

- **Admin**: vê e edita tudo (operadores, paragens, circulações, feeds GTFS, registos e utilizadores).
- **Utilizador**: está associado a um **operador** (obrigatório, definido pelo Admin em **Utilizadores**) e só vê a
  informação desse operador: circulações (e calendário, percurso, horas no TIC), paragens usadas nessas circulações e
  feeds GTFS do operador. Não vê os menus Operadores e Registos e não edita: nas circulações faz **pedidos de alteração**
  (menu **Pedidos**), que o Admin aceita ou rejeita (a alteração em si é feita pelo Admin na circulação).

Os filtros globais estão no `ApplicationDbContext`; o operador do utilizador vai numa claim da sessão
(`AppClaimsPrincipalFactory`). Mudar o operador ou o perfil de um utilizador termina as sessões abertas dele.
O `SaveChanges` continua a impedir alterar registos de outro login a quem não é Admin. Não há registo público.

## Arrancar em desenvolvimento

```bash
docker compose up -d                       # PostgreSQL 18 em localhost:5432 (postgres/postgres)
cd src/AppTIC.Web
dotnet run                                 # aplica as migrações e cria o Admin
```

Logins de teste (criados no arranque em Development, definidos em `Seed` no `appsettings.Development.json`):

| Login | Palavra-passe | Perfil |
|---|---|---|
| `admin@apptic.local` | `Admin12345` | Admin (vê todos os dados) |
| `rne@apptic.local` | `Rne12345` | Utilizador – Rede Nacional de Expressos |
| `flixbus@apptic.local` | `Flixbus123` | Utilizador – FlixBus |

Só são criados se ainda não existirem. Para produção, defina apenas `Seed:AdminEmail`/`Seed:AdminPassword` e crie os restantes utilizadores em **Utilizadores**.

Configuração (`appsettings.json` ou variáveis de ambiente, ex. `ConnectionStrings__DefaultConnection`):

- `ConnectionStrings:DefaultConnection` – ligação PostgreSQL
- `Seed:AdminEmail`, `Seed:AdminPassword` – primeiro Admin (só é criado se não existir)
- `Syncfusion:LicenseKey` – chave de licença Syncfusion (sem ela aparece o aviso de versão de avaliação); `Syncfusion:Version` deve coincidir com o pacote NuGet
- `Gtfs:StoragePath` – pasta dos .zip (por omissão `App_Data/gtfs`, um subdiretório por login); `Gtfs:MaxUploadBytes`
- `Database:MigrateOnStartup` – aplica migrações no arranque (true por omissão)

Script SQL completo da base de dados: [`database/apptic_schema.sql`](database/apptic_schema.sql) (ver [`database/README.md`](database/README.md)).

Migrações: `dotnet tool restore && dotnet ef migrations add <Nome> -p src/AppTIC.Web -o Data/Migrations`.

## Instalação no IIS (servidor 10.0.30.97)

A aplicação corre no IIS com o ASP.NET Core Module (in-process). A base de dados é a `tic_plan` do PostgreSQL/PostGIS
no mesmo servidor (`ConnectionStrings:DefaultConnection` em `appsettings.json`); as migrações aplicam-se no arranque.

**Uma vez, no servidor**

1. Instalar o **ASP.NET Core 10 Hosting Bundle** (inclui o runtime e o módulo do IIS) e reiniciar o IIS (`iisreset`).
2. Criar as pastas `C:\AppTIC\site` (aplicação), `C:\AppTIC\dados\gtfs` (feeds GTFS) e `C:\AppTIC\dados\keys`
   (chaves das sessões). Os caminhos estão em `appsettings.Production.json`.
3. No Gestor do IIS, criar o application pool **AppTIC** com *.NET CLR version* = **No Managed Code**.
4. Criar o site **AppTIC** com caminho físico `C:\AppTIC\site`, o pool AppTIC e o binding
   `http` · `10.0.30.97` · porta livre (p.ex. 8080). Também funciona como aplicação dentro de um site existente
   (em produção: `http://10.0.30.97/TICPlanner`, no Default Web Site). Os links e redirecionamentos usam sempre a raiz
   da aplicação (`~/`, `Url.Action`), nunca `/`, para funcionar dentro desse caminho.
5. Dar permissão de **Modificar** em `C:\AppTIC\dados` à conta `IIS AppPool\AppTIC`:
   `icacls C:\AppTIC\dados /grant "IIS AppPool\AppTIC:(OI)(CI)M"`
6. Abrir a porta na firewall do Windows, se for acedida a partir de outras máquinas.
7. Para criar o primeiro administrador numa base vazia, definir `Seed:AdminEmail` e `Seed:AdminPassword`
   (p.ex. como variáveis de ambiente `Seed__AdminEmail`/`Seed__AdminPassword` no `web.config` publicado) e retirá-los depois.

**Em cada versão**

```bash
dotnet publish src/AppTIC.Web -p:PublishProfile=IIS
```

Copiar o conteúdo de `src/AppTIC.Web/bin/Release/net10.0/publish-iis/` para `C:\AppTIC\site`. Antes de copiar, parar o
site ou pôr na pasta um `app_offline.htm` (o IIS liberta os ficheiros e mostra essa página); retirá-lo no fim.

O `web.config` define `ASPNETCORE_ENVIRONMENT=Production` e aumenta o limite de upload do IIS (30 MB por omissão) para os
200 MB de `Gtfs:MaxUploadBytes`. Para ver erros de arranque, pôr `stdoutLogEnabled="true"` no `web.config` do servidor
(cria `C:\AppTIC\site\logs`). Os browsers precisam de acesso à internet para o Syncfusion e o OpenLayers (CDN).

## Feeds GTFS

Carregar um .zip guarda o ficheiro, calcula o SHA-256 (avisa se for duplicado) e valida: ficheiros obrigatórios
(agency, stops, routes, trips, stop_times, calendar e/ou calendar_dates), cabeçalhos, contagens e datas/versão do
`feed_info.txt`. Cada passo (carregamento, validação, descarga, eliminação) fica no registo do feed e no registo do login.

Cada feed é associado a um **operador**. Depois da validação é gerado um **GTFS filtrado TIC**, com a mesma lógica do
GTFS_Agg: viagens com pelo menos um `stop_time` numa paragem do TIC → os seus `stop_times`, `trips`, `routes`, `stops`
(e estações-mãe), `calendar`/`calendar_dates`, `shapes`, `frequencies` e `transfers` referenciados. As paragens do TIC são
os `stop_id` definidos na ficha do operador (campo "Paragens do TIC no GTFS"); se estiver vazio, usa
`Gtfs:ParagensTicPorOmissao` do `appsettings.json` (RNE `100`, FlixBus `cd022a5c-…`, CP `94_2006`, STCP `TIC1-3`/`CMP1,2,5,6`,
Metro `5703`). Os `stop_id` são os originais de cada feed, sem o sufixo de origem (`_S2JE`, `_5G5E`, …) que o GTFS_Agg acrescenta.
