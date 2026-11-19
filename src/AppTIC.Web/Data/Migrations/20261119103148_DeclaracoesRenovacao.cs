using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeclaracoesRenovacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "anterior_id",
                table: "declaracoes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "declaracao_circulacoes",
                columns: table => new
                {
                    declaracao_id = table.Column<int>(type: "integer", nullable: false),
                    circulacao_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_declaracao_circulacoes", x => new { x.declaracao_id, x.circulacao_id });
                    table.ForeignKey(
                        name: "fk_declaracao_circulacoes_circulacoes_circulacao_id",
                        column: x => x.circulacao_id,
                        principalTable: "circulacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_declaracao_circulacoes_declaracoes_declaracao_id",
                        column: x => x.declaracao_id,
                        principalTable: "declaracoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_declaracoes_anterior_id",
                table: "declaracoes",
                column: "anterior_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_declaracao_circulacoes_circulacao_id",
                table: "declaracao_circulacoes",
                column: "circulacao_id");

            migrationBuilder.AddForeignKey(
                name: "fk_declaracoes_declaracoes_anterior_id",
                table: "declaracoes",
                column: "anterior_id",
                principalTable: "declaracoes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_declaracoes_declaracoes_anterior_id",
                table: "declaracoes");

            migrationBuilder.DropTable(
                name: "declaracao_circulacoes");

            migrationBuilder.DropIndex(
                name: "ix_declaracoes_anterior_id",
                table: "declaracoes");

            migrationBuilder.DropColumn(
                name: "anterior_id",
                table: "declaracoes");
        }
    }
}
