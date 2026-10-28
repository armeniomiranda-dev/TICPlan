using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CirculacaoHorarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chegada",
                table: "percursos");

            migrationBuilder.DropColumn(
                name: "partida",
                table: "percursos");

            migrationBuilder.AddColumn<int>(
                name: "gtfs_feed_id",
                table: "circulacoes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "circulacao_horarios",
                columns: table => new
                {
                    circulacao_id = table.Column<int>(type: "integer", nullable: false),
                    sequencia = table.Column<int>(type: "integer", nullable: false),
                    chegada = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    partida = table.Column<TimeOnly>(type: "time without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_circulacao_horarios", x => new { x.circulacao_id, x.sequencia });
                    table.ForeignKey(
                        name: "fk_circulacao_horarios_percursos_circulacao_id_sequencia",
                        columns: x => new { x.circulacao_id, x.sequencia },
                        principalTable: "percursos",
                        principalColumns: new[] { "circulacao_id", "sequencia" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_circulacoes_gtfs_feed_id",
                table: "circulacoes",
                column: "gtfs_feed_id");

            migrationBuilder.AddForeignKey(
                name: "fk_circulacoes_gtfs_feeds_gtfs_feed_id",
                table: "circulacoes",
                column: "gtfs_feed_id",
                principalTable: "gtfs_feeds",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_circulacoes_gtfs_feeds_gtfs_feed_id",
                table: "circulacoes");

            migrationBuilder.DropTable(
                name: "circulacao_horarios");

            migrationBuilder.DropIndex(
                name: "ix_circulacoes_gtfs_feed_id",
                table: "circulacoes");

            migrationBuilder.DropColumn(
                name: "gtfs_feed_id",
                table: "circulacoes");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "chegada",
                table: "percursos",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "partida",
                table: "percursos",
                type: "time without time zone",
                nullable: true);
        }
    }
}
