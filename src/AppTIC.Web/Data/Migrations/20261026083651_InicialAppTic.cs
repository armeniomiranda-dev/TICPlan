using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InicialAppTic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asp_net_roles",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_users",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    nome_completo = table.Column<string>(type: "text", nullable: true),
                    entidade = table.Column<string>(type: "text", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<string>(type: "text", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_role_claims_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "asp_net_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_user_claims_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_asp_net_user_logins_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_roles",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "asp_net_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "asp_net_user_tokens",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_asp_net_user_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gtfs_feeds",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descricao = table.Column<string>(type: "text", nullable: true),
                    nome_ficheiro_original = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    caminho_armazenamento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tamanho_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    data_inicio = table.Column<DateOnly>(type: "date", nullable: true),
                    data_fim = table.Column<DateOnly>(type: "date", nullable: true),
                    editor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    versao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    num_agencias = table.Column<int>(type: "integer", nullable: false),
                    num_paragens = table.Column<int>(type: "integer", nullable: false),
                    num_rotas = table.Column<int>(type: "integer", nullable: false),
                    num_viagens = table.Column<int>(type: "integer", nullable: false),
                    num_horarios = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gtfs_feeds", x => x.id);
                    table.ForeignKey(
                        name: "fk_gtfs_feeds_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operadores",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nif = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    designacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    alvara = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    alvara_validade = table.Column<DateOnly>(type: "date", nullable: true),
                    ambito = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    abreviatura = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    comentarios = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operadores", x => x.id);
                    table.ForeignKey(
                        name: "fk_operadores_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "paragens",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    comentarios = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paragens", x => x.id);
                    table.ForeignKey(
                        name: "fk_paragens_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    nivel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    categoria = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    acao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mensagem = table.Column<string>(type: "text", nullable: false),
                    gtfs_feed_id = table.Column<int>(type: "integer", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    endereco_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registos", x => x.id);
                    table.ForeignKey(
                        name: "fk_registos_gtfs_feeds_gtfs_feed_id",
                        column: x => x.gtfs_feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_registos_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "circulacoes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    operador_id = table.Column<int>(type: "integer", nullable: false),
                    linha_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    designacao_linha = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ambito = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    autorizacao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    autorizacao_validade = table.Column<DateOnly>(type: "date", nullable: true),
                    declaracao_capacidade = table.Column<bool>(type: "boolean", nullable: false),
                    id_dcp = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    viagem_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    id_tic = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    origem_id = table.Column<int>(type: "integer", nullable: true),
                    destino_id = table.Column<int>(type: "integer", nullable: true),
                    chegada = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    partida = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    tempo_embarque_minutos = table.Column<int>(type: "integer", nullable: true),
                    comentarios = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_circulacoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_circulacoes_operadores_operador_id",
                        column: x => x.operador_id,
                        principalTable: "operadores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_circulacoes_paragens_destino_id",
                        column: x => x.destino_id,
                        principalTable: "paragens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_circulacoes_paragens_origem_id",
                        column: x => x.origem_id,
                        principalTable: "paragens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_circulacoes_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "circulacao_dias",
                columns: table => new
                {
                    circulacao_id = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_circulacao_dias", x => new { x.circulacao_id, x.data });
                    table.ForeignKey(
                        name: "fk_circulacao_dias_circulacoes_circulacao_id",
                        column: x => x.circulacao_id,
                        principalTable: "circulacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "percursos",
                columns: table => new
                {
                    circulacao_id = table.Column<int>(type: "integer", nullable: false),
                    sequencia = table.Column<int>(type: "integer", nullable: false),
                    paragem_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_percursos", x => new { x.circulacao_id, x.sequencia });
                    table.ForeignKey(
                        name: "fk_percursos_circulacoes_circulacao_id",
                        column: x => x.circulacao_id,
                        principalTable: "circulacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_percursos_paragens_paragem_id",
                        column: x => x.paragem_id,
                        principalTable: "paragens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_role_claims_role_id",
                table: "asp_net_role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "asp_net_roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_claims_user_id",
                table: "asp_net_user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_logins_user_id",
                table: "asp_net_user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_roles_role_id",
                table: "asp_net_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "asp_net_users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "asp_net_users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_circulacao_dias_data",
                table: "circulacao_dias",
                column: "data");

            migrationBuilder.CreateIndex(
                name: "ix_circulacoes_destino_id",
                table: "circulacoes",
                column: "destino_id");

            migrationBuilder.CreateIndex(
                name: "ix_circulacoes_operador_id",
                table: "circulacoes",
                column: "operador_id");

            migrationBuilder.CreateIndex(
                name: "ix_circulacoes_origem_id",
                table: "circulacoes",
                column: "origem_id");

            migrationBuilder.CreateIndex(
                name: "ix_circulacoes_owner_id_viagem_id",
                table: "circulacoes",
                columns: new[] { "owner_id", "viagem_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gtfs_feeds_owner_id",
                table: "gtfs_feeds",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_gtfs_feeds_sha256",
                table: "gtfs_feeds",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_operadores_owner_id_nif",
                table: "operadores",
                columns: new[] { "owner_id", "nif" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_paragens_owner_id_codigo",
                table: "paragens",
                columns: new[] { "owner_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_percursos_paragem_id",
                table: "percursos",
                column: "paragem_id");

            migrationBuilder.CreateIndex(
                name: "ix_registos_gtfs_feed_id",
                table: "registos",
                column: "gtfs_feed_id");

            migrationBuilder.CreateIndex(
                name: "ix_registos_owner_id_data",
                table: "registos",
                columns: new[] { "owner_id", "data" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asp_net_role_claims");

            migrationBuilder.DropTable(
                name: "asp_net_user_claims");

            migrationBuilder.DropTable(
                name: "asp_net_user_logins");

            migrationBuilder.DropTable(
                name: "asp_net_user_roles");

            migrationBuilder.DropTable(
                name: "asp_net_user_tokens");

            migrationBuilder.DropTable(
                name: "circulacao_dias");

            migrationBuilder.DropTable(
                name: "percursos");

            migrationBuilder.DropTable(
                name: "registos");

            migrationBuilder.DropTable(
                name: "asp_net_roles");

            migrationBuilder.DropTable(
                name: "circulacoes");

            migrationBuilder.DropTable(
                name: "gtfs_feeds");

            migrationBuilder.DropTable(
                name: "operadores");

            migrationBuilder.DropTable(
                name: "paragens");

            migrationBuilder.DropTable(
                name: "asp_net_users");
        }
    }
}
