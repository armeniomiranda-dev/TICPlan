using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AppTIC.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeclaracoesIdTicGrupos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "circulacao_id",
                table: "pedidos_alteracao",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<bool>(
                name: "aplicado",
                table: "pedidos_alteracao",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "designacao_linha",
                table: "pedidos_alteracao",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "destino_id",
                table: "pedidos_alteracao",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dias_semana",
                table: "pedidos_alteracao",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "linha_id",
                table: "pedidos_alteracao",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "operador_id",
                table: "pedidos_alteracao",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "origem_id",
                table: "pedidos_alteracao",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo",
                table: "pedidos_alteracao",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "viagem_id",
                table: "pedidos_alteracao",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "nif",
                table: "operadores",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<bool>(
                name: "e_grupo",
                table: "operadores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "grupo_id",
                table: "operadores",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "prefixo_id_tic",
                table: "operadores",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "declaracao_id",
                table: "circulacoes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "declaracoes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ano = table.Column<int>(type: "integer", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    versao = table.Column<int>(type: "integer", nullable: false),
                    operador_id = table.Column<int>(type: "integer", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_declaracoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_declaracoes_operadores_operador_id",
                        column: x => x.operador_id,
                        principalTable: "operadores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "declaracao_versoes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    declaracao_id = table.Column<int>(type: "integer", nullable: false),
                    versao = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    utilizador = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    circulacao_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_declaracao_versoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_declaracao_versoes_declaracoes_declaracao_id",
                        column: x => x.declaracao_id,
                        principalTable: "declaracoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_alteracao_destino_id",
                table: "pedidos_alteracao",
                column: "destino_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_alteracao_operador_id",
                table: "pedidos_alteracao",
                column: "operador_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_alteracao_origem_id",
                table: "pedidos_alteracao",
                column: "origem_id");

            migrationBuilder.CreateIndex(
                name: "ix_operadores_grupo_id",
                table: "operadores",
                column: "grupo_id");

            migrationBuilder.CreateIndex(
                name: "ix_circulacoes_declaracao_id",
                table: "circulacoes",
                column: "declaracao_id");

            migrationBuilder.CreateIndex(
                name: "ix_circulacoes_id_tic",
                table: "circulacoes",
                column: "id_tic");

            migrationBuilder.CreateIndex(
                name: "ix_declaracao_versoes_declaracao_id_versao",
                table: "declaracao_versoes",
                columns: new[] { "declaracao_id", "versao" });

            migrationBuilder.CreateIndex(
                name: "ix_declaracoes_ano_numero",
                table: "declaracoes",
                columns: new[] { "ano", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_declaracoes_operador_id",
                table: "declaracoes",
                column: "operador_id");

            // Pedidos existentes: são alterações; o operador é o da circulação.
            migrationBuilder.Sql("""
                UPDATE pedidos_alteracao p SET operador_id = c.operador_id FROM circulacoes c WHERE c.id = p.circulacao_id;
                UPDATE pedidos_alteracao SET tipo = 'Alteracao' WHERE tipo = '';
                """);

            // id_DCP existente → declarações Ano.Numero.Versao ("2026.62.01", "62.01" ou "62"; sem ano, o do lote ou o atual).
            migrationBuilder.Sql("""
                CREATE TEMP TABLE tmp_dcp AS
                SELECT id, operador_id,
                       CASE WHEN cardinality(p) = 3 THEN p[1]::int ELSE ano_def END AS ano,
                       CASE WHEN cardinality(p) = 3 THEN p[2]::int ELSE p[1]::int END AS numero,
                       CASE WHEN cardinality(p) = 3 THEN p[3]::int WHEN cardinality(p) = 2 THEN p[2]::int ELSE 0 END AS versao
                FROM (
                    SELECT c.id, c.operador_id, string_to_array(trim(c.id_dcp), '.') AS p,
                           COALESCE(EXTRACT(YEAR FROM i.data_inicio)::int, EXTRACT(YEAR FROM now())::int) AS ano_def
                    FROM circulacoes c LEFT JOIN importacoes i ON i.id = c.importacao_id
                    WHERE trim(c.id_dcp) ~ '^\d{1,6}(\.\d{1,6}){0,2}$'
                ) x
                WHERE cardinality(p) < 3 OR p[1]::int >= 2000;

                INSERT INTO declaracoes (ano, numero, versao, operador_id, criado_em, atualizado_em)
                SELECT ano, numero, max(versao), min(operador_id), now(), now() FROM tmp_dcp GROUP BY ano, numero;

                INSERT INTO declaracao_versoes (declaracao_id, versao, data, motivo)
                SELECT id, versao, now(), 'Migração do id_DCP existente.' FROM declaracoes;

                UPDATE circulacoes c
                SET declaracao_id = d.id, id_dcp = d.ano || '.' || d.numero || '.' || lpad(d.versao::text, 2, '0')
                FROM tmp_dcp t JOIN declaracoes d ON d.ano = t.ano AND d.numero = t.numero
                WHERE c.id = t.id;

                DROP TABLE tmp_dcp;
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_circulacoes_declaracoes_declaracao_id",
                table: "circulacoes",
                column: "declaracao_id",
                principalTable: "declaracoes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_operadores_operadores_grupo_id",
                table: "operadores",
                column: "grupo_id",
                principalTable: "operadores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pedidos_alteracao_operadores_operador_id",
                table: "pedidos_alteracao",
                column: "operador_id",
                principalTable: "operadores",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_pedidos_alteracao_paragens_destino_id",
                table: "pedidos_alteracao",
                column: "destino_id",
                principalTable: "paragens",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_pedidos_alteracao_paragens_origem_id",
                table: "pedidos_alteracao",
                column: "origem_id",
                principalTable: "paragens",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_circulacoes_declaracoes_declaracao_id",
                table: "circulacoes");

            migrationBuilder.DropForeignKey(
                name: "fk_operadores_operadores_grupo_id",
                table: "operadores");

            migrationBuilder.DropForeignKey(
                name: "fk_pedidos_alteracao_operadores_operador_id",
                table: "pedidos_alteracao");

            migrationBuilder.DropForeignKey(
                name: "fk_pedidos_alteracao_paragens_destino_id",
                table: "pedidos_alteracao");

            migrationBuilder.DropForeignKey(
                name: "fk_pedidos_alteracao_paragens_origem_id",
                table: "pedidos_alteracao");

            migrationBuilder.DropTable(
                name: "declaracao_versoes");

            migrationBuilder.DropTable(
                name: "declaracoes");

            migrationBuilder.DropIndex(
                name: "ix_pedidos_alteracao_destino_id",
                table: "pedidos_alteracao");

            migrationBuilder.DropIndex(
                name: "ix_pedidos_alteracao_operador_id",
                table: "pedidos_alteracao");

            migrationBuilder.DropIndex(
                name: "ix_pedidos_alteracao_origem_id",
                table: "pedidos_alteracao");

            migrationBuilder.DropIndex(
                name: "ix_operadores_grupo_id",
                table: "operadores");

            migrationBuilder.DropIndex(
                name: "ix_circulacoes_declaracao_id",
                table: "circulacoes");

            migrationBuilder.DropIndex(
                name: "ix_circulacoes_id_tic",
                table: "circulacoes");

            migrationBuilder.DropColumn(
                name: "aplicado",
                table: "pedidos_alteracao");

            migrationBuilder.DropColumn(
                name: "designacao_linha",
                table: "pedidos_alteracao");

            migrationBuilder.DropColumn(
                name: "destino_id",
                table: "pedidos_alteracao");

            migrationBuilder.DropColumn(
                name: "dias_semana",
                table: "pedidos_alteracao");

            migrationBuilder.DropColumn(
                name: "linha_id",
                table: "pedidos_alteracao");

            migrationBuilder.DropColumn(
                name: "operador_id",
                table: "pedidos_alteracao");

            migrationBuilder.DropColumn(
                name: "origem_id",
                table: "pedidos_alteracao");

            migrationBuilder.DropColumn(
                name: "tipo",
                table: "pedidos_alteracao");

            migrationBuilder.DropColumn(
                name: "viagem_id",
                table: "pedidos_alteracao");

            migrationBuilder.DropColumn(
                name: "e_grupo",
                table: "operadores");

            migrationBuilder.DropColumn(
                name: "grupo_id",
                table: "operadores");

            migrationBuilder.DropColumn(
                name: "prefixo_id_tic",
                table: "operadores");

            migrationBuilder.DropColumn(
                name: "declaracao_id",
                table: "circulacoes");

            migrationBuilder.AlterColumn<int>(
                name: "circulacao_id",
                table: "pedidos_alteracao",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "nif",
                table: "operadores",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
