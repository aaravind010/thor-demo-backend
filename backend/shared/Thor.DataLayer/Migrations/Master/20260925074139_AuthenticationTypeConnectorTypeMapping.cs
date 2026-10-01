using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Thor.DataLayer.Migrations.Master
{
    /// <inheritdoc />
    public partial class AuthenticationTypeConnectorTypeMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // connector_config_fields.connector_type_id becomes NOT NULL below, and EF backfills
            // NULLs with 0 — not a valid connector_types id, so the FK would then fail. Stop with a
            // clear message instead of guessing a connector for those rows.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM master.connector_config_fields WHERE "ConnectorTypeId" IS NULL) THEN
                        RAISE EXCEPTION 'master.connector_config_fields has rows with NULL ConnectorTypeId; assign a connector type to them before applying this migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_authentication_types_connector_types_ConnectorTypeId",
                schema: "master",
                table: "authentication_types");

            migrationBuilder.DropForeignKey(
                name: "FK_connector_config_fields_connector_types_ConnectorTypeId",
                schema: "master",
                table: "connector_config_fields");

            migrationBuilder.DropIndex(
                name: "IX_authentication_types_ConnectorTypeId",
                schema: "master",
                table: "authentication_types");

            migrationBuilder.DropColumn(
                name: "connector_type",
                schema: "master",
                table: "connector_config_fields");

            migrationBuilder.DropColumn(
                name: "ConnectorTypeId",
                schema: "master",
                table: "authentication_types");

            migrationBuilder.DropColumn(
                name: "connector_type",
                schema: "master",
                table: "authentication_types");

            migrationBuilder.RenameColumn(
                name: "ConnectorTypeId",
                schema: "master",
                table: "connector_config_fields",
                newName: "connector_type_id");

            migrationBuilder.RenameIndex(
                name: "IX_connector_config_fields_ConnectorTypeId",
                schema: "master",
                table: "connector_config_fields",
                newName: "IX_connector_config_fields_connector_type_id");

            migrationBuilder.AlterTable(
                name: "connector_types",
                schema: "master",
                comment: "Canonical connector-type lookup (id -> name), see ADR §6.2. Every tenant-DB table with a connector_type column (Source, Account, Grp, Asset, Entitlement, their Staging counterparts, and ScanConnectorConfigValue) references this table's id at the application level only — no physical FK, since those tables live in a separate per-tenant database. Within the Master DB itself, AuthenticationTypeConnectorType and ConnectorConfigField reference it with a real FK.",
                oldComment: "Canonical connector-type lookup (id -> name), see ADR §6.2. Every tenant-DB table with a connector_type column (Source, Account, Grp, Asset, Entitlement, their Staging counterparts, and ScanConnectorConfigValue) references this table's id at the application level only — no physical FK, since those tables live in a separate per-tenant database. Within the Master DB itself, AuthenticationType and ConnectorConfigField reference it with a real FK.");

            migrationBuilder.AlterColumn<short>(
                name: "connector_type_id",
                schema: "master",
                table: "connector_config_fields",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "authentication_type_connector_types",
                schema: "master",
                columns: table => new
                {
                    authentication_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_type_id = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authentication_type_connector_types", x => new { x.authentication_type_id, x.connector_type_id });
                    table.ForeignKey(
                        name: "FK_authentication_type_connector_types_authentication_types_au~",
                        column: x => x.authentication_type_id,
                        principalSchema: "master",
                        principalTable: "authentication_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_authentication_type_connector_types_connector_types_connect~",
                        column: x => x.connector_type_id,
                        principalSchema: "master",
                        principalTable: "connector_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_authentication_type_connector_types_connector_type_id",
                schema: "master",
                table: "authentication_type_connector_types",
                column: "connector_type_id");

            migrationBuilder.AddForeignKey(
                name: "FK_connector_config_fields_connector_types_connector_type_id",
                schema: "master",
                table: "connector_config_fields",
                column: "connector_type_id",
                principalSchema: "master",
                principalTable: "connector_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_connector_config_fields_connector_types_connector_type_id",
                schema: "master",
                table: "connector_config_fields");

            migrationBuilder.DropTable(
                name: "authentication_type_connector_types",
                schema: "master");

            migrationBuilder.RenameColumn(
                name: "connector_type_id",
                schema: "master",
                table: "connector_config_fields",
                newName: "ConnectorTypeId");

            migrationBuilder.RenameIndex(
                name: "IX_connector_config_fields_connector_type_id",
                schema: "master",
                table: "connector_config_fields",
                newName: "IX_connector_config_fields_ConnectorTypeId");

            migrationBuilder.AlterTable(
                name: "connector_types",
                schema: "master",
                comment: "Canonical connector-type lookup (id -> name), see ADR §6.2. Every tenant-DB table with a connector_type column (Source, Account, Grp, Asset, Entitlement, their Staging counterparts, and ScanConnectorConfigValue) references this table's id at the application level only — no physical FK, since those tables live in a separate per-tenant database. Within the Master DB itself, AuthenticationType and ConnectorConfigField reference it with a real FK.",
                oldComment: "Canonical connector-type lookup (id -> name), see ADR §6.2. Every tenant-DB table with a connector_type column (Source, Account, Grp, Asset, Entitlement, their Staging counterparts, and ScanConnectorConfigValue) references this table's id at the application level only — no physical FK, since those tables live in a separate per-tenant database. Within the Master DB itself, AuthenticationTypeConnectorType and ConnectorConfigField reference it with a real FK.");

            migrationBuilder.AlterColumn<short>(
                name: "ConnectorTypeId",
                schema: "master",
                table: "connector_config_fields",
                type: "smallint",
                nullable: true,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddColumn<string>(
                name: "connector_type",
                schema: "master",
                table: "connector_config_fields",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<short>(
                name: "ConnectorTypeId",
                schema: "master",
                table: "authentication_types",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "connector_type",
                schema: "master",
                table: "authentication_types",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_authentication_types_ConnectorTypeId",
                schema: "master",
                table: "authentication_types",
                column: "ConnectorTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_authentication_types_connector_types_ConnectorTypeId",
                schema: "master",
                table: "authentication_types",
                column: "ConnectorTypeId",
                principalSchema: "master",
                principalTable: "connector_types",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_connector_config_fields_connector_types_ConnectorTypeId",
                schema: "master",
                table: "connector_config_fields",
                column: "ConnectorTypeId",
                principalSchema: "master",
                principalTable: "connector_types",
                principalColumn: "id");
        }
    }
}
