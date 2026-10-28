using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class UtilizadorOperadorPedidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_operadores_users_owner_id",
                table: "operadores");

            migrationBuilder.AddColumn<int>(
                name: "operador_id",
                table: "asp_net_users",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pedidos_alteracao",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    circulacao_id = table.Column<int>(type: "integer", nullable: false),
                    descricao = table.Column<string>(type: "text", nullable: false),
                    data_inicio = table.Column<DateOnly>(type: "date", nullable: true),
                    data_fim = table.Column<DateOnly>(type: "date", nullable: true),
                    nova_chegada = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    nova_partida = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pedido_por_id = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resposta = table.Column<string>(type: "text", nullable: true),
                    respondido_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    respondido_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos_alteracao", x => x.id);
                    table.ForeignKey(
                        name: "fk_pedidos_alteracao_circulacoes_circulacao_id",
                        column: x => x.circulacao_id,
                        principalTable: "circulacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pedidos_alteracao_users_pedido_por_id",
                        column: x => x.pedido_por_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_operador_id",
                table: "asp_net_users",
                column: "operador_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_alteracao_circulacao_id",
                table: "pedidos_alteracao",
                column: "circulacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_alteracao_estado_criado_em",
                table: "pedidos_alteracao",
                columns: new[] { "estado", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_alteracao_pedido_por_id",
                table: "pedidos_alteracao",
                column: "pedido_por_id");

            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_users_operadores_operador_id",
                table: "asp_net_users",
                column: "operador_id",
                principalTable: "operadores",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_operadores_asp_net_users_owner_id",
                table: "operadores",
                column: "owner_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_asp_net_users_operadores_operador_id",
                table: "asp_net_users");

            migrationBuilder.DropForeignKey(
                name: "fk_operadores_asp_net_users_owner_id",
                table: "operadores");

            migrationBuilder.DropTable(
                name: "pedidos_alteracao");

            migrationBuilder.DropIndex(
                name: "ix_asp_net_users_operador_id",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "operador_id",
                table: "asp_net_users");

            migrationBuilder.AddForeignKey(
                name: "fk_operadores_users_owner_id",
                table: "operadores",
                column: "owner_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
