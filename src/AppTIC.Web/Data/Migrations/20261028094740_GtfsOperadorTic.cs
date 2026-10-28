using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class GtfsOperadorTic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "gtfs_paragens_tic",
                table: "operadores",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "caminho_filtrado",
                table: "gtfs_feeds",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "num_horarios_tic",
                table: "gtfs_feeds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "num_paragens_tic",
                table: "gtfs_feeds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "num_rotas_tic",
                table: "gtfs_feeds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "num_viagens_tic",
                table: "gtfs_feeds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "operador_id",
                table: "gtfs_feeds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paragens_tic",
                table: "gtfs_feeds",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "tamanho_filtrado_bytes",
                table: "gtfs_feeds",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_gtfs_feeds_operador_id",
                table: "gtfs_feeds",
                column: "operador_id");

            migrationBuilder.AddForeignKey(
                name: "fk_gtfs_feeds_operadores_operador_id",
                table: "gtfs_feeds",
                column: "operador_id",
                principalTable: "operadores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_gtfs_feeds_operadores_operador_id",
                table: "gtfs_feeds");

            migrationBuilder.DropIndex(
                name: "ix_gtfs_feeds_operador_id",
                table: "gtfs_feeds");

            migrationBuilder.DropColumn(
                name: "gtfs_paragens_tic",
                table: "operadores");

            migrationBuilder.DropColumn(
                name: "caminho_filtrado",
                table: "gtfs_feeds");

            migrationBuilder.DropColumn(
                name: "num_horarios_tic",
                table: "gtfs_feeds");

            migrationBuilder.DropColumn(
                name: "num_paragens_tic",
                table: "gtfs_feeds");

            migrationBuilder.DropColumn(
                name: "num_rotas_tic",
                table: "gtfs_feeds");

            migrationBuilder.DropColumn(
                name: "num_viagens_tic",
                table: "gtfs_feeds");

            migrationBuilder.DropColumn(
                name: "operador_id",
                table: "gtfs_feeds");

            migrationBuilder.DropColumn(
                name: "paragens_tic",
                table: "gtfs_feeds");

            migrationBuilder.DropColumn(
                name: "tamanho_filtrado_bytes",
                table: "gtfs_feeds");
        }
    }
}
