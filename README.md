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

## Segmentação por login

Todas as entidades com dono (`IOwnedEntity`) têm um filtro global no `ApplicationDbContext`: um utilizador só vê e altera
os seus registos; o perfil `Admin` vê todos (com a coluna "Utilizador" nas grelhas). O `SaveChanges` impede criar,
alterar ou transferir registos de outro login. Não há registo público: o Admin cria os utilizadores em **Utilizadores**.

## Arrancar em desenvolvimento

```bash
docker compose up -d                       # PostgreSQL 18 em localhost:5432 (postgres/postgres)
cd src/AppTIC.Web
dotnet run                                 # aplica as migrações e cria o Admin
```

Admin de desenvolvimento (definido em `appsettings.Development.json`): `admin@apptic.local` / `Admin12345`.

Configuração (`appsettings.json` ou variáveis de ambiente, ex. `ConnectionStrings__DefaultConnection`):

- `ConnectionStrings:DefaultConnection` – ligação PostgreSQL
- `Seed:AdminEmail`, `Seed:AdminPassword` – primeiro Admin (só é criado se não existir)
- `Syncfusion:LicenseKey` – chave de licença Syncfusion (sem ela aparece o aviso de versão de avaliação); `Syncfusion:Version` deve coincidir com o pacote NuGet
- `Gtfs:StoragePath` – pasta dos .zip (por omissão `App_Data/gtfs`, um subdiretório por login); `Gtfs:MaxUploadBytes`
- `Database:MigrateOnStartup` – aplica migrações no arranque (true por omissão)

Script SQL completo da base de dados: [`database/apptic_schema.sql`](database/apptic_schema.sql) (ver [`database/README.md`](database/README.md)).

Migrações: `dotnet tool restore && dotnet ef migrations add <Nome> -p src/AppTIC.Web -o Data/Migrations`.

## Feeds GTFS

Carregar um .zip guarda o ficheiro, calcula o SHA-256 (avisa se for duplicado) e valida: ficheiros obrigatórios
(agency, stops, routes, trips, stop_times, calendar e/ou calendar_dates), cabeçalhos, contagens e datas/versão do
`feed_info.txt`. Cada passo (carregamento, validação, descarga, eliminação) fica no registo do feed e no registo do login.
