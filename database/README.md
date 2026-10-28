# Base de dados (PostgreSQL 18 + PostGIS)

A migração `GtfsSchema` executa `CREATE EXTENSION IF NOT EXISTS postgis`: a extensão tem de estar instalada no servidor
(e o utilizador da aplicação precisa de permissão para a criar, ou um administrador cria-a antes).

## Schema `gtfs`

Uma tabela por ficheiro da referência GTFS Schedule (`agency`, `stops`, `routes`, `trips`, `stop_times`, `calendar`,
`calendar_dates`, `shapes`, `frequencies`, `transfers`, `pathways`, `levels`, `feed_info`, `translations`,
`attributions`, Fares v1 e v2, `networks`/`route_networks`, `areas`/`stop_areas`, GTFS-Flex `location_groups`,
`location_group_stops`, `locations` (locations.geojson) e `booking_rules`).

- **Versão do feed:** todas as tabelas têm `feed_id` → `public.gtfs_feeds(id)` com `ON DELETE CASCADE`; apagar o feed
  apaga o seu conteúdo. O `feed_version` do ficheiro fica em `gtfs.feed_info`.
- **Chaves:** os IDs GTFS só são únicos dentro do feed, por isso as PK/FK são compostas, ex. `(feed_id, stop_id)`.
  As FKs internas são `DEFERRABLE INITIALLY DEFERRED`: numa transação os ficheiros podem ser carregados por qualquer ordem.
  `service_id` não tem FK (pode existir só em `calendar_dates`); `translations.record_id` também não (depende de `table_name`).
- **Tipos:** horas em `interval` (aceita `25:10:00`), datas em `date`, enumerações em `smallint` com `CHECK`,
  `calendar` em `boolean`.
- **PostGIS (SRID 4326):** `stops.geom` é calculado de `stop_lat`/`stop_lon`; `locations.geom` guarda o GeoJSON;
  `shape_geoms` tem uma `LineString` por `shape_id`, gerada a partir de `shapes` com
  `SELECT gtfs.gerar_shape_geoms(<feed_id>);` — tem de correr antes do commit, porque `trips.shape_id` referencia
  `shape_geoms`.

A aplicação (`GtfsDbLoader`) carrega o feed original em cada carregamento e em "Validar e filtrar de novo": numa só
transação apaga o conteúdo anterior do feed, faz `COPY` de cada ficheiro para a tabela com o mesmo nome (só as colunas
da referência; as restantes ficam indicadas no registo do feed), gera `shape_geoms` e verifica as FKs. Se algo falhar
(tipo, chave duplicada ou referência inexistente), nada é alterado e o erro fica no registo do feed.

`apptic_schema.sql` cria todas as tabelas, índices e chaves. É gerado a partir das migrações EF Core
(`src/AppTIC.Web/Data/Migrations`) e é idempotente: pode ser executado várias vezes, só aplica o que falta.

```bash
createdb -U postgres apptic
psql -U postgres -d apptic -v ON_ERROR_STOP=1 -f database/apptic_schema.sql
```

Os perfis (`Admin`, `Utilizador`) e o primeiro Admin são criados pela aplicação no arranque, a partir de
`Seed:AdminEmail` / `Seed:AdminPassword`. Se aplicar o schema por este script, pode desligar as migrações automáticas
com `Database:MigrateOnStartup=false`.

Depois de criar uma nova migração, volte a gerar o script:

```bash
dotnet tool restore
dotnet ef migrations script --idempotent -p src/AppTIC.Web -o database/apptic_schema.sql
```
