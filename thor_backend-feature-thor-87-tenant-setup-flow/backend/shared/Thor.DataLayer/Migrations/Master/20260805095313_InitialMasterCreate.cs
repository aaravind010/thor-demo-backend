using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Thor.DataLayer.Migrations.Master
{
    /// <inheritdoc />
    public partial class InitialMasterCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "master");

            migrationBuilder.CreateTable(
                name: "authentication_types",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    connector_type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authentication_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "connector_config_fields",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_type = table.Column<string>(type: "text", nullable: false),
                    field_name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    input_type = table.Column<string>(type: "text", nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_config_fields", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant",
                schema: "auth",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    subdomain = table.Column<string>(type: "text", nullable: false),
                    tier_id = table.Column<short>(type: "smallint", nullable: false),
                    isolation_type_id = table.Column<short>(type: "smallint", nullable: false),
                    status_id = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "authentication_fields",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authentication_fields", x => x.id);
                    table.ForeignKey(
                        name: "FK_authentication_fields_authentication_types_type_id",
                        column: x => x.type_id,
                        principalSchema: "master",
                        principalTable: "authentication_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_routing",
                schema: "auth",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cluster_endpoint = table.Column<string>(type: "text", nullable: false),
                    database_name = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    user_pool_id = table.Column<string>(type: "text", nullable: false),
                    app_client_Id = table.Column<string>(type: "text", nullable: false),
                    secret_arn = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_routing", x => x.tenant_id);
                    table.ForeignKey(
                        name: "FK_tenant_routing_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "auth",
                        principalTable: "tenant",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_authentication_fields_type_id",
                schema: "master",
                table: "authentication_fields",
                column: "type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authentication_fields",
                schema: "master");

            migrationBuilder.DropTable(
                name: "connector_config_fields",
                schema: "master");

            migrationBuilder.DropTable(
                name: "tenant_routing",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "authentication_types",
                schema: "master");

            migrationBuilder.DropTable(
                name: "tenant",
                schema: "auth");
        }
    }
}
