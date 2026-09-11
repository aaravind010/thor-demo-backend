using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Thor.DataLayer.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class InitialTenantCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenant");

            migrationBuilder.CreateTable(
                name: "authentication_methods",
                schema: "tenant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authentication_methods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source",
                schema: "tenant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_type = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    config = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "authentication_values",
                schema: "tenant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authentication_values", x => x.id);
                    table.ForeignKey(
                        name: "FK_authentication_values_authentication_methods_method_id",
                        column: x => x.method_id,
                        principalSchema: "tenant",
                        principalTable: "authentication_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan_config",
                schema: "tenant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    auth_method = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_config", x => x.id);
                    table.ForeignKey(
                        name: "FK_scan_config_authentication_methods_auth_method",
                        column: x => x.auth_method,
                        principalSchema: "tenant",
                        principalTable: "authentication_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan",
                schema: "tenant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_tasks = table.Column<int>(type: "integer", nullable: false),
                    completed_tasks = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ingestion_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    analysis_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan", x => x.id);
                    table.ForeignKey(
                        name: "FK_scan_scan_config_scan_config_id",
                        column: x => x.scan_config_id,
                        principalSchema: "tenant",
                        principalTable: "scan_config",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan_connector_config_values",
                schema: "tenant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connector_type = table.Column<string>(type: "text", nullable: false),
                    config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_connector_config_values", x => x.id);
                    table.ForeignKey(
                        name: "FK_scan_connector_config_values_scan_config_scan_config_id",
                        column: x => x.scan_config_id,
                        principalSchema: "tenant",
                        principalTable: "scan_config",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan_source_mapping",
                schema: "tenant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_source_mapping", x => x.id);
                    table.ForeignKey(
                        name: "FK_scan_source_mapping_scan_config_scan_config_id",
                        column: x => x.scan_config_id,
                        principalSchema: "tenant",
                        principalTable: "scan_config",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scan_source_mapping_source_source_id",
                        column: x => x.source_id,
                        principalSchema: "tenant",
                        principalTable: "source",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "scan_task",
                schema: "tenant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_task", x => x.id);
                    table.ForeignKey(
                        name: "FK_scan_task_scan_scan_id",
                        column: x => x.scan_id,
                        principalSchema: "tenant",
                        principalTable: "scan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scan_task_source_source_id",
                        column: x => x.source_id,
                        principalSchema: "tenant",
                        principalTable: "source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow",
                schema: "tenant",
                columns: table => new
                {
                    workflow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_type = table.Column<string>(type: "text", nullable: false),
                    scheduled_by = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    timeout_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow", x => x.workflow_id);
                    table.ForeignKey(
                        name: "FK_workflow_scan_scan_id",
                        column: x => x.scan_id,
                        principalSchema: "tenant",
                        principalTable: "scan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_graph",
                schema: "tenant",
                columns: table => new
                {
                    parent_workflow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_workflow_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_graph", x => new { x.parent_workflow_id, x.child_workflow_id });
                    table.ForeignKey(
                        name: "FK_workflow_graph_workflow_child_workflow_id",
                        column: x => x.child_workflow_id,
                        principalSchema: "tenant",
                        principalTable: "workflow",
                        principalColumn: "workflow_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workflow_graph_workflow_parent_workflow_id",
                        column: x => x.parent_workflow_id,
                        principalSchema: "tenant",
                        principalTable: "workflow",
                        principalColumn: "workflow_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_authentication_values_method_id",
                schema: "tenant",
                table: "authentication_values",
                column: "method_id");

            migrationBuilder.CreateIndex(
                name: "IX_scan_scan_config_id",
                schema: "tenant",
                table: "scan",
                column: "scan_config_id");

            migrationBuilder.CreateIndex(
                name: "IX_scan_config_auth_method",
                schema: "tenant",
                table: "scan_config",
                column: "auth_method");

            migrationBuilder.CreateIndex(
                name: "IX_scan_connector_config_values_scan_config_id",
                schema: "tenant",
                table: "scan_connector_config_values",
                column: "scan_config_id");

            migrationBuilder.CreateIndex(
                name: "IX_scan_source_mapping_scan_config_id",
                schema: "tenant",
                table: "scan_source_mapping",
                column: "scan_config_id");

            migrationBuilder.CreateIndex(
                name: "IX_scan_source_mapping_source_id",
                schema: "tenant",
                table: "scan_source_mapping",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "IX_scan_task_scan_id",
                schema: "tenant",
                table: "scan_task",
                column: "scan_id");

            migrationBuilder.CreateIndex(
                name: "IX_scan_task_source_id",
                schema: "tenant",
                table: "scan_task",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_scan_id",
                schema: "tenant",
                table: "workflow",
                column: "scan_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_graph_child_workflow_id",
                schema: "tenant",
                table: "workflow_graph",
                column: "child_workflow_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authentication_values",
                schema: "tenant");

            migrationBuilder.DropTable(
                name: "scan_connector_config_values",
                schema: "tenant");

            migrationBuilder.DropTable(
                name: "scan_source_mapping",
                schema: "tenant");

            migrationBuilder.DropTable(
                name: "scan_task",
                schema: "tenant");

            migrationBuilder.DropTable(
                name: "workflow_graph",
                schema: "tenant");

            migrationBuilder.DropTable(
                name: "source",
                schema: "tenant");

            migrationBuilder.DropTable(
                name: "workflow",
                schema: "tenant");

            migrationBuilder.DropTable(
                name: "scan",
                schema: "tenant");

            migrationBuilder.DropTable(
                name: "scan_config",
                schema: "tenant");

            migrationBuilder.DropTable(
                name: "authentication_methods",
                schema: "tenant");
        }
    }
}
