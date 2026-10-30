using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CategoriaInternacional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "categoria",
                table: "circulacoes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            // A categoria "Fora" (cais 5–8) passou a chamar-se "Internacional".
            migrationBuilder.Sql("UPDATE circulacoes SET categoria = 'Internacional' WHERE categoria = 'Fora';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE circulacoes SET categoria = 'Fora' WHERE categoria = 'Internacional';");

            migrationBuilder.AlterColumn<string>(
                name: "categoria",
                table: "circulacoes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);
        }
    }
}
