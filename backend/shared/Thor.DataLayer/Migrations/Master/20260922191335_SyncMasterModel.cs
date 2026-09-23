using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Thor.DataLayer.Migrations.Master
{
    /// <inheritdoc />
    public partial class SyncMasterModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_authentication_types_connector_types_ConnectorTypeId",
                schema: "master",
                table: "authentication_types");

            migrationBuilder.DropForeignKey(
                name: "FK_connector_config_fields_connector_types_ConnectorTypeId",
                schema: "master",
                table: "connector_config_fields");

            migrationBuilder.DropColumn(
                name: "connector_type",
                schema: "master",
                table: "connector_config_fields");

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

            migrationBuilder.RenameColumn(
                name: "ConnectorTypeId",
                schema: "master",
                table: "authentication_types",
                newName: "connector_type_id");

            migrationBuilder.RenameIndex(
                name: "IX_authentication_types_ConnectorTypeId",
                schema: "master",
                table: "authentication_types",
                newName: "IX_authentication_types_connector_type_id");

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

            migrationBuilder.AlterColumn<short>(
                name: "connector_type_id",
                schema: "master",
                table: "authentication_types",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_authentication_types_connector_types_connector_type_id",
                schema: "master",
                table: "authentication_types",
                column: "connector_type_id",
                principalSchema: "master",
                principalTable: "connector_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

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
                name: "FK_authentication_types_connector_types_connector_type_id",
                schema: "master",
                table: "authentication_types");

            migrationBuilder.DropForeignKey(
                name: "FK_connector_config_fields_connector_types_connector_type_id",
                schema: "master",
                table: "connector_config_fields");

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

            migrationBuilder.RenameColumn(
                name: "connector_type_id",
                schema: "master",
                table: "authentication_types",
                newName: "ConnectorTypeId");

            migrationBuilder.RenameIndex(
                name: "IX_authentication_types_connector_type_id",
                schema: "master",
                table: "authentication_types",
                newName: "IX_authentication_types_ConnectorTypeId");

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

            migrationBuilder.AlterColumn<short>(
                name: "ConnectorTypeId",
                schema: "master",
                table: "authentication_types",
                type: "smallint",
                nullable: true,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddColumn<string>(
                name: "connector_type",
                schema: "master",
                table: "authentication_types",
                type: "text",
                nullable: false,
                defaultValue: "");

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
