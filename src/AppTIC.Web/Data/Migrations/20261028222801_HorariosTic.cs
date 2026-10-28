using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class HorariosTic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "circulacao_horarios");

            migrationBuilder.CreateTable(
                name: "circulacao_horarios_tic",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    circulacao_id = table.Column<int>(type: "integer", nullable: false),
                    data_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    data_fim = table.Column<DateOnly>(type: "date", nullable: false),
                    chegada = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    partida = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    origem = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    registado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_circulacao_horarios_tic", x => x.id);
                    table.CheckConstraint("ck_circulacao_horarios_tic_periodo", "data_fim >= data_inicio");
                    table.ForeignKey(
                        name: "fk_circulacao_horarios_tic_circulacoes_circulacao_id",
                        column: x => x.circulacao_id,
                        principalTable: "circulacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_circulacao_horarios_tic_circulacao_id_data_inicio",
                table: "circulacao_horarios_tic",
                columns: new[] { "circulacao_id", "data_inicio" });

            // Histórico inicial: as horas atuais de cada circulação, do primeiro ao último dia do calendário.
            migrationBuilder.Sql("""
                INSERT INTO circulacao_horarios_tic (circulacao_id, data_inicio, data_fim, chegada, partida, origem, registado_em)
                SELECT c.id, MIN(d.data), MAX(d.data), c.chegada, c.partida,
                       CASE WHEN c.gtfs_feed_id IS NULL THEN 'Manual' ELSE 'Gtfs' END, now()
                FROM circulacoes c JOIN circulacao_dias d ON d.circulacao_id = c.id
                WHERE c.chegada IS NOT NULL OR c.partida IS NOT NULL
                GROUP BY c.id, c.chegada, c.partida, c.gtfs_feed_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "circulacao_horarios_tic");

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
        }
    }
}
