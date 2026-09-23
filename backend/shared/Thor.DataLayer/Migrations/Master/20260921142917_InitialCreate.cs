using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Thor.DataLayer.Migrations.Master
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "auth");

            migrationBuilder.EnsureSchema(
                name: "master");

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
                name: "connector_types",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false, comment: "Fixed, manually-assigned id — not identity. Do not renumber existing rows; tenant-DB data references these ids with no physical FK to enforce it."),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_types", x => x.id);
                },
                comment: "Canonical connector-type lookup (id -> name), see ADR §6.2. Every tenant-DB table with a connector_type column (Source, Account, Grp, Asset, Entitlement, their Staging counterparts, and ScanConnectorConfigValue) references this table's id at the application level only — no physical FK, since those tables live in a separate per-tenant database. Within the Master DB itself, AuthenticationType and ConnectorConfigField reference it with a real FK.");

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
                name: "authentication_types",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    connector_type = table.Column<string>(type: "text", nullable: false),
                    ConnectorTypeId = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authentication_types", x => x.id);
                    table.ForeignKey(
                        name: "FK_authentication_types_connector_types_ConnectorTypeId",
                        column: x => x.ConnectorTypeId,
                        principalSchema: "master",
                        principalTable: "connector_types",
                        principalColumn: "id");
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
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectorTypeId = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_config_fields", x => x.id);
                    table.ForeignKey(
                        name: "FK_connector_config_fields_connector_types_ConnectorTypeId",
                        column: x => x.ConnectorTypeId,
                        principalSchema: "master",
                        principalTable: "connector_types",
                        principalColumn: "id");
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
                name: "tenant_log_sink_config",
                schema: "auth",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    http_endpoint = table.Column<string>(type: "text", nullable: false),
                    batch_size_limit = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_log_sink_config", x => x.tenant_id);
                    table.ForeignKey(
                        name: "FK_tenant_log_sink_config_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "auth",
                        principalTable: "tenant",
                        principalColumn: "tenant_id",
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
                    db_user = table.Column<string>(type: "text", nullable: false),
                    read_only_db_user = table.Column<string>(type: "text", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "authentication_fields",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    input_type = table.Column<string>(type: "text", nullable: false),
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
                name: "IX_authentication_fields_type_id",
                schema: "master",
                table: "authentication_fields",
                column: "type_id");

            migrationBuilder.CreateIndex(
                name: "IX_authentication_types_ConnectorTypeId",
                schema: "master",
                table: "authentication_types",
                column: "ConnectorTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_connector_config_fields_ConnectorTypeId",
                schema: "master",
                table: "connector_config_fields",
                column: "ConnectorTypeId");

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
                name: "authentication_fields",
                schema: "master");

            migrationBuilder.DropTable(
                name: "connector_config_fields",
                schema: "master");

            migrationBuilder.DropTable(
                name: "key_scope_map",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "task_api_refresh_token",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "tenant_log_sink_config",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "tenant_routing",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "authentication_types",
                schema: "master");

            migrationBuilder.DropTable(
                name: "api_scopes",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "tenant_api_key",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "connector_types",
                schema: "master");

            migrationBuilder.DropTable(
                name: "tenant",
                schema: "auth");
        }
    }
}
