using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportacoesLote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "importacao_id",
                table: "circulacoes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "importacoes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    origem = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    gtfs_feed_id = table.Column<int>(type: "integer", nullable: true),
                    rotas = table.Column<string>(type: "text", nullable: true),
                    nome_ficheiro = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    caminho_armazenamento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tamanho_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    data_inicio = table.Column<DateOnly>(type: "date", nullable: true),
                    data_fim = table.Column<DateOnly>(type: "date", nullable: true),
                    num_operadores = table.Column<int>(type: "integer", nullable: false),
                    num_paragens = table.Column<int>(type: "integer", nullable: false),
                    num_circulacoes = table.Column<int>(type: "integer", nullable: false),
                    num_dias = table.Column<int>(type: "integer", nullable: false),
                    aplicacoes = table.Column<int>(type: "integer", nullable: false),
                    aplicada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_importacoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_importacoes_gtfs_feeds_gtfs_feed_id",
                        column: x => x.gtfs_feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_importacoes_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_circulacoes_importacao_id",
                table: "circulacoes",
                column: "importacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_importacoes_gtfs_feed_id",
                table: "importacoes",
                column: "gtfs_feed_id");

            migrationBuilder.CreateIndex(
                name: "ix_importacoes_owner_id",
                table: "importacoes",
                column: "owner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_circulacoes_importacoes_importacao_id",
                table: "circulacoes",
                column: "importacao_id",
                principalTable: "importacoes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_circulacoes_importacoes_importacao_id",
                table: "circulacoes");

            migrationBuilder.DropTable(
                name: "importacoes");

            migrationBuilder.DropIndex(
                name: "ix_circulacoes_importacao_id",
                table: "circulacoes");

            migrationBuilder.DropColumn(
                name: "importacao_id",
                table: "circulacoes");
        }
    }
}
