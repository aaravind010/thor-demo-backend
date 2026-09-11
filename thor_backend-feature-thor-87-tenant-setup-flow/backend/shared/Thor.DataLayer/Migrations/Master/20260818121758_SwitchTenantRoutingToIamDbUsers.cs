using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Thor.DataLayer.Migrations.Master
{
    /// <inheritdoc />
    public partial class SwitchTenantRoutingToIamDbUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "secret_arn",
                schema: "auth",
                table: "tenant_routing",
                newName: "read_only_db_user");

            migrationBuilder.AddColumn<string>(
                name: "db_user",
                schema: "auth",
                table: "tenant_routing",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "db_user",
                schema: "auth",
                table: "tenant_routing");

            migrationBuilder.RenameColumn(
                name: "read_only_db_user",
                schema: "auth",
                table: "tenant_routing",
                newName: "secret_arn");
        }
    }
}
