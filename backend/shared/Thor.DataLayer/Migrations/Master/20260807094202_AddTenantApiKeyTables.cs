using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Thor.DataLayer.Migrations.Master
{
    /// <inheritdoc />
    public partial class AddTenantApiKeyTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_scopes",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    scope_text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_api_key",
                schema: "auth",
                columns: table => new
                {
                    key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<string>(type: "text", nullable: false),
                    salt = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: true),
                    status_id = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_api_key", x => x.key_id);
                    table.ForeignKey(
                        name: "FK_tenant_api_key_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "auth",
                        principalTable: "tenant",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "key_scope_map",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    api_key_uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_id = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_key_scope_map", x => x.id);
                    table.ForeignKey(
                        name: "FK_key_scope_map_api_scopes_scope_id",
                        column: x => x.scope_id,
                        principalSchema: "auth",
                        principalTable: "api_scopes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_key_scope_map_tenant_api_key_api_key_uuid",
                        column: x => x.api_key_uuid,
                        principalSchema: "auth",
                        principalTable: "tenant_api_key",
                        principalColumn: "key_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_api_refresh_token",
                schema: "auth",
                columns: table => new
                {
                    token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    salt = table.Column<string>(type: "text", nullable: false),
                    role_type = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_api_refresh_token", x => x.token_id);
                    table.ForeignKey(
                        name: "FK_task_api_refresh_token_tenant_api_key_key_id",
                        column: x => x.key_id,
                        principalSchema: "auth",
                        principalTable: "tenant_api_key",
                        principalColumn: "key_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_key_scope_map_api_key_uuid",
                schema: "auth",
                table: "key_scope_map",
                column: "api_key_uuid");

            migrationBuilder.CreateIndex(
                name: "IX_key_scope_map_scope_id",
                schema: "auth",
                table: "key_scope_map",
                column: "scope_id");

            migrationBuilder.CreateIndex(
                name: "IX_task_api_refresh_token_key_id",
                schema: "auth",
                table: "task_api_refresh_token",
                column: "key_id");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_api_key_tenant_id",
                schema: "auth",
                table: "tenant_api_key",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "key_scope_map",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "task_api_refresh_token",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "api_scopes",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "tenant_api_key",
                schema: "auth");
        }
    }
}
