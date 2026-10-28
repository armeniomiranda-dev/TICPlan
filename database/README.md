# Base de dados (PostgreSQL 18)

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
