using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CirculacaoCategoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "categoria",
                table: "circulacoes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "categoria",
                table: "circulacoes");
        }
    }
}
