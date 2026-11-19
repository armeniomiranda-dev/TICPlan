using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class PedidoCalendario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pedido_dias",
                columns: table => new
                {
                    pedido_id = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedido_dias", x => new { x.pedido_id, x.data });
                    table.ForeignKey(
                        name: "fk_pedido_dias_pedidos_alteracao_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos_alteracao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pedido_dias");
        }
    }
}
